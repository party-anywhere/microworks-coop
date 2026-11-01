using System.Collections;
using PartyAnywhere;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// A tela do jogo no celular: quem ligou "Tela do jogo no celular" sai da TV e a câmera dele desenha numa imagem
    /// pequena, lida da placa de vídeo sem esperar (leitura assíncrona) e entregue ao Hub, que manda para o celular.
    /// A tela dividida (SplitScreen) decide quem está na TV e quem está no celular; aqui ficam as imagens e a entrega.
    /// </summary>
    class PhoneViews : MonoBehaviour
    {
        const int Width = 640, Height = 360; // o padrão do SDK: chega rápido ao celular e custa pouco desenhar
        const int EveryFrames = 2;           // o Hub manda até 30 por segundo, sempre o quadro mais novo

        static readonly RenderTexture[] targets = new RenderTexture[Client.MaxSeats];
        static readonly bool[] reading = new bool[Client.MaxSeats];

        /// <summary>O jogador desta vaga (do Hub) quer a parte dele no celular.</summary>
        public static bool Wants(int seat) => Plugin.Hub != null && Plugin.Hub.ViewWanted(seat);

        /// <summary>A imagem onde a câmera de quem está no celular desenha.</summary>
        public static RenderTexture TargetFor(int seat)
        {
            RenderTexture rt = targets[seat];
            if (rt != null) return rt;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = $"PartyAnywhere.Phone.P{seat + 1}" };
            rt.Create();
            Plugin.Log.LogInfo($"P{seat + 1}: tela do jogo no celular");
            return targets[seat] = rt;
        }

        public static bool IsOurs(RenderTexture rt)
        {
            if (rt == null) return false;
            foreach (RenderTexture t in targets) if (t == rt) return true;
            return false;
        }

        /// <summary>Voltou para a TV (ou saiu): a imagem dele não é mais desenhada nem entregue.</summary>
        public static void Release(int seat)
        {
            RenderTexture rt = targets[seat];
            if (rt == null) return;
            targets[seat] = null;
            if (!reading[seat]) Destroy(rt); // senão, a leitura em andamento termina e solta
            Plugin.Log.LogInfo($"P{seat + 1}: de volta à TV");
        }

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();
            for (int frame = 0; ; frame++)
            {
                yield return endOfFrame;
                if (frame % EveryFrames != 0) continue;
                for (int seat = 0; seat < Client.MaxSeats; seat++)
                {
                    RenderTexture rt = targets[seat];
                    if (rt == null || reading[seat]) continue;
                    reading[seat] = true;
                    int s = seat;
                    AsyncGPUReadback.Request(rt, 0, TextureFormat.BGRA32, r => Deliver(s, rt, r));
                }
            }
        }

        static unsafe void Deliver(int seat, RenderTexture rt, AsyncGPUReadbackRequest r)
        {
            reading[seat] = false;
            if (targets[seat] != rt) { Destroy(rt); return; } // voltou para a TV enquanto lia
            if (r.hasError || Plugin.Hub == null) return;
            var data = r.GetData<byte>();
            var p = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
            // a imagem vem de baixo para cima; o Hub quer de cima para baixo: começa da última linha e anda para trás
            int row = r.width * 4;
            Plugin.Hub.SubmitView(seat, p + (long)(r.height - 1) * row, r.width, r.height, -row);
        }
    }
}
