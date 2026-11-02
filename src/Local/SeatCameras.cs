using System.Collections.Generic;
using HarmonyLib;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// O jogo guarda uma fila de câmeras e a primeira é "a câmera ativa": dela sai a mira (o que está no meio da tela),
    /// o "usar" (pegar o caranguejo, apertar botões), a direção do arremesso e do tiro, e o som. Pela internet, cada
    /// jogo tem a sua fila. Aqui era uma fila só: a mira de um jogador do sofá saía da câmera de quem hospeda (o P2
    /// só pegava o que o P1 olhava), e a câmera de um jogador do sofá podia virar a ativa de quem hospeda.
    ///
    /// Cada jogador do sofá ganha a sua fila; no contexto dele, "a câmera ativa" é a dele.
    /// </summary>
    static class SeatCameras
    {
        static readonly List<Camera>[] queues = new List<Camera>[Client.MaxSeats];
        static readonly AccessTools.FieldRef<GlobalGameEssentials, LayerMask> globalMask = AccessTools.FieldRefAccess<GlobalGameEssentials, LayerMask>("globalLayerMask");
        static readonly Vector3 center = new Vector3(0.5f, 0.5f, 0f);

        static int SeatOf(Camera cam) => SeatContext.IsExtra ? SeatContext.Seat : SeatContext.SeatOf(cam);

        static Camera Active(int seat)
        {
            List<Camera> q = queues[seat];
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                    if (q[i] != null) return q[i];
            PlayerController pc = SeatContext.PlayerOf(seat);
            return pc != null ? pc.playerCamera : null;
        }

        [HarmonyPatch(typeof(GlobalGameEssentials), nameof(GlobalGameEssentials.AddToCameraQueue))]
        static class Add
        {
            static bool Prefix(Camera cam)
            {
                int seat = cam != null ? SeatOf(cam) : -1;
                if (seat < 0) return true;
                List<Camera> q = queues[seat] ?? (queues[seat] = new List<Camera>());
                q.Remove(cam);
                q.Add(cam);
                return false;
            }
        }

        [HarmonyPatch(typeof(GlobalGameEssentials), nameof(GlobalGameEssentials.RemoveFromCameraQueue))]
        static class Remove
        {
            static bool Prefix(Camera cam)
            {
                bool ours = false;
                foreach (List<Camera> q in queues) if (q != null && q.Remove(cam)) ours = true;
                Clean();
                return !ours;
            }

            static void Clean()
            {
                foreach (List<Camera> q in queues) q?.RemoveAll(c => c == null);
            }
        }

        [HarmonyPatch(typeof(GlobalGameEssentials), nameof(GlobalGameEssentials.ActiveCamera), MethodType.Getter)]
        static class ActiveCamera
        {
            static bool Prefix(ref Camera __result)
            {
                if (!SeatContext.IsExtra) return true;
                __result = Active(SeatContext.Seat);
                return false;
            }
        }

        [HarmonyPatch(typeof(GlobalGameEssentials), nameof(GlobalGameEssentials.GetScreenCenterWorldPoint))]
        static class CenterPoint
        {
            static bool Prefix(GlobalGameEssentials __instance, ref Vector3 __result)
            {
                if (!SeatContext.IsExtra) return true;
                Camera cam = Active(SeatContext.Seat);
                if (cam == null) return true;
                Ray ray = cam.ViewportPointToRay(center);
                __result = Physics.Raycast(ray, out RaycastHit hit, 1000f, globalMask(__instance)) ? hit.point : ray.GetPoint(1000f);
                return false;
            }
        }

        [HarmonyPatch(typeof(GlobalGameEssentials), nameof(GlobalGameEssentials.GetScreenCenterRay))]
        static class CenterRay
        {
            static bool Prefix(ref Ray __result)
            {
                if (!SeatContext.IsExtra) return true;
                Camera cam = Active(SeatContext.Seat);
                if (cam == null) return true;
                __result = cam.ViewportPointToRay(center);
                return false;
            }
        }
    }
}
