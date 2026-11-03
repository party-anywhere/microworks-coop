using System;
using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// A conexão de um jogador extra no servidor de quem hospeda. O personagem dele nasce pelo caminho normal do
    /// jogo (AddPlayerForConnection): entra no placar, nos microgames e na troca de fase como qualquer jogador.
    /// Não há ninguém do outro lado: o personagem já vive neste jogo, então as mensagens de rede para ela são
    /// engolidas (as dirigidas a ele vêm para cá; ver TargetRpcToThisGame).
    /// </summary>
    class LocalSeatConnection : NetworkConnectionToClient
    {
        public readonly int Seat;

        public LocalSeatConnection(int seat) : base(-1000 - seat)
        {
            Seat = seat;
            isAuthenticated = true;
        }

        // o jogo confere o SteamID pelo endereço da conexão, menos para "localhost" (o próprio host)
        public override string address => "localhost";

        public override void Disconnect() => isReady = false;
    }

    /// <summary>Faz os jogadores extras nascerem quando entram no lobby do Party Anywhere e saírem quando saem.</summary>
    class LocalPlayers : MonoBehaviour
    {
        // depois que o personagem de quem hospeda aparece numa fase, um respiro para a fase se arrumar
        const float Settle = 1.5f;

        static readonly LocalSeatConnection[] connections = new LocalSeatConnection[Client.MaxSeats];
        static readonly System.Reflection.MethodInfo setAuthority = AccessTools.PropertySetter(typeof(NetworkIdentity), "hasAuthority");

        float hostSince = -1f;

        void Update()
        {
            Client hub = Plugin.Hub;
            NetworkConnectionToClient host = NetworkServer.localConnection;
            if (!Plugin.Active || hub == null || !NetworkServer.active || host == null || host.identity == null)
            {
                hostSince = -1f;
                return;
            }
            if (hostSince < 0f) hostSince = Time.unscaledTime;
            SeatPawns.Tick();
            if (Time.unscaledTime - hostSince < Settle) return;

            for (int seat = 0; seat < Client.MaxSeats; seat++)
            {
                if (seat == Plugin.Seat) continue;
                bool wanted = hub.GetSeatState(seat) != SeatState.Empty; // quem caiu e está voltando guarda o personagem
                bool alive = SeatContext.PlayerOf(seat) != null;
                if (wanted && !alive) { Spawn(seat); refreshAt = Time.unscaledTime + RefreshDelay; }
                else if (!wanted && alive) { Despawn(seat); refreshAt = Time.unscaledTime + RefreshDelay; }
            }
            // o personagem que entra se apresenta ao placar no contexto dele (só o HUD dele via); depois que todos se
            // apresentaram, o placar de todos é refeito
            if (refreshAt > 0f && Time.unscaledTime >= refreshAt && PlayersManager.singleton != null)
            {
                refreshAt = -1f;
                PlayersManager.singleton.SortAndRefreshScoreboard();
            }
        }

        const float RefreshDelay = 1f;
        float refreshAt = -1f;

        static void Spawn(int seat)
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null || nm.playerPrefab == null) return;
            LocalSeatConnection conn = connections[seat] ?? (connections[seat] = new LocalSeatConnection(seat));
            NetworkServer.connections[conn.connectionId] = conn;

            Transform start = nm.GetStartPosition();
            GameObject obj = start != null ? Instantiate(nm.playerPrefab, start.position, start.rotation) : Instantiate(nm.playerPrefab);
            var pc = obj.GetComponent<PlayerController>();
            SeatContext.Register(seat, pc); // antes de nascer: o Start dele já roda como jogador local
            if (!NetworkServer.AddPlayerForConnection(conn, obj))
            {
                SeatContext.Unregister(seat);
                Destroy(obj);
                return;
            }
            setAuthority.Invoke(pc.netIdentity, new object[] { true });
            Plugin.Log.LogInfo($"P{seat + 1} entrou na partida (neste jogo)");
            SeatPawns.Joined();
        }

        static void Despawn(int seat)
        {
            LocalSeatConnection conn = connections[seat];
            if (conn != null && conn.identity != null) NetworkServer.RemovePlayerForConnection(conn, true);
            SeatContext.Unregister(seat);
            if (conn != null) NetworkServer.connections.Remove(conn.connectionId);
            Plugin.Log.LogInfo($"P{seat + 1} saiu da partida");
        }

        void OnDestroy()
        {
            for (int s = 0; s < Client.MaxSeats; s++) SeatContext.Unregister(s);
        }
    }

    // ---- o Mirror com jogadores que vivem no próprio jogo de quem hospeda ----

    [HarmonyPatch(typeof(NetworkConnectionToClient), "Send", typeof(ArraySegment<byte>), typeof(int))]
    static class SwallowSends
    {
        static bool Prefix(NetworkConnectionToClient __instance, ref bool __result)
        {
            if (!(__instance is LocalSeatConnection)) return true;
            __result = true;
            return false;
        }
    }

    // O envio para vários de uma vez (atualizações para todos os prontos) passa a lista de conexões direto ao
    // transporte: as dos jogadores extras saem da lista (o transporte não as conhece).
    [HarmonyPatch(typeof(NetworkConnectionToClient), "Send", typeof(System.Collections.Generic.List<int>), typeof(ArraySegment<byte>), typeof(int))]
    static class SwallowBatchSends
    {
        public const int FirstLocalSeatId = -1000 - Client.MaxSeats + 1; // ids -1000..-1009 (LocalSeatConnection)

        static bool Prefix(System.Collections.Generic.List<int> connectionIds, ref bool __result)
        {
            connectionIds.RemoveAll(id => id <= -1000 && id >= FirstLocalSeatId);
            if (connectionIds.Count > 0) return true;
            __result = true;
            return false;
        }
    }

    // a conexão de um jogador extra nunca recebe mensagens; o servidor não pode achar que ela "caiu"
    [HarmonyPatch(typeof(NetworkConnectionToClient), "IsClientAlive")]
    static class AlwaysAlive
    {
        static bool Prefix(NetworkConnectionToClient __instance, ref bool __result)
        {
            if (!(__instance is LocalSeatConnection)) return true;
            __result = true;
            return false;
        }
    }

    // O comando de um jogador extra sai pela conexão de quem hospeda (é o mesmo jogo). O servidor só aceita
    // comando do dono do personagem: aqui ele fica sabendo que o dono é a conexão daquele jogador.
    // Comandos de objetos que não são de ninguém (os da fase, como o "passei no ponto de controle" do kart, que
    // aceitam comando de qualquer um): o dono é quem mandou. Cada comando enviado anota de que vaga saiu, na ordem, e o
    // servidor confere a anotação quando ele chega.
    [HarmonyPatch(typeof(NetworkServer), "OnCommandMessage")]
    static class CommandsFromLocalSeats
    {
        static void Prefix(ref NetworkConnection conn, CommandMessage msg)
        {
            if (conn != NetworkServer.localConnection) return;
            int sentBy = CommandSenders.Take(msg.netId, msg.functionHash);
            if (NetworkIdentity.spawned.TryGetValue(msg.netId, out NetworkIdentity id) && id.connectionToClient is LocalSeatConnection seatConn)
                conn = seatConn;
            else if (sentBy >= 0 && SeatContext.PlayerOf(sentBy) is PlayerController pc && pc.connectionToClient != null)
                conn = pc.connectionToClient;
        }
    }

    [HarmonyPatch(typeof(NetworkBehaviour), "SendCommandInternal")]
    static class CommandSenders
    {
        // (netId, função, vaga ou -1 para quem hospeda), na ordem do envio; sem resposta, os mais velhos saem
        const int Max = 64;
        static readonly uint[] ids = new uint[Max];
        static readonly int[] hashes = new int[Max], seats = new int[Max];
        static int count;

        static readonly Func<Type, string, int> methodHash =
            AccessTools.MethodDelegate<Func<Type, string, int>>(AccessTools.Method(typeof(Mirror.RemoteCalls.RemoteCallHelper), "GetMethodHash"));

        static void Prefix(NetworkBehaviour __instance, Type invokeClass, string cmdName)
        {
            if (!NetworkServer.active) return;
            if (count == Max) Remove(0);
            ids[count] = __instance.netId;
            hashes[count] = methodHash(invokeClass, cmdName);
            seats[count] = SeatContext.IsExtra ? SeatContext.Seat : -1;
            count++;
        }

        public static int Take(uint netId, int hash)
        {
            for (int i = 0; i < count; i++)
            {
                if (ids[i] != netId || hashes[i] != hash) continue;
                int seat = seats[i];
                Remove(i);
                return seat;
            }
            return -1;
        }

        static void Remove(int i)
        {
            count--;
            Array.Copy(ids, i + 1, ids, i, count - i);
            Array.Copy(hashes, i + 1, hashes, i, count - i);
            Array.Copy(seats, i + 1, seats, i, count - i);
        }
    }

    // O que o servidor manda só para um jogador extra (TargetRpc) roda neste jogo, onde o personagem dele vive, já no
    // contexto dele: o aviso do desafio só para ele, o tranco do empurrão, o teletransporte. Vale também para o que
    // vem de objetos da fase (o coordenador dos desafios), que não são de ninguém.
    [HarmonyPatch(typeof(NetworkBehaviour), "SendTargetRPCInternal")]
    static class TargetRpcToThisGame
    {
        static readonly Func<Type, string, int> methodHash =
            AccessTools.MethodDelegate<Func<Type, string, int>>(AccessTools.Method(typeof(Mirror.RemoteCalls.RemoteCallHelper), "GetMethodHash"));

        static bool Prefix(NetworkBehaviour __instance, ref NetworkConnection conn, Type invokeClass, string rpcName, NetworkWriter writer)
        {
            NetworkConnection target = conn ?? __instance.connectionToClient;
            if (!(target is LocalSeatConnection seatConn)) return true;
            Mirror.RemoteCalls.CmdDelegate rpc = NetworkClient.active ? Mirror.RemoteCalls.RemoteCallHelper.GetDelegate(methodHash(invokeClass, rpcName)) : null;
            if (rpc == null)
            {
                conn = NetworkServer.localConnection; // como antes: pela conexão de quem hospeda
                return true;
            }
            SeatContext.Token t = SeatContext.Enter(seatConn.Seat);
            try { rpc(__instance, new NetworkReader(writer.ToArraySegment()), null); }
            catch (Exception e) { Plugin.Log.LogWarning($"P{seatConn.Seat + 1}: {rpcName}: {e.Message}"); }
            finally { SeatContext.Exit(t); }
            return false;
        }
    }

    // O que chega para o personagem de um extra (ClientRpc, TargetRpc) roda como no jogo dele: ali ele é o jogador
    // local. Sem isso o jogo acha que o personagem é de outro PC e manda de novo, para sempre (o teleporte do
    // começo de cada minigame travava o jogo assim).
    [HarmonyPatch(typeof(NetworkIdentity), "HandleRemoteCall")]
    static class RemoteCallsInSeatContext
    {
        static void Prefix(NetworkIdentity __instance, MirrorInvokeType invokeType, out SeatContext.Token __state)
        {
            __state = default;
            if (invokeType == MirrorInvokeType.Command || __instance == null) return;
            int seat = SeatContext.SeatOf(__instance);
            if (seat >= 0) __state = SeatContext.Enter(seat);
        }

        static Exception Finalizer(Exception __exception, SeatContext.Token __state)
        {
            SeatContext.Exit(__state);
            return __exception;
        }
    }

    // No Start do jogador local, o jogo marca no servidor qual é o personagem de quem hospeda. Um jogador extra
    // passa por esse caminho (é "local" no contexto dele): a marca volta para o de quem hospeda.
    [HarmonyPatch(typeof(PlayerController), "Start")]
    static class KeepHostPlayerId
    {
        static void Postfix()
        {
            if (!SeatContext.IsExtra) return;
            NetworkIdentity host = NetworkServer.localConnection?.identity;
            if (host != null) CVars.SetValue("srv_iHostPlayerId", (int)host.netId, persist: false);
        }
    }
}
