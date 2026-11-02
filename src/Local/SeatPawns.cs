using System;
using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// O kart (Kai Kart) e o sapo (Jetfrogs) são entregues quando "todos chegaram" à fase. Com uma pessoa só no lobby
    /// da Steam, esse aviso sai assim que a fase carrega, antes de os jogadores do sofá nascerem (eles nascem logo
    /// depois): só quem hospeda ganhava o kart. Enquanto a fase dá veículos e um jogador do sofá está sem o dele, a
    /// entrega do jogo roda de novo (ela pula quem já tem).
    ///
    /// E na largada, o "pode andar" do kart solta só o jogador local: solta também os do sofá.
    /// </summary>
    static class SeatPawns
    {
        // depois que alguém do sofá nasce, por quanto tempo conferir
        const float Window = 10f, Every = 0.5f;
        static float until = -1f, next;

        static readonly Action<KartRaceBossStage, AllPlayersJoinedEvent> kartJoined =
            AccessTools.MethodDelegate<Action<KartRaceBossStage, AllPlayersJoinedEvent>>(AccessTools.Method(typeof(KartRaceBossStage), "OnAllPlayersJoined"));
        static readonly Action<JetfrogsBossStage, AllPlayersJoinedEvent> frogJoined =
            AccessTools.MethodDelegate<Action<JetfrogsBossStage, AllPlayersJoinedEvent>>(AccessTools.Method(typeof(JetfrogsBossStage), "OnAllPlayersJoined"));

        /// <summary>Um jogador do sofá nasceu: confere por um tempo se a fase lhe deve um veículo.</summary>
        public static void Joined() => until = Time.unscaledTime + Window;

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now > until || now < next || !NetworkServer.active) return;
            next = now + Every;
            // a fase já deu o veículo de quem hospeda: é uma fase de veículos e a entrega já aconteceu
            NetworkIdentity hostId = NetworkServer.localConnection?.identity;
            PlayerController host = hostId != null ? hostId.GetComponent<PlayerController>() : null;
            if (host == null || !host.IsPossessingPawn) return;
            bool missing = false;
            for (int s = 0; s < Client.MaxSeats && !missing; s++)
            {
                PlayerController pc = SeatContext.PlayerOf(s);
                missing = pc != null && pc.Alive && !pc.IsPossessingPawn;
            }
            if (!missing) return;
            var kart = UnityEngine.Object.FindObjectOfType<KartRaceBossStage>();
            if (kart != null) { kartJoined(kart, default); return; }
            var frogs = UnityEngine.Object.FindObjectOfType<JetfrogsBossStage>();
            if (frogs != null) frogJoined(frogs, default);
        }

        [HarmonyPatch(typeof(KartRaceEndGoal), nameof(KartRaceEndGoal.UserCode_RpcUnlockMovement))]
        static class KartGo
        {
            static void Postfix()
            {
                for (int s = 0; s < Client.MaxSeats; s++)
                {
                    PlayerController pc = SeatContext.PlayerOf(s);
                    if (pc == null) continue;
                    SeatContext.Token t = SeatContext.Enter(s);
                    try { pc.LockMovement(MovementLockingOptions.None); }
                    finally { SeatContext.Exit(t); }
                }
            }
        }
    }
}
