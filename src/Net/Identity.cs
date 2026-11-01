using System.Threading.Tasks;
using HarmonyLib;
using PartyAnywhere;
using Steamworks;
using UnityEngine;

namespace MicroWorksCoop.Net
{
    /// <summary>
    /// Todos os jogadores do sofá estão na conta Steam de quem hospeda, mas o jogo guarda placar e reconexão pelo
    /// SteamID. Cada jogador extra se apresenta com o SteamID real e outro campo "instância" (P2 → 0x11, P3 →
    /// 0x12...). Não existe conta assim, então nunca colide com um jogador de verdade; o nome mostrado é o da vaga
    /// no Party Anywhere.
    /// </summary>
    static class Identity
    {
        const int InstanceShift = 32;
        const ulong InstanceMask = 0xFFFFFUL << InstanceShift;
        const uint RealInstance = 1; // a instância de um jogador no PC
        const uint FirstFakeInstance = 0x10;

        static readonly string[] seatNames = new string[Client.MaxSeats];
        static readonly Color32[] seatColors = new Color32[Client.MaxSeats];

        public static ulong Fake(ulong real, int seat) => WithInstance(real, FirstFakeInstance + (uint)seat);

        /// <summary>A vaga de um SteamID de jogador extra; false para um SteamID de verdade.</summary>
        public static bool TrySeat(ulong id, out int seat)
        {
            uint instance = (uint)((id & InstanceMask) >> InstanceShift);
            seat = (int)(instance - FirstFakeInstance);
            return instance >= FirstFakeInstance && seat < Client.MaxSeats;
        }

        /// <summary>
        /// Quem joga neste PC: os extras (SteamID de extra) e a conta de quem hospeda, enquanto o co-op local está
        /// valendo e a vaga tem alguém. Gente que entrou pela internet fica de fora.
        /// </summary>
        public static bool TryLocalSeat(ulong id, out int seat)
        {
            Client hub = Plugin.Hub;
            if (!Plugin.Active || hub == null) { seat = -1; return false; }
            if (!TrySeat(id, out seat))
            {
                if (id != WithInstance(SteamClient.SteamId.Value, RealInstance)) { seat = -1; return false; }
                seat = Plugin.Seat; // a conta de verdade é a de quem hospeda
            }
            return hub.GetSeatState(seat) != SeatState.Empty;
        }

        /// <summary>Guarda nome e cor da vaga (chamado nos eventos do Hub: entrou, trocou de nome).</summary>
        public static void SetSeat(int seat, SeatInfo info)
        {
            if (seat < 0 || seat >= Client.MaxSeats) return;
            seatNames[seat] = info.Name;
            seatColors[seat] = new Color32(info.R, info.G, info.B, 255);
        }

        public static Color32 SeatColor(int seat) =>
            seatColors[seat].a == 0 ? new Color32(120, 120, 120, 255) : seatColors[seat];

        static ulong WithInstance(ulong id, uint instance) => (id & ~InstanceMask) | ((ulong)instance << InstanceShift);

        public static string SeatName(int seat) =>
            string.IsNullOrEmpty(seatNames[seat]) ? $"Jogador {seat + 1}" : seatNames[seat];
    }

    [HarmonyPatch(typeof(SteamClient), nameof(SteamClient.SteamId), MethodType.Getter)]
    static class SteamIdPatch
    {
        static void Postfix(ref SteamId __result)
        {
            if (__result.Value == 0) return;
            // o código de um jogador extra (em contexto) se apresenta com o SteamID dele
            if (Local.SeatContext.IsExtra) __result = Identity.Fake(__result.Value, Local.SeatContext.Seat);
        }
    }

    [HarmonyPatch(typeof(SteamClient), nameof(SteamClient.Name), MethodType.Getter)]
    static class SteamNamePatch
    {
        static void Postfix(ref string __result)
        {
            if (Identity.TryLocalSeat(SteamClient.SteamId.Value, out int seat)) __result = Identity.SeatName(seat);
        }
    }

    [HarmonyPatch(typeof(Friend), nameof(Friend.Name), MethodType.Getter)]
    static class FriendNamePatch
    {
        static void Postfix(Friend __instance, ref string __result)
        {
            if (Identity.TryLocalSeat(__instance.Id.Value, out int seat)) __result = Identity.SeatName(seat);
        }
    }

    // Antes de mostrar nome e avatar (placar, votação) o jogo pede os dados da conta à Steam e espera a resposta.
    // Um SteamID de jogador extra não é conta nenhuma: a resposta nunca vem e o placar ficava com "?".
    [HarmonyPatch(typeof(Friend), nameof(Friend.RequestInfoAsync))]
    static class FakeIdNeedsNoSteamInfo
    {
        static bool Prefix(Friend __instance, ref Task __result)
        {
            if (!Identity.TrySeat(__instance.Id.Value, out _)) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }
}
