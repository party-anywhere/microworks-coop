using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Para desenvolver (DiagnosticoJogadores): anota no log o que cada jogador faz, para conferir que os do sofá jogam
    /// como quem hospeda. Armas na mão, acertos, quem passou ou falhou na rodada e, a cada 2 s, velocidade, correr,
    /// travas de movimento e o veículo de cada um.
    /// </summary>
    class SeatDiag : MonoBehaviour
    {
        const float Every = 2f;
        static bool on;
        public static bool On => on;
        float next;

        static readonly AccessTools.FieldRef<PlayerController, float> speed = AccessTools.FieldRefAccess<PlayerController, float>("currentSpeed");
        static readonly AccessTools.FieldRef<KartPawn, float> kartSpeed = AccessTools.FieldRefAccess<KartPawn, float>("currentSpeed");

        void Awake()
        {
            on = true;
            GlobalEvent<WeaponAttachedEvent>.AddListener(e => Plugin.Log.LogInfo($"[diag] {Who(e.Player)} pegou {(e.Weapon != null ? e.Weapon.GetType().Name : "?")}"));
        }

        public static string Who(PlayerController pc)
        {
            if (pc == null) return "ninguém";
            int seat = SeatContext.SeatOf(pc);
            if (seat >= 0) return $"P{seat + 1}";
            NetworkIdentity host = NetworkServer.localConnection?.identity;
            return host != null && host.gameObject == pc.gameObject ? $"P{Plugin.Seat + 1}" : pc.name;
        }

        void Update()
        {
            if (Time.unscaledTime < next || !NetworkServer.active) return;
            next = Time.unscaledTime + Every;
            NetworkIdentity hostId = NetworkServer.localConnection?.identity;
            if (hostId == null) return;
            var line = new System.Text.StringBuilder("[diag]");
            Add(line, hostId.GetComponent<PlayerController>());
            for (int s = 0; s < Client.MaxSeats; s++) Add(line, SeatContext.PlayerOf(s));
            Plugin.Log.LogInfo(line.ToString());
            if (GrabCrabs) Crabs(hostId.GetComponent<PlayerController>());
        }

        /// <summary>DiagnosticoPegarCaranguejo: no "Pegue um caranguejo", cada um sem nada na mão usa um caranguejo (pelo Use do jogo).</summary>
        public static bool GrabCrabs;

        static void Crabs(PlayerController host)
        {
            WeaponUsable[] crabs = FindObjectsOfType<WeaponUsable>();
            var free = new System.Collections.Generic.List<WeaponUsable>();
            foreach (WeaponUsable w in crabs) if (w.GetComponent<CrabAI>() != null) free.Add(w);
            int ai = FindObjectsOfType<CrabAI>().Length;
            if (ai == 0) return;
            Plugin.Log.LogInfo($"[diag] caranguejos na arena: {ai} ({free.Count} para pegar)");
            for (int s = -1; s < Client.MaxSeats && free.Count > 0; s++)
            {
                PlayerController pc = s < 0 ? host : SeatContext.PlayerOf(s);
                if (pc == null || pc.Weapon != null || !pc.Alive) continue;
                WeaponUsable crab = free[0];
                free.RemoveAt(0);
                SeatContext.Token t = s >= 0 ? SeatContext.Enter(s) : default;
                try { if (!crab.Used) crab.Use(pc); }
                finally { SeatContext.Exit(t); }
            }
        }

        static void Add(System.Text.StringBuilder line, PlayerController pc)
        {
            if (pc == null) return;
            line.Append($" | {Who(pc)} v={speed(pc):0.0}{(pc.Sprinting ? " correndo" : "")}");
            if (pc.LockingOptions != MovementLockingOptions.None) line.Append($" trava={pc.LockingOptions}");
            if (pc.speedMultiplier != 1f) line.Append($" x{pc.speedMultiplier:0.00}");
            if (pc.Weapon != null) line.Append($" arma={pc.Weapon.GetType().Name}");
            if (pc.IsPossessingPawn && pc.PossessedPawn is KartPawn kart)
                line.Append($" kart={kartSpeed(kart):0.0} esfera={(kart.sphere != null ? kart.sphere.velocity.magnitude : 0f):0.0} pos={(kart.sphere != null ? kart.sphere.position : kart.transform.position):F0} dirige={kart.IsPosessing} aut={kart.hasAuthority} dono={Who(kart.Controller)} esferaLigada={(kart.sphere != null && kart.sphere.gameObject.activeInHierarchy)} poder={(kart.powerupManager != null ? kart.powerupManager.HeldPowerup?.ToString() ?? "-" : "?")}");
            else line.Append($" pos={pc.transform.position:F0}");
            if (!pc.Alive) line.Append(" morto");
        }

        [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.TakeDamageAtPoint))]
        static class Hits
        {
            static void Postfix(PlayerController __instance, uint damagerId)
            {
                if (!on) return;
                PlayerController by = NetworkIdentity.spawned.TryGetValue(damagerId, out NetworkIdentity id) ? id.GetComponent<PlayerController>() : null;
                Plugin.Log.LogInfo($"[diag] {Who(__instance)} acertado por {Who(by)}");
            }
        }

        [HarmonyPatch(typeof(SodaCan), "Shake")]
        static class Shakes
        {
            static void Postfix(SodaCan __instance)
            {
                if (on) Plugin.Log.LogInfo($"[diag] {Who(__instance.parentPlayer)} chacoalhou ({Traverse.Create(__instance).Field("shakeAmount").GetValue<int>()}/{__instance.shakeCount})");
            }
        }

        [HarmonyPatch(typeof(SodaCan), "OnWeaponTick")]
        static class CanTick
        {
            static float next;
            static void Postfix(SodaCan __instance)
            {
                if (!on || Time.unscaledTime < next || __instance.parentPlayer == null || !__instance.parentPlayer.IsLocalController) return;
                if (SeatContext.IsExtra) return; // só a de quem hospeda: é a que não passava
                next = Time.unscaledTime + 1f;
                Vector4 m = __instance.parentPlayer.MousePositionValues;
                Plugin.Log.LogInfo($"[diag] lata de {Who(__instance.parentPlayer)}: ângulo {Traverse.Create(__instance).Field("accumalativeAngle").GetValue<float>():0} olhar {m} horizontal {__instance.isHorizontal} PDA {NonExclusiveState.IsNonExclusive}");
            }
        }

        [HarmonyPatch(typeof(MicrogameCoordinator), nameof(MicrogameCoordinator.MarkWinnerWithAdjustedTime))]
        static class Won
        {
            static void Postfix(uint playerId)
            {
                if (on && NetworkIdentity.spawned.TryGetValue(playerId, out NetworkIdentity id)) Plugin.Log.LogInfo($"[diag] {Who(id.GetComponent<PlayerController>())} passou");
            }
        }

        [HarmonyPatch(typeof(MicrogameCoordinator), nameof(MicrogameCoordinator.MarkLoser))]
        static class Lost
        {
            static void Postfix(uint playerId)
            {
                if (on && NetworkIdentity.spawned.TryGetValue(playerId, out NetworkIdentity id)) Plugin.Log.LogInfo($"[diag] {Who(id.GetComponent<PlayerController>())} falhou");
            }
        }
    }
}
