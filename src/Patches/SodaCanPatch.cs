using HarmonyLib;
using MicroWorksCoop.Input;
using UnityEngine;

namespace MicroWorksCoop.Patches
{
    // A lata do "Chacoalhe" com controle ou celular: PadInput.CanShake diz ao eixo do mouse quanto falta para a lata
    // chegar onde o analógico manda; o código do jogo conta a chacoalhada como sempre.
    [HarmonyPatch(typeof(SodaCan), "OnWeaponTick")]
    static class SodaCanPatch
    {
        static readonly AccessTools.FieldRef<SodaCan, float> angle = AccessTools.FieldRefAccess<SodaCan, float>("accumalativeAngle");

        static void Prefix(SodaCan __instance)
        {
            // só a lata de quem está em contexto (a dos outros não conta nada neste quadro)
            if (!PadInput.Routing || __instance.parentPlayer == null || !__instance.parentPlayer.IsLocalController) return;
            float limit = __instance.angleRequirements.x; // o jogo usa o x para os dois sentidos
            PadInput.CanShake(__instance.isHorizontal, Mathf.Abs(limit), angle(__instance));
        }

        static void Postfix() => PadInput.CanShakeDone();
    }
}
