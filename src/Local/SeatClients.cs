using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Pela internet, o código de cada desafio roda no jogo de cada pessoa: a parte "de servidor" só em quem hospeda
    /// (sortear, dar armas, marcar quem venceu) e a parte "de cliente" em todos, olhando o jogador de cada um ("já se
    /// mexeu?", "pulou 3 vezes?", "pisou no ladrilho certo?", entrar na porta, somar os números). Aqui há um jogo só,
    /// e a parte de cliente rodava só para quem hospeda: os jogadores do sofá nunca passavam nem falhavam nesses
    /// desafios.
    ///
    /// Cada jogador do sofá passa a rodar a parte de cliente do desafio (começo, cada quadro, fim e o aviso de quem
    /// passou), no contexto dele, como se fosse o jogo dele: a parte de servidor fica desligada (IsServer = false),
    /// o estado do desafio ("já se mexeu?") é guardado separado para cada um, e o sorteio do desafio (PRNG) começa no
    /// mesmo ponto que o de quem hospeda, para todos verem a mesma sala. O que o jogador faz vai pelo mesmo evento de
    /// rede do jogo (SendClientEvent), no nome dele.
    ///
    /// Ficam de fora os desafios que já têm um painel por jogador (SeatPads) e os feitos em Lua (o estado deles fica
    /// no Lua, sem como separar por jogador).
    /// </summary>
    static class SeatClients
    {
        /// <summary>Rodando a parte de cliente de um desafio para um jogador do sofá (sem a parte de servidor).</summary>
        public static bool Replaying { get; private set; }

        static readonly HashSet<string> skip = new HashSet<string>
        {
            "TypeTheAnswer", "TypeTheWord", "TypeTheColor", "SelectTheAnswer", "RepeatTheSequence", "StackIt", "FreeRound",
            "LuaMicrogame", "TestMicrogame",
        };

        static readonly MethodInfo setBegin = AccessTools.PropertySetter(typeof(MicrogameBase), nameof(MicrogameBase.OnBeginMicrogame));
        static readonly MethodInfo setTick = AccessTools.PropertySetter(typeof(MicrogameBase), nameof(MicrogameBase.OnMicrogameTick));
        static readonly MethodInfo setPost = AccessTools.PropertySetter(typeof(MicrogameBase), nameof(MicrogameBase.OnPostMicrogame));
        static readonly MethodInfo setState = AccessTools.PropertySetter(typeof(MicrogameBase), nameof(MicrogameBase.OnPlayerStateUpdate));

        static readonly Dictionary<Type, FieldInfo[]> fieldsOf = new Dictionary<Type, FieldInfo[]>();
        static FieldInfo[] prngFields;
        // o estado do desafio de cada jogador do sofá (vaga → valores dos campos), o do desafio atual
        static readonly object[][] states = new object[Plugin_MaxSeats][];
        static MicrogameBase current;
        static object[] hostState, prngBefore, prngAfter;
        static readonly HashSet<string> failed = new HashSet<string>();
        const int Plugin_MaxSeats = PartyAnywhere.Client.MaxSeats;

        [HarmonyPatch(typeof(MicrogameCoordinator), nameof(MicrogameCoordinator.IsServer), MethodType.Getter)]
        static class NotServerWhileReplaying
        {
            static bool Prefix(ref bool __result)
            {
                if (!Replaying) return true;
                __result = false;
                return false;
            }
        }

        // rede de segurança: o que só o servidor faz (marcar quem venceu, dar ou tirar armas, criar objetos de rede)
        // não acontece de novo na repetição, mesmo num desafio que não pergunta IsServer antes
        [HarmonyPatch]
        static class ServerOnly
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(MicrogameCoordinator), nameof(MicrogameCoordinator.MarkWinnerWithAdjustedTime));
                yield return AccessTools.Method(typeof(MicrogameCoordinator), nameof(MicrogameCoordinator.MarkLoser));
                yield return AccessTools.Method(typeof(PlayerController), nameof(PlayerController.AwardWeapon));
                yield return AccessTools.Method(typeof(PlayerController), nameof(PlayerController.ReplaceWeapon));
                yield return AccessTools.Method(typeof(PlayerController), nameof(PlayerController.RemoveWeapon));
                yield return AccessTools.Method(typeof(MicrogameBase), "SpawnTemporaryNetObject");
            }

            static bool Prefix() => !Replaying;
        }

        // a cada desafio (e variação), o jogo escolhe as funções de começo, quadro e fim: cada uma passa a rodar
        // também para os jogadores do sofá
        [HarmonyPatch(typeof(MicrogameBase), nameof(MicrogameBase.SetCurrentVariation))]
        static class Wrap
        {
            static void Postfix(MicrogameBase __instance)
            {
                if (__instance == null || skip.Contains(__instance.GetType().Name)) return;
                MicrogameBase mg = __instance;
                Action begin = mg.OnBeginMicrogame;
                Action<double> tick = mg.OnMicrogameTick;
                Action post = mg.OnPostMicrogame;
                Action<PlayerController, WinState> state = mg.OnPlayerStateUpdate;
                if (begin != null) setBegin.Invoke(mg, new object[] { (Action)(() => Begin(mg, begin)) });
                if (tick != null) setTick.Invoke(mg, new object[] { (Action<double>)(t => Tick(mg, tick, t)) });
                if (post != null) setPost.Invoke(mg, new object[] { (Action)(() => Post(mg, post)) });
                if (state != null) setState.Invoke(mg, new object[] { (Action<PlayerController, WinState>)((pc, ws) => State(mg, state, pc, ws)) });
            }
        }

        static void Begin(MicrogameBase mg, Action begin)
        {
            current = mg;
            Array.Clear(states, 0, states.Length);
            if (!HasExtras()) { begin(); return; }
            prngBefore = Save(mg.PRNG, Prng(mg.PRNG), prngBefore);
            begin();
            prngAfter = Save(mg.PRNG, Prng(mg.PRNG), prngAfter);
            FieldInfo[] fields = Fields(mg.GetType());
            hostState = Save(mg, fields, hostState);
            for (int s = 0; s < states.Length; s++)
            {
                if (SeatContext.PlayerOf(s) == null) continue;
                // cada um começa de onde quem hospeda começou, com o mesmo sorteio
                Load(mg, fields, hostState);
                Load(mg.PRNG, Prng(mg.PRNG), prngBefore);
                Run(mg, s, begin);
                states[s] = Save(mg, fields, null);
            }
            Load(mg, fields, hostState);
            Load(mg.PRNG, Prng(mg.PRNG), prngAfter);
        }

        static void Tick(MicrogameBase mg, Action<double> tick, double timeLeft)
        {
            if (current == mg && HasStates())
            {
                FieldInfo[] fields = Fields(mg.GetType());
                hostState = Save(mg, fields, hostState);
                prngBefore = Save(mg.PRNG, Prng(mg.PRNG), prngBefore);
                for (int s = 0; s < states.Length; s++)
                {
                    if (states[s] == null || SeatContext.PlayerOf(s) == null) continue;
                    Load(mg, fields, states[s]);
                    Load(mg.PRNG, Prng(mg.PRNG), prngBefore);
                    Run(mg, s, () => tick(timeLeft));
                    Save(mg, fields, states[s]);
                }
                Load(mg, fields, hostState);
                Load(mg.PRNG, Prng(mg.PRNG), prngBefore);
            }
            // quem hospeda por último: o que é da tela (um só) fica como o dele
            tick(timeLeft);
        }

        static void Post(MicrogameBase mg, Action post)
        {
            post();
            if (current != mg || !HasStates()) return;
            FieldInfo[] fields = Fields(mg.GetType());
            hostState = Save(mg, fields, hostState);
            for (int s = 0; s < states.Length; s++)
            {
                if (states[s] == null || SeatContext.PlayerOf(s) == null) continue;
                Load(mg, fields, states[s]);
                Run(mg, s, post);
            }
            Load(mg, fields, hostState);
            Array.Clear(states, 0, states.Length);
            current = null;
        }

        static void State(MicrogameBase mg, Action<PlayerController, WinState> update, PlayerController pc, WinState ws)
        {
            update(pc, ws);
            if (current != mg || !HasStates()) return;
            FieldInfo[] fields = Fields(mg.GetType());
            hostState = Save(mg, fields, hostState);
            for (int s = 0; s < states.Length; s++)
            {
                if (states[s] == null || SeatContext.PlayerOf(s) == null) continue;
                Load(mg, fields, states[s]);
                Run(mg, s, () => update(pc, ws));
                Save(mg, fields, states[s]);
            }
            Load(mg, fields, hostState);
        }

        // ---- eventos: cada um com o estado do jogador a quem pertence ----

        static bool swapping;
        static object[] swapHost;

        /// <summary>
        /// Os desafios também reagem a eventos ("apontou para o monitor", "recebeu o apontador", o aviso do servidor só
        /// para um jogador). Um evento de um jogador do sofá chega no contexto dele: a reação usa o estado dele, não o
        /// de quem hospeda. E os ouvintes que a repetição registraria de novo ficam de fora (o evento contaria várias
        /// vezes). Um método por vez, como em SeatTriggers.
        /// </summary>
        public static void InstallEvents(Harmony harmony)
        {
            var notReplaying = new HarmonyMethod(AccessTools.Method(typeof(SeatClients), nameof(NotWhileReplaying)));
            var swapIn = new HarmonyMethod(AccessTools.Method(typeof(SeatClients), nameof(SwapIn)));
            var swapOut = new HarmonyMethod(AccessTools.Method(typeof(SeatClients), nameof(SwapOut)));
            Type[] types;
            try { types = typeof(MicrogameBase).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            int listeners = 0, handlers = 0;
            foreach (Type t in types)
            {
                if (t.IsValueType && !t.IsGenericType && typeof(IEventPayload).IsAssignableFrom(t))
                {
                    Type ge = typeof(GlobalEvent<>).MakeGenericType(t);
                    foreach (string name in new[] { "AddListener", "RemoveListener" })
                        if (TryPatch(harmony, ge.GetMethod(name), notReplaying, null)) listeners++;
                }
                if (!typeof(MicrogameBase).IsAssignableFrom(t) || t.IsAbstract || skip.Contains(t.Name)) continue;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1 && typeof(IEventPayload).IsAssignableFrom(ps[0].ParameterType) && m.GetMethodBody() != null
                        && TryPatch(harmony, m, swapIn, swapOut)) handlers++;
                }
            }
            if (TryPatch(harmony, AccessTools.Method(typeof(MicrogameBase), nameof(MicrogameBase.InvokeCustomEvent)),
                new HarmonyMethod(AccessTools.Method(typeof(SeatClients), nameof(SwapInServerEvent))), swapOut)) handlers++;
            Plugin.Log.LogInfo($"Desafios por jogador: {listeners} registros de ouvinte, {handlers} reações");
        }

        static bool TryPatch(Harmony harmony, MethodInfo m, HarmonyMethod prefix, HarmonyMethod finalizer)
        {
            if (m == null) return false;
            try { harmony.Patch(m, prefix: prefix, finalizer: finalizer); return true; }
            catch (Exception e) { Plugin.Log.LogWarning($"{m.DeclaringType.Name}.{m.Name}: {e.Message}"); return false; }
        }

        static bool NotWhileReplaying() => !Replaying;

        static void SwapIn(object __instance, out int __state) => __state = Enter(__instance);

        // os eventos que o servidor manda (só esses: os que um jogador manda ao servidor são a parte de servidor)
        static void SwapInServerEvent(MicrogameBase __instance, bool calledFromClient, out int __state) =>
            __state = calledFromClient ? -1 : Enter(__instance);

        static int Enter(object instance)
        {
            if (Replaying || swapping || !SeatContext.IsExtra || current == null || !ReferenceEquals(instance, current)) return -1;
            int seat = SeatContext.Seat;
            if (seat < 0 || seat >= states.Length || states[seat] == null) return -1;
            FieldInfo[] fields = Fields(current.GetType());
            swapHost = Save(current, fields, swapHost);
            Load(current, fields, states[seat]);
            swapping = true;
            return seat;
        }

        static Exception SwapOut(Exception __exception, int __state)
        {
            if (__state < 0 || current == null) return __exception;
            FieldInfo[] fields = Fields(current.GetType());
            Save(current, fields, states[__state]);
            Load(current, fields, swapHost);
            swapping = false;
            return __exception;
        }

        static void Run(MicrogameBase mg, int seat, Action action)
        {
            SeatContext.Token t = SeatContext.Enter(seat);
            Replaying = true;
            try
            {
                action();
            }
            catch (Exception e)
            {
                // um desafio que não aguenta rodar duas vezes: avisa uma vez e segue (quem hospeda não é afetado)
                if (failed.Add(mg.GetType().Name)) Plugin.Log.LogWarning($"P{seat + 1} em {mg.GetType().Name}: {e.Message}");
            }
            finally
            {
                Replaying = false;
                SeatContext.Exit(t);
            }
        }

        static bool HasExtras()
        {
            for (int s = 0; s < states.Length; s++) if (SeatContext.PlayerOf(s) != null) return true;
            return false;
        }

        static bool HasStates()
        {
            for (int s = 0; s < states.Length; s++) if (states[s] != null) return true;
            return false;
        }

        // os campos do próprio desafio (não os do MicrogameBase, que são do jogo todo)
        static FieldInfo[] Fields(Type type)
        {
            if (fieldsOf.TryGetValue(type, out FieldInfo[] f)) return f;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(MicrogameBase); t = t.BaseType)
                foreach (FieldInfo fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!fi.IsInitOnly && !fi.IsLiteral) list.Add(fi);
            return fieldsOf[type] = list.ToArray();
        }

        static FieldInfo[] Prng(object prng)
        {
            if (prngFields == null && prng != null)
                prngFields = Array.FindAll(prng.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), f => !f.IsLiteral);
            return prngFields ?? new FieldInfo[0];
        }

        static object[] Save(object obj, FieldInfo[] fields, object[] into)
        {
            if (into == null || into.Length != fields.Length) into = new object[fields.Length];
            if (obj == null) return into;
            for (int i = 0; i < fields.Length; i++) into[i] = fields[i].GetValue(obj);
            return into;
        }

        static void Load(object obj, FieldInfo[] fields, object[] from)
        {
            if (obj == null || from == null || from.Length != fields.Length) return;
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(obj, from[i]);
        }
    }

    /// <summary>
    /// Rodadas especiais e regras da rodada que mudam só o jogador local (Turbo: mais rápido; movimento invertido;
    /// câmera virada): valem também para cada jogador do sofá.
    /// </summary>
    [HarmonyPatch]
    static class SeatModifiers
    {
        static bool running;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TurboSpecialRound), nameof(TurboSpecialRound.OnGameStart));
            yield return AccessTools.Method(typeof(TurboSpecialRound), nameof(TurboSpecialRound.OnGameEnd));
            yield return AccessTools.Method(typeof(InvertMovementArgument), nameof(InvertMovementArgument.OnRoundBegin));
            yield return AccessTools.Method(typeof(InvertMovementArgument), nameof(InvertMovementArgument.OnRoundEnd));
            yield return AccessTools.Method(typeof(CameraFlipArgument), nameof(CameraFlipArgument.OnRoundBegin));
            yield return AccessTools.Method(typeof(CameraFlipArgument), nameof(CameraFlipArgument.OnRoundEnd));
        }

        static void Postfix(object __instance, MethodBase __originalMethod)
        {
            if (running || SeatContext.IsExtra) return;
            running = true;
            try
            {
                for (int s = 0; s < PartyAnywhere.Client.MaxSeats; s++)
                {
                    if (SeatContext.PlayerOf(s) == null) continue;
                    SeatContext.Token t = SeatContext.Enter(s);
                    try { __originalMethod.Invoke(__instance, null); }
                    catch (Exception e) { Plugin.Log.LogWarning($"P{s + 1} em {__originalMethod.DeclaringType.Name}: {e.Message}"); }
                    finally { SeatContext.Exit(t); }
                }
            }
            finally { running = false; }
        }
    }
}
