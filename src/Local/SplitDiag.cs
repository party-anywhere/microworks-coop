using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Ferramenta de desenvolvimento (DiagnosticoTelaDividida): a cada 2 s, onde cada jogador extra aparece na câmera
    /// de quem hospeda e se o corpo dele foi desenhado por alguma câmera. É o que confirma, sem olhar a tela, que um
    /// jogador vê o outro. Também vigia travamentos: se o jogo para de desenhar por 8 s, interrompe a linha
    /// principal uma vez, e o erro que isso gera sai no log com o lugar onde ela estava presa.
    /// </summary>
    class SplitDiag : MonoBehaviour
    {
        const int HangMs = 8000;
        float next;
        static int beat;
        // as chamadas de rede (comandos e RPCs) do quadro atual: um travamento na rede é um vaivém sem fim
        static readonly Dictionary<string, int> calls = new Dictionary<string, int>();

        static void Count(Type invokeClass, string name, string kind)
        {
            lock (calls)
            {
                string key = kind + " " + invokeClass.Name + "." + name;
                calls.TryGetValue(key, out int n);
                calls[key] = n + 1;
            }
        }

        static void CountCommand(Type invokeClass, string cmdName) => Count(invokeClass, cmdName, "Cmd");
        static void CountRpc(Type invokeClass, string rpcName) => Count(invokeClass, rpcName, "Rpc");
        static void CountTargetRpc(Type invokeClass, string rpcName) => Count(invokeClass, rpcName, "TargetRpc");

        static string Busiest()
        {
            lock (calls)
                return string.Join(", ", calls.OrderByDescending(c => c.Value).Take(8).Select(c => $"{c.Key} x{c.Value}"));
        }

        void Awake()
        {
            var harmony = new Harmony(Plugin.Id + ".diag");
            foreach ((string method, string counter) in new[] { ("SendCommandInternal", nameof(CountCommand)), ("SendRPCInternal", nameof(CountRpc)), ("SendTargetRPCInternal", nameof(CountTargetRpc)) })
                harmony.Patch(AccessTools.Method(typeof(NetworkBehaviour), method), new HarmonyMethod(typeof(SplitDiag), counter));

            Thread main = Thread.CurrentThread;
            var watch = new Thread(() =>
            {
                int seen = -1, still = 0;
                while (true)
                {
                    Thread.Sleep(500);
                    int now = Volatile.Read(ref beat);
                    still = now == seen ? still + 500 : 0;
                    seen = now;
                    if (still < HangMs) continue;
                    Plugin.Log.LogError($"[diag] o jogo travou há {still / 1000} s; interrompendo para saber onde. Rede neste quadro: {Busiest()}");
                    main.Abort();
                    return;
                }
            }) { IsBackground = true, Name = "MicroWorksCoop.HangWatch" };
            watch.Start();
        }

        void LateUpdate()
        {
            Interlocked.Increment(ref beat);
            lock (calls) calls.Clear();
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            NetworkIdentity hostId = NetworkServer.active ? NetworkServer.localConnection?.identity : null;
            var host = hostId != null ? hostId.GetComponent<PlayerController>() : null;
            Camera cam = host != null ? host.playerCamera : null;
            if (cam == null) return;
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                PlayerController pc = SeatContext.PlayerOf(s);
                if (pc == null) continue;
                Vector3 vp = cam.WorldToViewportPoint(pc.transform.position + Vector3.up);
                string v = string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1:0.00} {2:0.0}", vp.x, vp.y, vp.z);
                Plugin.Log.LogInfo($"[diag] P{s + 1} na câmera de P{Plugin.Seat + 1}: {v} corpo desenhado={pc.playerMesh.isVisible} camada={pc.playerMesh.gameObject.layer} ligado={pc.playerMesh.enabled}");
            }
        }
    }
}
