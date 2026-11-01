using System;
using HarmonyLib;
using Mirror;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Pela internet, o jogo de cada pessoa tem "autoridade" sobre o que é dela: a arma na mão (a bola da queimada,
    /// o caranguejo, a bandeira), o que ela controla (sapo, kart) e os projéteis. É isso que deixa o código desses
    /// objetos ler o controle dela e mandar comandos ao servidor (arremessar, atirar).
    ///
    /// Aqui, o que é de um jogador extra pertence à conexão de mentira dele (LocalSeatConnection), e o Mirror só dá
    /// autoridade no jogo de quem hospeda ao que é da conexão de quem hospeda: a arma do P2 não arremessava. Este
    /// arquivo dá a autoridade também ao que é de um extra, avisa os objetos como o Mirror avisaria no jogo dele
    /// (OnStartAuthority, no contexto do extra: o sapo descobre quem o controla perguntando pelo jogador local) e
    /// põe a troca de contexto no código deles (SeatContext acha a vaga pelo dono do objeto).
    /// </summary>
    static class SeatAuthority
    {
        static readonly Action<NetworkIdentity, bool> setAuthority =
            AccessTools.MethodDelegate<Action<NetworkIdentity, bool>>(AccessTools.PropertySetter(typeof(NetworkIdentity), "hasAuthority"));

        static readonly Action<NetworkIdentity> notifyAuthority =
            AccessTools.MethodDelegate<Action<NetworkIdentity>>(AccessTools.Method(typeof(NetworkIdentity), "NotifyAuthority"));

        public static int SeatOf(NetworkIdentity id) =>
            id != null && id.connectionToClient is LocalSeatConnection conn ? conn.Seat : -1;

        /// <summary>Recalcula a autoridade do objeto no jogo de quem hospeda e avisa o objeto, no contexto do dono.</summary>
        public static void Refresh(NetworkIdentity id, bool owned)
        {
            if (id == null || !NetworkServer.active) return;
            setAuthority(id, owned);
            notifyAuthority(id); // o prefixo abaixo põe o contexto
        }

        // O Mirror avisa a autoridade de cada objeto que aparece no jogo de quem hospeda (e quando ela muda). O que é
        // de um extra ganha a autoridade aqui, como no jogo dele, e o aviso roda como ele.
        [HarmonyPatch(typeof(NetworkIdentity), "NotifyAuthority")]
        static class OwnedByExtras
        {
            static void Prefix(NetworkIdentity __instance, out SeatContext.Token __state)
            {
                __state = default;
                int seat = SeatOf(__instance);
                if (seat < 0 || !NetworkServer.active) return;
                if (!__instance.hasAuthority) setAuthority(__instance, true);
                SeatContext.Owned(__instance.gameObject);
                __state = SeatContext.Enter(seat);
            }

            static Exception Finalizer(Exception __exception, SeatContext.Token __state)
            {
                SeatContext.Exit(__state);
                return __exception;
            }
        }

        // Dar um objeto a um extra (o sapo, o kart): a mensagem que avisaria o jogo dele vai para a conexão de
        // mentira (engolida); o aviso acontece aqui.
        [HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.AssignClientAuthority))]
        static class AssignToExtra
        {
            static void Postfix(NetworkIdentity __instance, NetworkConnection conn, bool __result)
            {
                if (__result && conn is LocalSeatConnection) Refresh(__instance, true);
            }
        }

        // Tirar de um extra: o mesmo, ao contrário (sem o dono, o objeto volta a ser só do servidor).
        [HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.RemoveClientAuthority))]
        static class RemoveFromExtra
        {
            static void Prefix(NetworkIdentity __instance, out int __state) => __state = SeatOf(__instance);

            static void Postfix(NetworkIdentity __instance, int __state)
            {
                if (__state < 0 || __instance.connectionToClient != null) return;
                SeatContext.Disowned();
                // o aviso de "perdeu o objeto" roda como o extra, como no jogo dele
                SeatContext.Token t = SeatContext.Enter(__state);
                try { Refresh(__instance, false); }
                finally { SeatContext.Exit(t); }
            }
        }
    }
}
