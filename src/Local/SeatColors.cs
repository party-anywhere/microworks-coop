using HarmonyLib;
using MicroWorksCoop.Net;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Cada boneco do sofá com a cor da vaga dele (P1 azul, P2 vermelho...): corpo, mochila e propulsores na cor,
    /// olhos claros (escuros no P10, que é branco). Sem isso todos saem iguais, com as cores do "Personalizar" da
    /// conta de quem hospeda. Só na partida e só na memória: o que a pessoa salvou no Personalizar não muda.
    ///
    /// O jogo manda as cores de cada boneco uma vez, ao nascer, lendo o Personalizar (FromCVars) no código do
    /// próprio boneco; aqui, quando quem pergunta é um jogador do sofá, a resposta é a cor da vaga.
    /// </summary>
    [HarmonyPatch(typeof(CustomizationUpdatePacket), nameof(CustomizationUpdatePacket.FromCVars))]
    static class SeatColors
    {
        static void Postfix(ref CustomizationUpdatePacket __result)
        {
            Client hub = Plugin.Hub;
            int seat = SeatContext.Seat;
            if (hub == null || !Plugin.Active || !NetworkServer.active || hub.GetSeatState(seat) == SeatState.Empty) return;
            Color32 c = Identity.SeatColor(seat);
            int color = (c.r << 16) | (c.g << 8) | c.b;
            bool light = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f > 200f;
            __result.BodyColor = color;
            __result.JetpackColor = color;
            __result.JetpackEffectColor = color;
            __result.EyesColor = light ? 0x1E1E24 : 0xFFFFFF;
        }
    }
}
