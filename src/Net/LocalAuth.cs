using HarmonyLib;
using Mirror;

namespace MicroWorksCoop.Net
{
    /// <summary>
    /// Sem o Party Pass, o jogo só cria sala pública com "VAC" (o servidor confere o ticket Steam de cada um). As
    /// cópias deste PC são a mesma conta Steam do host, que já foi conferida, com um SteamID de cópia (Identity):
    /// entram direto. Só por 127.0.0.1: quem vem pela internet passa por todas as conferências do jogo, e um
    /// SteamID de cópia vindo de fora é recusado.
    /// </summary>
    [HarmonyPatch(typeof(AgiriAuth), nameof(AgiriAuth.OnAuthenticationRequest))]
    static class LocalCopyAuth
    {
        static bool Prefix(AgiriAuth __instance, NetworkConnection conn, AgiriAuth.RequestAuthMessage msg)
        {
            bool local = LocalNet.IsLocalCopy(conn);
            bool copyId = Identity.TrySeat(msg.steamId, out int seat);
            if (local != copyId)
            {
                Plugin.Log.LogWarning($"Conexão recusada: {(local ? "cópia local sem SteamID de cópia" : "SteamID de cópia vindo de fora")}");
                conn.Disconnect();
                return false;
            }
            if (!local) return true;

            Plugin.Log.LogInfo($"P{seat + 1} entrou na partida (cópia local)");
            conn.Send(new AgiriAuth.ResponseAuthMessage { responseCode = 100, message = "" });
            __instance.OnServerAuthenticated.Invoke(conn);
            return false;
        }
    }
}
