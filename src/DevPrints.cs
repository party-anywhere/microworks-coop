using System.IO;
using UnityEngine;

namespace MicroWorksCoop
{
    /// <summary>
    /// Para desenvolver (PrintsACadaSegundos): grava a tela do jogo, como ela sai na TV, em BepInEx\prints a cada tantos
    /// segundos. Sem nada por cima (é o próprio jogo que grava), para conferir a tela dividida e fazer a capa.
    /// </summary>
    class DevPrints : MonoBehaviour
    {
        public float Every = 5f;
        float next;
        int count;

        void Update()
        {
            if (Time.unscaledTime < next || Local.SplitScreen.LocalPlayers < 2) return;
            next = Time.unscaledTime + Every;
            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "prints");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"tela_{++count:000}.png"));
        }
    }
}
