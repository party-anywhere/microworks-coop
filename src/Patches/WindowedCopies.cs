using HarmonyLib;
using UnityEngine;

namespace MicroWorksCoop.Patches
{
    /// <summary>
    /// Com uma cópia por jogador, o Hub dá a cada cópia uma parte da tela e passa o tamanho dela em
    /// "-screen-width"/"-screen-height". O MicroWorks aplica a resolução salva nas configurações (tela cheia) por
    /// cima disso, e a janela não ficava na parte dela. Aqui cada cópia fica em janela, do tamanho que o Hub pediu;
    /// as configurações de vídeo da pessoa não mudam (nada é gravado), então o jogo normal abre como sempre.
    /// </summary>
    [HarmonyPatch(typeof(GraphicsHelper), nameof(GraphicsHelper.ApplyResolutionFromCVars))]
    static class WindowedCopies
    {
        static bool Prefix()
        {
            if (!Plugin.OpenedByHubAsCopy) return true;
            int w = Plugin.ArgInt("-screen-width", Screen.width);
            int h = Plugin.ArgInt("-screen-height", Screen.height);
            if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width != w || Screen.height != h)
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            return false;
        }
    }
}
