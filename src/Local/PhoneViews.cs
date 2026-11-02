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
        // uma leitura leva de 1 a 3 quadros para voltar: várias ao mesmo tempo, uma pedida a cada quadro (o Hub
        // manda até 30 por segundo, sempre o mais novo)
        const int MaxReading = 3;

        static readonly RenderTexture[] targets = new RenderTexture[Client.MaxSeats];
        static readonly int[] reading = new int[Client.MaxSeats];

        /// <summary>O jogador desta vaga (do Hub) quer a parte dele no celular.</summary>
        public static bool Wants(int seat) => Plugin.Hub != null && Plugin.Hub.ViewWanted(seat);

        /// <summary>
        /// A imagem onde a câmera de quem está no celular desenha: no formato da tela do celular dele (o Hub diz;
        /// 540 de altura), assim enche a tela, sem faixas. Muda junto quando a pessoa gira o celular.
        /// </summary>
        public static RenderTexture TargetFor(int seat)
        {
            RenderTexture rt = targets[seat];
            int w = 960, h = 540;
            if (Plugin.Hub != null) Plugin.Hub.ViewSize(seat, out w, out h);
            if (rt != null && rt.width == w && rt.height == h) return rt;
            if (rt != null) Release(seat);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = $"PartyAnywhere.Phone.P{seat + 1}" };
            rt.Create();
            Plugin.Log.LogInfo($"P{seat + 1}: tela do jogo no celular ({w}x{h})");
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
            if (reading[seat] == 0) Destroy(rt); // senão, a última leitura em andamento termina e solta
            Plugin.Log.LogInfo($"P{seat + 1}: de volta à TV");
        }

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                for (int seat = 0; seat < Client.MaxSeats; seat++)
                {
                    RenderTexture rt = targets[seat];
                    if (rt == null || reading[seat] >= MaxReading) continue;
                    reading[seat]++;
                    int s = seat;
                    AsyncGPUReadback.Request(rt, 0, TextureFormat.BGRA32, r => Deliver(s, rt, r));
                }
            }
        }

        static unsafe void Deliver(int seat, RenderTexture rt, AsyncGPUReadbackRequest r)
        {
            reading[seat]--;
            if (targets[seat] != rt) // voltou para a TV enquanto lia
            {
                if (reading[seat] == 0 && rt != null) Destroy(rt);
                return;
            }
            if (r.hasError || Plugin.Hub == null) return;
            var data = r.GetData<byte>();
            var p = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
            // a imagem vem de baixo para cima; o Hub quer de cima para baixo: começa da última linha e anda para trás
            int row = r.width * 4;
            Plugin.Hub.SubmitView(seat, p + (long)(r.height - 1) * row, r.width, r.height, -row);
        }
    }
}
