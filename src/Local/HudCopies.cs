using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MicroWorksCoop.Local
{
    /// <summary>Marca um objeto que não é do personagem, mas é de um jogador extra (a cópia do HUD dele).</summary>
    class SeatOwner : MonoBehaviour
    {
        public int Seat;
    }

    /// <summary>
    /// O HUD da partida (rodada, título do minigame, placar, "VENCEU/FALHOU", dicas, setas) é um só no jogo:
    /// o do jogador local. Cada jogador extra ganha uma cópia dele, que fica na parte dele da tela.
    ///
    /// Quem mexe no HUD:
    /// - o código da partida (o mesmo para todos, roda uma vez): vai para o HUD de quem hospeda e se repete
    ///   em cada cópia, no contexto do jogador dela (pela internet, cada jogo rodaria esse código);
    /// - o código de um jogador extra (no contexto dele): vai só para a cópia dele;
    /// - o resultado da rodada e o vencedor: cada um vê o seu.
    /// </summary>
    static class HudCopies
    {
        // as telas do HUD que são de cada jogador; o resto (votação, lista de jogadores, pausa) segue um só
        static readonly HashSet<string> perPlayer = new HashSet<string> { "Game", "MiscItems", "IndicatorsCanvas", "CustomCanvas" };
        // os avisos (fulano entrou, votação, combo) são um só, na parte de quem hospeda: soltos, cobririam a de outro
        static readonly HashSet<string> hostPart = new HashSet<string> { "Notifications" };

        // o que só vale para quem hospeda quando chamado fora do contexto de um extra (cada extra tem o seu caminho)
        static readonly HashSet<string> hostOnly = new HashSet<string>
        {
            "ShowRoundResult", "ShowRoundResultNoScore", "ShowRespawnCountdown", "SetSpectatorRespawnText",
            "SetSpectatorRespawnTextActive", "ShowWinner", "ShowWinnerMultiple",
        };

        static readonly HashSet<Type> rootKeeps = new HashSet<Type>
        {
            typeof(MicrogameHUDManager), typeof(GenericUIElement), typeof(AutoInternationalization), typeof(SeasonalPainter),
        };

        static readonly Type[] hudTypes =
        {
            typeof(MicrogameHUDManager), typeof(ScoreboardManager), typeof(HealthUIManager), typeof(HintsUIHandler),
            typeof(ItemMessageUIHandler), typeof(TargetIndicatorManager),
        };

        class Copy
        {
            public GameObject Root;
            public MicrogameHUDManager Hud;
            public readonly Dictionary<int, Component> Twin = new Dictionary<int, Component>(); // original → cópia
            public Canvas[] Canvases;
        }

        static readonly Dictionary<int, Copy> copies = new Dictionary<int, Copy>();
        static readonly HashSet<string> failed = new HashSet<string>();
        static MicrogameHUDManager main;
        static Canvas[] mainCanvases = new Canvas[0];
        static bool patched, replaying;

        static readonly FieldInfo additiveDone = AccessTools.Field(typeof(GenericTweener), "additiveCalculationsFinished");

        static readonly Func<MicrogameCoordinator, bool, bool> postProcessWin =
            AccessTools.MethodDelegate<Func<MicrogameCoordinator, bool, bool>>(AccessTools.Method(typeof(MicrogameCoordinator), "PostProcessMicrogameWinState"));

        /// <summary>As telas do HUD da partida de um jogador (quem hospeda: as do jogo).</summary>
        public static Canvas[] CanvasesOf(int seat) =>
            seat < 0 ? mainCanvases : copies.TryGetValue(seat, out Copy c) ? c.Canvases : new Canvas[0];

        /// <summary>Chamado a cada quadro: uma cópia por jogador extra, refeitas quando o HUD do jogo muda (fase nova).</summary>
        public static void Tick()
        {
            MicrogameHUDManager hud = MicrogameCoordinator.singleton != null ? MicrogameCoordinator.singleton.HUDManager : null;
            if (hud != main)
            {
                foreach (int seat in copies.Keys.ToList()) Drop(seat);
                main = hud;
                mainCanvases = hud != null ? CanvasesUnder(hud.gameObject, hostPart) : new Canvas[0];
            }
            if (main == null) return;
            // já na chegada da fase, antes das animações do HUD (uma cópia no meio de uma animação fica torta), para
            // cada vaga ocupada no Hub; o personagem do jogador extra nasce um pouco depois
            Client hub = Plugin.Hub;
            for (int seat = 0; seat < Client.MaxSeats; seat++)
            {
                bool playing = SeatContext.PlayerOf(seat) != null
                    || seat != Plugin.Seat && hub != null && Plugin.Active && hub.GetSeatState(seat) != SeatState.Empty;
                // até o personagem nascer a cópia não tem parte na tela: fica escondida
                if (copies.TryGetValue(seat, out Copy shown))
                    foreach (Canvas canvas in shown.Canvases)
                        if (canvas != null && canvas.enabled != (SeatContext.PlayerOf(seat) != null)) canvas.enabled = !canvas.enabled;
                if (playing && !copies.ContainsKey(seat)) Make(seat);
                else if (!playing && copies.ContainsKey(seat)) Drop(seat);
            }
        }

        static Canvas[] CanvasesUnder(GameObject root, HashSet<string> also = null)
        {
            var list = new List<Canvas>();
            foreach (Transform child in root.transform)
                if ((perPlayer.Contains(child.name) || also != null && also.Contains(child.name)) && child.TryGetComponent(out Canvas canvas)) list.Add(canvas);
            return list.ToArray();
        }

        static void Make(int seat)
        {
            Patch();
            GameObject source = main.gameObject;
            // sob um pai desligado a cópia nasce sem rodar nada; o que não é de cada jogador sai antes de ligar
            var holder = new GameObject("PartyAnywhere.HudHolder");
            holder.SetActive(false);
            GameObject root = Object.Instantiate(source, holder.transform);
            root.name = $"{source.name} (P{seat + 1})";
            var copy = new Copy { Root = root };

            MonoBehaviour[] originals = source.GetComponentsInChildren<MonoBehaviour>(true);
            MonoBehaviour[] twins = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (originals.Length != twins.Length)
            {
                Plugin.Log.LogWarning($"HUD de P{seat + 1}: a cópia não bate com o original; sem HUD próprio");
                Object.Destroy(holder);
                copies[seat] = new Copy { Canvases = new Canvas[0] };
                return;
            }
            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null) continue;
                copy.Twin[originals[i].GetInstanceID()] = twins[i];
                // o animador soma a posição de partida aos destinos uma vez e marca num campo que não é copiado: a
                // cópia já tem a soma e somaria de novo (o HUD sairia do lugar)
                if (originals[i] is GenericTweener) additiveDone.SetValue(twins[i], additiveDone.GetValue(originals[i]));
            }
            foreach (Transform child in root.transform.Cast<Transform>().ToList())
                if (!perPlayer.Contains(child.name)) Object.DestroyImmediate(child.gameObject);
            // a cópia só mostra: sem as peças de rede (o Mirror apaga um objeto de cena repetido ao ligar) e, na raiz,
            // sem o que não é HUD (pausa, PDA, a espera pelos jogadores, que começaria as rodadas de novo)
            foreach (NetworkBehaviour nb in root.GetComponentsInChildren<NetworkBehaviour>(true)) Object.DestroyImmediate(nb);
            foreach (NetworkIdentity ni in root.GetComponentsInChildren<NetworkIdentity>(true)) Object.DestroyImmediate(ni);
            foreach (MonoBehaviour mb in root.GetComponents<MonoBehaviour>())
                if (!rootKeeps.Contains(mb.GetType())) Object.DestroyImmediate(mb);

            root.AddComponent<SeatOwner>().Seat = seat;
            copy.Hud = root.GetComponent<MicrogameHUDManager>();
            copy.Canvases = CanvasesUnder(root);
            ContextPatcher.PatchTypesOf(root);

            // ligar roda o Awake: os "únicos" do jogo (HUD da partida, vida, dicas) continuam apontando o original
            List<(FieldInfo, object)> singletons = Singletons(twins);
            SeatContext.Token t = SeatContext.Enter(seat);
            try { root.transform.SetParent(null, false); }
            finally { SeatContext.Exit(t); }
            foreach ((FieldInfo f, object value) in singletons) f.SetValue(null, value);
            MicrogameCoordinator.singleton.HUDManager = main;
            if (root.scene != source.scene) SceneManager.MoveGameObjectToScene(root, source.scene); // some com a fase
            Object.Destroy(holder);

            // o que uma animação ou contagem do original mostrava agora, sem quem o anima, na cópia ficaria parado
            GameObject counter = Traverse.Create(copy.Hud).Field("counter").GetValue<GameObject>();
            if (counter != null) counter.SetActive(false);

            copies[seat] = copy;
            Plugin.Log.LogInfo($"P{seat + 1}: HUD da partida próprio ({copy.Canvases.Length} telas)");
        }

        static void Drop(int seat)
        {
            if (copies.TryGetValue(seat, out Copy c) && c.Root != null) Object.Destroy(c.Root);
            copies.Remove(seat);
        }

        // os campos estáticos que guardam um componente (o "único" de cada tipo) dos tipos copiados, como estão agora
        static List<(FieldInfo, object)> Singletons(IEnumerable<MonoBehaviour> components)
        {
            var saved = new List<(FieldInfo, object)>();
            var seen = new HashSet<Type>();
            foreach (MonoBehaviour mb in components)
            {
                if (mb == null) continue;
                for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    if (!seen.Add(t) || t.ContainsGenericParameters) continue;
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!f.IsLiteral && !f.IsInitOnly && typeof(Object).IsAssignableFrom(f.FieldType)) saved.Add((f, f.GetValue(null)));
                }
            }
            return saved;
        }

        static void Patch()
        {
            if (patched) return;
            patched = true;
            var harmony = new Harmony(Plugin.Id + ".hud");
            var route = new HarmonyMethod(typeof(HudCopies), nameof(Route));
            var replay = new HarmonyMethod(typeof(HudCopies), nameof(Replay));
            var enter = new HarmonyMethod(typeof(HudCopies), nameof(Enter));
            var exit = new HarmonyMethod(typeof(HudCopies), nameof(Exit));
            foreach (Type type in hudTypes)
            {
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.IsGenericMethodDefinition || m.GetMethodBody() == null) continue;
                    // parâmetros "in"/"ref" não passam pela lista de argumentos do Harmony: esses ficam só com o contexto
                    bool routed = m.IsPublic && m.ReturnType == typeof(void) && !m.IsSpecialName && !m.GetParameters().Any(p => p.ParameterType.IsByRef);
                    try
                    {
                        // a cópia roda como o jogador dela (eventos, corrotinas, o que ela mesma chama)
                        if (routed) harmony.Patch(m, prefix: route, postfix: replay);
                        harmony.Patch(m, prefix: enter, finalizer: exit);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"HUD por jogador: {type.Name}.{m.Name} ficou de fora ({e.GetType().Name})");
                    }
                }
            }
            harmony.Patch(AccessTools.Method(typeof(MicrogameBase), nameof(MicrogameBase.FinishAnyExistingHints)), postfix: new HarmonyMethod(typeof(HudCopies), nameof(RoundResults)));
        }

        static bool IsMain(Component c) => main != null && c != null && c.transform.root == main.transform;

        static void Enter(Component __instance, out SeatContext.Token __state)
        {
            int seat = SeatContext.SeatOf(__instance);
            __state = seat >= 0 ? SeatContext.Enter(seat) : default;
        }

        static Exception Exit(Exception __exception, SeatContext.Token __state)
        {
            SeatContext.Exit(__state);
            return __exception;
        }

        // código de um jogador extra mexendo no HUD do jogo: vai para a cópia dele
        static bool Route(MethodBase __originalMethod, Component __instance, object[] __args)
        {
            if (replaying || !SeatContext.IsExtra || !IsMain(__instance)) return true;
            if (!copies.TryGetValue(SeatContext.Seat, out Copy c)) return true;
            Call(c, __originalMethod, __instance, __args);
            return false;
        }

        // código da partida mexendo no HUD do jogo: também em cada cópia
        static void Replay(MethodBase __originalMethod, Component __instance, object[] __args)
        {
            if (replaying || SeatContext.IsExtra || copies.Count == 0 || !IsMain(__instance)) return;
            string name = __originalMethod.Name;
            if (hostOnly.Contains(name))
            {
                if (name == "ShowWinner" || name == "ShowWinnerMultiple") Winners(name, __args);
                return;
            }
            foreach (Copy c in copies.Values) Call(c, __originalMethod, __instance, __args);
        }

        static void Call(Copy c, MethodBase method, Component original, object[] args)
        {
            if (!c.Twin.TryGetValue(original.GetInstanceID(), out Component twin) || twin == null) return;
            Invoke(c, () => method.Invoke(twin, args), method.Name);
        }

        static void Invoke(Copy c, Action call, string what)
        {
            int seat = c.Root != null && c.Root.TryGetComponent(out SeatOwner o) ? o.Seat : -1;
            if (seat < 0) return;
            bool was = replaying;
            replaying = true;
            SeatContext.Token t = SeatContext.Enter(seat);
            try { call(); }
            catch (Exception e)
            {
                if (failed.Add(what)) Plugin.Log.LogWarning($"HUD de P{seat + 1}: {what} falhou ({(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message})");
            }
            finally
            {
                SeatContext.Exit(t);
                replaying = was;
            }
        }

        // o fim de partida: cada um vê se ele (ou o time dele) venceu
        static void Winners(string name, object[] args)
        {
            foreach (KeyValuePair<int, Copy> kv in copies)
            {
                PlayerController pc = SeatContext.PlayerOf(kv.Key);
                if (pc == null || kv.Value.Hud == null) continue;
                MicrogameHUDManager hud = kv.Value.Hud;
                if (name == "ShowWinner")
                {
                    var winner = (PlayerController)args[0];
                    Invoke(kv.Value, () => hud.ShowWinner(winner, winner == pc, (bool)args[2]), name);
                }
                else
                {
                    var winners = (uint[])args[0];
                    Invoke(kv.Value, () => hud.ShowWinnerMultiple(winners, winners != null && winners.Contains(pc.netId), (bool)args[2]), name);
                }
            }
        }

        // fim de rodada (a mesma conta que o jogo faz para o jogador local, para cada extra): "VENCEU +500" ou "FALHOU"
        static void RoundResults()
        {
            MicrogameCoordinator coord = MicrogameCoordinator.singleton;
            if (copies.Count == 0 || coord == null || coord.forceStopCoordinator) return;
            foreach (KeyValuePair<int, Copy> kv in copies)
            {
                PlayerController pc = SeatContext.PlayerOf(kv.Key);
                MicrogameHUDManager hud = kv.Value.Hud;
                if (pc == null || hud == null) continue;
                Invoke(kv.Value, () =>
                {
                    if (pc.State == PlayerController.PlayerState.Player)
                    {
                        bool won = postProcessWin(coord, coord.IsWinner(pc.netId));
                        int awarded = coord.scoreAwarded < 1 ? coord.Gamemode.BaseScore : coord.scoreAwarded;
                        int score = awarded + coord.Argument.EvaluateAdditionalBonuses(awarded);
                        score += coord.Gamemode.EvaluateComboPoints(PlayersManager.singleton.GetPlayerCombo(pc.netId) + 1);
                        if (coord.SpecialRound != null) score += coord.SpecialRound.EvaluateAdditionalBonuses(score);
                        hud.ShowRoundResult(won ? WinState.Won : WinState.Lost, score);
                    }
                    else if (pc.spectatorMaster.specMode == SpectatingMode.SpectateOthers && pc.spectatorMaster.SpectatedPlayer != null)
                    {
                        bool won = postProcessWin(coord, coord.IsWinner(pc.spectatorMaster.SpectatedPlayer.netId));
                        hud.ShowRoundResultNoScore(won ? WinState.Won : WinState.Lost);
                    }
                    else hud.HideMicrogameInstruction();
                }, "ShowRoundResult");
            }
        }
    }
}
