using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// O que se arremessa e o que se atira devagar (a bola de neve, a queimada, o tomate, a bomba) é um projétil que cada
    /// jogo cria sozinho, e só o jogo de quem lançou diz ao servidor quem foi acertado ("quem lançou é o jogador local?").
    /// Pela internet, o projétil passa por quem lançou (camada do jogador local) e bate nos outros (camada dos outros PCs).
    ///
    /// Aqui todos são locais: a bola do P1 atravessava os outros jogadores, e a de um jogador do sofá acertava mas não
    /// contava (o jogo de quem hospeda achava que ela era de outro PC). Agora:
    /// - o código do projétil roda no contexto de quem lançou (o acerto conta para ele);
    /// - enquanto há jogadores do sofá, a camada do projétil bate nos jogadores locais, e cada projétil ignora só o corpo
    ///   de quem o lançou;
    /// - na hora do choque, os outros jogadores do sofá são vistos como de outro PC (SeatLayers), como no jogo de quem lançou.
    /// </summary>
    static class SeatProjectiles
    {
        const int LocalPlayerLayer = 13, OtherPlayerLayer = 8;
        static readonly AccessTools.FieldRef<Projectile, PlayerController> launcher = AccessTools.FieldRefAccess<Projectile, PlayerController>("launcherPlayer");
        static readonly HashSet<int> opened = new HashSet<int>(); // camadas de projétil que passamos a deixar bater nos locais
        static readonly HashSet<string> logged = new HashSet<string>();

        public static void Install(Harmony harmony)
        {
            var enter = new HarmonyMethod(AccessTools.Method(typeof(SeatProjectiles), nameof(Enter)));
            var exit = new HarmonyMethod(AccessTools.Method(typeof(SeatProjectiles), nameof(Exit)));
            var hit = new HarmonyMethod(AccessTools.Method(typeof(SeatProjectiles), nameof(HitPrefix)));
            var hitExit = new HarmonyMethod(AccessTools.Method(typeof(SeatProjectiles), nameof(HitFinalizer)));
            Type[] types;
            try { types = typeof(Projectile).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            int done = 0;
            foreach (Type t in types)
            {
                if (!typeof(Projectile).IsAssignableFrom(t)) continue;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.IsGenericMethodDefinition || m.GetMethodBody() == null || m.Name == nameof(Projectile.Launch)) continue;
                    bool collision = m.Name.StartsWith("OnCollision") || m.Name.StartsWith("OnTrigger") || m.Name == "OnCustomCollision";
                    try { harmony.Patch(m, prefix: collision ? hit : enter, finalizer: collision ? hitExit : exit); done++; }
                    catch (Exception e) { Plugin.Log.LogWarning($"Projétil {t.Name}.{m.Name}: {e.Message}"); }
                }
            }
            harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.Launch)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(SeatProjectiles), nameof(LaunchPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(SeatProjectiles), nameof(LaunchPostfix))),
                finalizer: exit);
            Plugin.Log.LogInfo($"Projéteis no contexto de quem lançou: {done} métodos");
        }

        static int SeatOfLauncher(Projectile p)
        {
            PlayerController pc = p != null ? launcher(p) : null;
            return pc != null ? SeatContext.SeatOf(pc) : -1;
        }

        static void Enter(Projectile __instance, out SeatContext.Token __state)
        {
            int seat = SeatOfLauncher(__instance);
            __state = seat >= 0 ? SeatContext.Enter(seat) : default;
        }

        static Exception Exit(Exception __exception, SeatContext.Token __state)
        {
            SeatContext.Exit(__state);
            return __exception;
        }

        struct HitState
        {
            public SeatContext.Token Token;
            public bool Swapped;
        }

        // no choque, quem lançou é o local e os outros jogadores do sofá são "de outro PC" (a máscara do projétil). Bater
        // no próprio corpo não conta: pela internet, o projétil passa por quem lançou
        static bool HitPrefix(Projectile __instance, object[] __args, out HitState __state)
        {
            __state = default;
            if (__instance == null) return true;
            PlayerController own = launcher(__instance);
            if (own != null && SplitScreen.LocalPlayers > 1 && __args.Length == 1)
            {
                Component other = __args[0] is Collision c ? (Component)c.collider : __args[0] as Collider;
                if (other != null && other.GetComponentInParent<PlayerController>() == own) return false;
            }
            int seat = SeatOfLauncher(__instance);
            if (seat >= 0) __state.Token = SeatContext.Enter(seat);
            __state.Swapped = SeatLayers.AsSeenBy(launcher(__instance));
            return true;
        }

        // o acerto que volta para a arma: pela internet, o jogo de quem lançou não manda ao servidor o acerto nele mesmo
        // (a explosão em volta também o pega); no jogo de quem hospeda não havia esse filtro, e aqui todos estão nele
        [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.NotifyOf))]
        static class NotSelf
        {
            static bool Prefix(WeaponBase __instance, WeaponBase.WeaponHitData notificationData) =>
                SplitScreen.LocalPlayers < 2 || __instance.parentPlayer == null || notificationData.entityNetId != __instance.parentPlayer.netId;
        }

        static Exception HitFinalizer(Exception __exception, HitState __state)
        {
            if (__state.Swapped) SeatLayers.Restore();
            SeatContext.Exit(__state.Token);
            return __exception;
        }

        static void LaunchPrefix(PlayerController launcher, out SeatContext.Token __state)
        {
            int seat = launcher != null ? SeatContext.SeatOf(launcher) : -1;
            __state = seat >= 0 ? SeatContext.Enter(seat) : default;
        }

        // a camada do projétil passa a bater nos jogadores locais (só enquanto há jogadores do sofá); quem lançou, não
        static void LaunchPostfix(Projectile __instance, PlayerController launcher)
        {
            if (__instance == null) return;
            if (SplitScreen.LocalPlayers < 2) { CloseAll(); return; }
            int layer = __instance.gameObject.layer;
            if (!opened.Contains(layer) && Physics.GetIgnoreLayerCollision(layer, LocalPlayerLayer) && !Physics.GetIgnoreLayerCollision(layer, OtherPlayerLayer))
            {
                Physics.IgnoreLayerCollision(layer, LocalPlayerLayer, false);
                opened.Add(layer);
                Plugin.Log.LogInfo($"Projéteis na camada {LayerMask.LayerToName(layer)} batem nos jogadores do sofá");
            }
            if (logged.Add(__instance.GetType().Name + layer))
                Plugin.Log.LogInfo($"Projétil {__instance.GetType().Name}: camada {layer} ({LayerMask.LayerToName(layer)}), bate no local {!Physics.GetIgnoreLayerCollision(layer, LocalPlayerLayer)}, nos outros {!Physics.GetIgnoreLayerCollision(layer, OtherPlayerLayer)}, máscara {__instance.collisionMask.value:X}");
            if (launcher == null) return;
            Collider[] mine = __instance.GetComponentsInChildren<Collider>(true);
            foreach (Collider body in launcher.GetComponentsInChildren<Collider>(true))
                foreach (Collider c in mine)
                    if (body != null && c != null) Physics.IgnoreCollision(c, body, true);
        }

        // de volta ao jogo como ele é (um jogador só)
        static void CloseAll()
        {
            if (opened.Count == 0) return;
            foreach (int layer in opened) Physics.IgnoreLayerCollision(layer, LocalPlayerLayer, true);
            opened.Clear();
        }
    }
}
