using HarmonyLib;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// A animação do boneco de um jogador extra roda no código dele (no contexto, como jogador local): empurrar,
    /// pular, segurar a arma. O jogo também manda essa animação pela rede para os outros PCs, e aqui o "outro PC" é
    /// o mesmo jogo: ela chegava de volta e tocava de novo no mesmo boneco, fora do contexto (o empurrar saía
    /// dobrado, recomeçando). Os gatilhos e as camadas que voltam pela rede são ignorados para os bonecos do sofá;
    /// o estado contínuo (velocidade, cabeça) continua chegando, como para qualquer boneco.
    /// </summary>
    static class SeatAnimation
    {
        // a lista do jogo (uma por boneco, pelo número que vai na mensagem), criada uma vez
        static readonly KaiNetworkAnimator[] all =
            (KaiNetworkAnimator[])AccessTools.PropertyGetter(typeof(KaiNetworkAnimator), "Animators").Invoke(null, null);

        static bool OfExtra(byte id)
        {
            KaiNetworkAnimator a = all != null && id < all.Length ? all[id] : null;
            return a != null && a.PlayerController != null && SeatContext.SeatOf(a.PlayerController) >= 0;
        }

        [HarmonyPatch(typeof(KaiNetworkAnimator), "ClientHandleTriggerData")]
        static class Triggers
        {
            static bool Prefix(KaiAnimationTrigger __1) => !OfExtra(__1.AnimatorID);
        }

        // as camadas da animação (segurar a arma, empurrar) também voltam pela rede
        [HarmonyPatch(typeof(KaiNetworkAnimator), "OnLayerWeightsUpdateReceived")]
        static class Layers
        {
            static bool Prefix(KaiNetworkAnimator __instance) =>
                __instance.PlayerController == null || SeatContext.SeatOf(__instance.PlayerController) < 0 || SeatContext.IsExtra;
        }
    }
}
