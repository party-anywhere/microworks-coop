using HarmonyLib;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// O que mata quem encosta (o ácido do "Não Caia", lava, buracos) guarda o primeiro jogador que encostou e daí
    /// em diante mata sempre esse. Pela internet não importa: no jogo de cada pessoa só o jogador dela encosta (a
    /// camada 13 é a do jogador local). Aqui todos são locais: o P1 caía no ácido e quem morria era o P2, que tinha
    /// encostado antes. Cada encostada passa a valer para quem encostou.
    /// </summary>
    [HarmonyPatch(typeof(Killer))]
    static class SeatKiller
    {
        const int LocalPlayerLayer = 13;
        static readonly AccessTools.FieldRef<Killer, PlayerController> player = AccessTools.FieldRefAccess<Killer, PlayerController>("player");

        [HarmonyPrefix, HarmonyPatch(nameof(Killer.OnTriggerEnter))]
        static void Enter(Killer __instance, Collider other) => Who(__instance, other);

        [HarmonyPrefix, HarmonyPatch(nameof(Killer.OnTriggerExit))]
        static void Exit(Killer __instance, Collider other) => Who(__instance, other);

        static void Who(Killer killer, Collider other)
        {
            if (other == null || other.gameObject.layer != LocalPlayerLayer) return;
            PlayerController pc = other.gameObject.GetComponent<PlayerController>();
            if (pc != null) player(killer) = pc;
        }
    }
}
