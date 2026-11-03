using System.IO;
using MicroWorksCoop.Local;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop
{
    /// <summary>
    /// Para desenvolver (PrintsACadaSegundos): grava a tela do jogo, como ela sai na TV, em BepInEx\prints a cada tantos
    /// segundos. Sem nada por cima (é o próprio jogo que grava), para conferir a tela dividida e fazer a capa. Com
    /// PrintsEmRoda, antes de cada print os jogadores ficam em roda, de frente uns para os outros (cada parte mostra os
    /// outros bonecos, com as placas).
    /// </summary>
    class DevPrints : MonoBehaviour
    {
        const float Radius = 2.2f, SettleBeforeShot = 0.8f;

        public float Every = 5f;
        public bool Circle;
        float next, shotAt = -1f;
        int count;

        void Update()
        {
            if (shotAt > 0f && Time.unscaledTime >= shotAt)
            {
                shotAt = -1f;
                Shoot();
            }
            if (Time.unscaledTime < next || SplitScreen.LocalPlayers < 2) return;
            next = Time.unscaledTime + Every;
            if (Circle && Pose()) shotAt = Time.unscaledTime + SettleBeforeShot;
            else Shoot();
        }

        void Shoot()
        {
            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "prints");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"tela_{++count:000}.png"));
        }

        // todos numa roda à frente de quem hospeda, olhando para o meio
        static bool Pose()
        {
            NetworkIdentity hostId = NetworkServer.localConnection?.identity;
            PlayerController host = hostId != null ? hostId.GetComponent<PlayerController>() : null;
            if (host == null || !host.Alive || host.IsPossessingPawn) return false;
            Vector3 forward = Vector3.ProjectOnPlane(host.transform.forward, Vector3.up).normalized;
            Vector3 center = host.transform.position + forward * Radius;
            int n = SplitScreen.LocalPlayers, i = 0;
            Place(host, -1, center, -forward, i++, n);
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                PlayerController pc = SeatContext.PlayerOf(s);
                if (pc != null && pc.Alive && !pc.IsPossessingPawn) Place(pc, s, center, -forward, i++, n);
            }
            return true;
        }

        static void Place(PlayerController pc, int seat, Vector3 center, Vector3 first, int i, int n)
        {
            Vector3 dir = Quaternion.Euler(0f, 360f * i / n, 0f) * first;
            Vector3 pos = center + dir * Radius;
            SeatContext.Token t = seat >= 0 ? SeatContext.Enter(seat) : default;
            try { pc.Teleport(pos, -dir, playEffect: false); }
            finally { SeatContext.Exit(t); }
        }
    }
}
