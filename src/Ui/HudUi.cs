using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MicroWorksCoop.Ui
{
    /// <summary>
    /// Peças de UI simples que o mod põe no HUD de cada jogador (painel de resposta, quem é quem), no estilo do HUD
    /// do jogo: a fonte é a do PDA. Ficam dentro da parte do jogador (a moldura da SplitScreen), então vão junto
    /// para a imagem do celular de quem joga por lá.
    /// </summary>
    static class HudUi
    {
        /// <summary>Onde pôr algo no HUD do jogador: a moldura da parte dele (ou a tela do HUD, sem divisão).</summary>
        public static Transform PartOf(PlayerController pc)
        {
            if (pc == null || pc.playerHUD == null) return null;
            foreach (Canvas canvas in pc.playerHUD.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                Transform frame = canvas.transform.Find("PartyAnywhere.Part");
                return frame != null ? frame : canvas.transform;
            }
            return null;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        public static Image Box(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, Vector2 at, Vector2 size, float fontSize, Color color)
        {
            RectTransform rt = Rect("Text", parent);
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (PDA.singleton != null) t.font = PDA.singleton.inputQuestion.font;
            t.text = text;
            t.fontSize = fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMin = 14f;
            t.fontSizeMax = fontSize;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Texto escuro ou claro, o que se lê melhor sobre a cor (P10 é branco).</summary>
        public static Color InkOn(Color background) =>
            background.r * 0.299f + background.g * 0.587f + background.b * 0.114f > 0.6f ? new Color(0.05f, 0.05f, 0.07f) : Color.white;
    }
}
