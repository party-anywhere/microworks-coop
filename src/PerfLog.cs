using System.Diagnostics;
using UnityEngine;

namespace MicroWorksCoop
{
    /// <summary>
    /// Para medir quantas cópias o PC aguenta: a cada 10 s, a média de quadros por segundo, o pior quadro e a memória
    /// desta cópia no log do BepInEx. Liga em BepInEx\config\com.joaocaioo.microworks-coop.cfg (MedirDesempenho).
    /// </summary>
    class PerfLog : MonoBehaviour
    {
        const float Every = 10f;

        float since;
        int frames;
        float worst;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            since += dt;
            frames++;
            if (dt > worst) worst = dt;
            if (since < Every) return;
            long mb = Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024);
            Plugin.Log.LogInfo($"desempenho P{Plugin.Seat + 1}: {frames / since:0.0} fps, pior quadro {worst * 1000f:0} ms, {mb} MB, janela {Screen.width}x{Screen.height}");
            since = 0f;
            frames = 0;
            worst = 0f;
        }
    }
}
