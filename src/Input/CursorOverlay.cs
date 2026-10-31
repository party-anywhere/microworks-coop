using UnityEngine;

namespace MicroWorksCoop.Input
{
    /// <summary>Desenha o cursor virtual desta cópia nos menus: um anel com a cor do jogador.</summary>
    class CursorOverlay : MonoBehaviour
    {
        const int Size = 32;

        static Texture2D ring;

        /// <summary>A cor da vaga (o Hub dá; P1 é sempre azul).</summary>
        public static Color SeatColor = Color.white;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !PadInput.InMenu) return;
            if (ring == null) ring = MakeRing();
            Vector2 p = PadInput.CursorPosition;
            GUI.depth = -1000;
            Color old = GUI.color;
            GUI.color = SeatColor;
            GUI.DrawTexture(new Rect(p.x - Size / 2f, Screen.height - p.y - Size / 2f, Size, Size), ring);
            GUI.color = old;
        }

        static Texture2D MakeRing()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            float c = (Size - 1) / 2f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    // anel branco (tingido pela cor do jogador) com borda escura, e um ponto no centro
                    float ringA = Mathf.Clamp01(1f - Mathf.Abs(d - 11f) / 2.5f);
                    float edgeA = Mathf.Clamp01(1f - Mathf.Abs(d - 11f) / 4f) - ringA;
                    float dotA = Mathf.Clamp01(2.5f - d);
                    float a = Mathf.Max(ringA, dotA);
                    byte v = (byte)(255 * a / Mathf.Max(a + edgeA, 0.001f));
                    px[y * Size + x] = new Color32(v, v, v, (byte)(255 * Mathf.Clamp01(a + edgeA)));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
