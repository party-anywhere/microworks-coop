using System.Threading.Tasks;
using HarmonyLib;
using MicroWorksCoop.Net;
using PartyAnywhere;
using Steamworks;
using UnityEngine;

namespace MicroWorksCoop.Ui
{
    /// <summary>
    /// O avatar de quem joga neste PC: a cor da vaga com "P1".."P10" em branco, no lugar da foto da Steam (que
    /// seria a mesma para todo mundo) e do "?" das cópias. Quem joga pela internet continua com a foto dele.
    /// </summary>
    static class SeatAvatar
    {
        const int Size = 128;
        const int Corner = 22;
        const int Scale = 7; // cada "pixel" da fonte 5x7 vira 7x7

        static readonly Texture2D[] cache = new Texture2D[Client.MaxSeats];
        static readonly Color32[] cachedColor = new Color32[Client.MaxSeats];

        public static Texture2D Get(int seat)
        {
            Color32 color = Identity.SeatColor(seat);
            Texture2D tex = cache[seat];
            if (tex != null && cachedColor[seat].Equals(color)) return tex;
            if (tex != null) Object.Destroy(tex);
            cache[seat] = tex = Draw($"P{seat + 1}", color);
            cachedColor[seat] = color;
            return tex;
        }

        static Texture2D Draw(string label, Color32 color)
        {
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[y * Size + x] = InsideRounded(x, y) ? color : new Color32(0, 0, 0, 0);

            int glyphs = label.Length;
            int width = (glyphs * Font.Width + (glyphs - 1)) * Scale;
            int left = (Size - width) / 2;
            int bottom = (Size - Font.Height * Scale) / 2;
            var ink = new Color32(255, 255, 255, 255);
            var shade = new Color32((byte)(color.r / 2), (byte)(color.g / 2), (byte)(color.b / 2), 255);
            for (int i = 0; i < glyphs; i++)
            {
                int gx = left + i * (Font.Width + 1) * Scale;
                // sombra um passo abaixo e à direita, depois a letra: lê bem em qualquer cor de vaga
                Stamp(px, label[i], gx + Scale / 2, bottom - Scale / 2, shade);
                Stamp(px, label[i], gx, bottom, ink);
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static bool InsideRounded(int x, int y)
        {
            int cx = x < Corner ? Corner : x > Size - 1 - Corner ? Size - 1 - Corner : x;
            int cy = y < Corner ? Corner : y > Size - 1 - Corner ? Size - 1 - Corner : y;
            int dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= Corner * Corner;
        }

        static void Stamp(Color32[] px, char c, int left, int bottom, Color32 ink)
        {
            string[] rows = Font.Glyph(c);
            for (int row = 0; row < Font.Height; row++)
            {
                string bits = rows[Font.Height - 1 - row]; // a textura cresce para cima
                for (int col = 0; col < Font.Width; col++)
                {
                    if (bits[col] != '#') continue;
                    for (int sy = 0; sy < Scale; sy++)
                        for (int sx = 0; sx < Scale; sx++)
                        {
                            int x = left + col * Scale + sx, y = bottom + row * Scale + sy;
                            if (x >= 0 && x < Size && y >= 0 && y < Size) px[y * Size + x] = ink;
                        }
                }
            }
        }

        /// <summary>Fonte 5x7 só com o que o avatar usa: P e os dígitos.</summary>
        static class Font
        {
            public const int Width = 5, Height = 7;

            public static string[] Glyph(char c)
            {
                switch (c)
                {
                    case 'P': return new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." };
                    case '0': return new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." };
                    case '1': return new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." };
                    case '2': return new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" };
                    case '3': return new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." };
                    case '4': return new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." };
                    case '5': return new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." };
                    case '6': return new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." };
                    case '7': return new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." };
                    case '8': return new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." };
                    case '9': return new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." };
                    default: return new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." };
                }
            }
        }
    }

    [HarmonyPatch(typeof(SteamworksManager), nameof(SteamworksManager.GetTextureFromAvatar))]
    static class SeatAvatarPatch
    {
        static bool Prefix(Friend friend, ref Task<Texture2D> __result)
        {
            if (!Identity.TryLocalSeat(friend.Id.Value, out int seat)) return true;
            __result = Task.FromResult(SeatAvatar.Get(seat));
            return false;
        }
    }
}
