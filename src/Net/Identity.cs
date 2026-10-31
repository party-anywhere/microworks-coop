using HarmonyLib;
using PartyAnywhere;
using Steamworks;
using UnityEngine;

namespace MicroWorksCoop.Net
{
    /// <summary>
    /// Todas as cópias rodam na mesma conta Steam, mas o jogo guarda placar e reconexão pelo SteamID. Cada cópia
    /// extra se apresenta com o SteamID real e outro campo "instância" (P2 → 0x11, P3 → 0x12...). Não existe conta
    /// assim, então nunca colide com um jogador de verdade; o nome mostrado é o da vaga no Party Anywhere.
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

        /// <summary>A vaga de um SteamID de cópia extra; false para um SteamID de verdade.</summary>
        public static bool TrySeat(ulong id, out int seat)
        {
            uint instance = (uint)((id & InstanceMask) >> InstanceShift);
            seat = (int)(instance - FirstFakeInstance);
            return instance >= FirstFakeInstance && seat < Client.MaxSeats;
        }

        /// <summary>
        /// Quem joga neste PC: as cópias (SteamID de cópia) e a conta da cópia que hospeda, enquanto o co-op local
        /// está valendo e a vaga tem alguém. Gente que entrou pela internet fica de fora.
        /// </summary>
        public static bool TryLocalSeat(ulong id, out int seat)
        {
            Client hub = Plugin.Hub;
            if (!Plugin.Active || hub == null) { seat = -1; return false; }
            if (!TrySeat(id, out seat))
            {
                if (id != WithInstance(SteamClient.SteamId.Value, RealInstance)) { seat = -1; return false; }
                seat = Plugin.HostSeat; // a conta de verdade é a da cópia que hospeda
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
            if (!Plugin.IsLeader && __result.Value != 0) __result = Identity.Fake(__result.Value, Plugin.Seat);
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
}
