using System;
using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Empurrar, socar, atirar e passar a bomba procuram os alvos na camada dos jogadores dos outros PCs (8): o
    /// próprio personagem fica na camada do jogador local (13) e assim ninguém acerta a si mesmo. Aqui todos estão no
    /// mesmo jogo e todos são locais (13): o empurrão e os golpes não achavam ninguém.
    ///
    /// Enquanto um jogador procura alvos, os outros jogadores do sofá ficam na camada dos jogadores dos outros PCs,
    /// como no jogo dele pela internet, e voltam logo depois. O resto do jogo (o ácido, as placas de pressão) continua
    /// vendo todos como locais, que é o que faz cada um morrer ou apertar a placa.
    /// </summary>
    static class SeatLayers
    {
        const int LocalPlayerLayer = 13, OtherPlayerLayer = 8;
        static readonly GameObject[] swapped = new GameObject[Client.MaxSeats + 1];
        static int count;

        /// <summary>Os outros jogadores do sofá passam para a camada dos outros PCs. Devolve se trocou algo.</summary>
        public static bool AsSeenBy(PlayerController self)
        {
            if (self == null || count > 0 || SplitScreen.LocalPlayers < 2) return false;
            NetworkIdentity host = NetworkServer.localConnection?.identity;
            Swap(host != null ? host.GetComponent<PlayerController>() : null, self);
            for (int s = 0; s < Client.MaxSeats; s++) Swap(SeatContext.PlayerOf(s), self);
            return count > 0;
        }

        public static void Restore()
        {
            for (int i = 0; i < count; i++)
                if (swapped[i] != null) swapped[i].layer = LocalPlayerLayer;
            Array.Clear(swapped, 0, count);
            count = 0;
        }

        static void Swap(PlayerController pc, PlayerController self)
        {
            if (pc == null || pc == self || count >= swapped.Length) return;
            GameObject go = pc.gameObject;
            if (go.layer != LocalPlayerLayer) return;
            go.layer = OtherPlayerLayer;
            swapped[count++] = go;
        }

        [HarmonyPatch(typeof(PushingHandler), nameof(PushingHandler.DoPush))]
        static class Push
        {
            static void Prefix(PushingHandler __instance, out bool __state) => __state = AsSeenBy(__instance.parentPlayer);
            static Exception Finalizer(Exception __exception, bool __state) { if (__state) Restore(); return __exception; }
        }

        // tiros (raio e esfera), socos da luva e a bomba que passa de mão em mão
        [HarmonyPatch]
        static class Weapons
        {
            static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(WeaponBase), "PerformRaycast");
                yield return AccessTools.Method(typeof(WeaponBase), "PerformSphereCast");
                yield return AccessTools.Method(typeof(BoxingGloves), "Punch");
                yield return AccessTools.Method(typeof(PassableBombThrowable), nameof(PassableBombThrowable.TryPassBomb));
            }

            static void Prefix(WeaponBase __instance, out bool __state) => __state = AsSeenBy(__instance.parentPlayer);
            static Exception Finalizer(Exception __exception, bool __state) { if (__state) Restore(); return __exception; }
        }
    }
}
