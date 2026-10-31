using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace MicroWorksCoop.Net
{
    /// <summary>
    /// As cópias deste PC entram na partida da cópia 1 por TCP em 127.0.0.1, como se viessem pela internet.
    /// A cópia 1 continua aceitando quem vem pela Steam (MultiplexTransport: Steam + local).
    /// </summary>
    static class LocalNet
    {
        public const ushort DefaultPort = 27160;
        const string HostReadyName = @"Local\MicroWorksCoop.HostReady.";
        // o mesmo limite do transporte Steam do jogo (o Multiplex usa o menor dos dois)
        const int MaxMessageSize = 512000;

        public static ushort Port = DefaultPort;

        static Transport original;
        static GameObject muxObject;
        static IntPtr hostReady;

        static readonly AccessTools.FieldRef<NetworkManager, Transport> transportField =
            AccessTools.FieldRefAccess<NetworkManager, Transport>("transport");

        // ---- cópia 1: hospeda ----

        /// <summary>Antes do StartHost: a partida passa a aceitar também as cópias locais.</summary>
        public static void BeforeHost(NetworkManager nm)
        {
            if (muxObject != null) return;
            original = transportField(nm);
            muxObject = new GameObject("PartyAnywhere.LocalNet");
            muxObject.SetActive(false); // o Awake do Multiplex precisa dos transportes já definidos
            UnityEngine.Object.DontDestroyOnLoad(muxObject);
            LocalCopiesTransport local = Configure(muxObject.AddComponent<LocalCopiesTransport>());
            var mux = muxObject.AddComponent<MultiplexTransport>();
            mux.transports = new[] { original, local };
            muxObject.SetActive(true);
            transportField(nm) = mux;
            Transport.activeTransport = mux;
            Plugin.Log.LogInfo($"Partida local: as cópias entram por 127.0.0.1:{Port}");
        }

        /// <summary>Depois do StartHost: avisa as cópias que já dá para entrar.</summary>
        public static void AfterHost()
        {
            if (muxObject == null || hostReady != IntPtr.Zero) return;
            hostReady = Native.CreateMutexW(IntPtr.Zero, false, HostReadyName + Port);
        }

        /// <summary>A partida acabou: o jogo volta ao transporte dele, como se o mod não existisse.</summary>
        public static void AfterStopHost(NetworkManager nm)
        {
            if (hostReady != IntPtr.Zero) { Native.CloseHandle(hostReady); hostReady = IntPtr.Zero; }
            if (muxObject == null) return;
            transportField(nm) = original;
            Transport.activeTransport = original;
            UnityEngine.Object.Destroy(muxObject);
            muxObject = null;
            original = null;
        }

        /// <summary>A conexão veio de uma cópia deste PC (o 2º transporte do Multiplex), não da Steam nem do próprio host.</summary>
        public static bool IsLocalCopy(NetworkConnection conn) =>
            muxObject != null && conn is NetworkConnectionToClient && !(conn is ULocalConnectionToClient) && conn.connectionId % 2 == 1;

        // ---- cópias 2 a 10: entram ----

        public static bool HostIsReady()
        {
            IntPtr h = Native.OpenMutexW(Native.SYNCHRONIZE, false, HostReadyName + Port);
            if (h == IntPtr.Zero) return false;
            Native.CloseHandle(h);
            return true;
        }

        public static void JoinLocalHost(NetworkManager nm)
        {
            if (!(transportField(nm) is LocalCopiesTransport))
            {
                original = transportField(nm);
                if (original != null) original.enabled = false;
                transportField(nm) = Configure(nm.gameObject.AddComponent<LocalCopiesTransport>());
            }
            // o StartClient só copia o campo para o activeTransport na primeira vez (o NetworkManager já existe)
            Transport.activeTransport = transportField(nm);
            nm.networkAddress = "127.0.0.1";
            Plugin.Log.LogInfo($"Entrando na partida da cópia 1 (127.0.0.1:{Port})");
            nm.StartClient();
        }

        static LocalCopiesTransport Configure(LocalCopiesTransport t)
        {
            // o Telepathy só loga eventos de conexão (raros): vão para o log do mod, de qualquer thread
            Telepathy.Logger.Log = m => Plugin.Log.LogInfo($"[local] {m}");
            Telepathy.Logger.LogWarning = m => Plugin.Log.LogWarning($"[local] {m}");
            Telepathy.Logger.LogError = m => Plugin.Log.LogError($"[local] {m}");
            t.port = Port;
            t.serverMaxMessageSize = MaxMessageSize;
            t.clientMaxMessageSize = MaxMessageSize;
            return t;
        }

        // o servidor do Telepathy escuta só em 127.0.0.1: nada de rede, nem aviso do firewall
        internal static TcpListener CreateLoopbackListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            return listener;
        }

        static class Native
        {
            public const uint SYNCHRONIZE = 0x00100000;

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateMutexW(IntPtr attributes, bool initialOwner, string name);

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr OpenMutexW(uint access, bool inherit, string name);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr handle);
        }
    }

    /// <summary>TCP local entre as cópias. Só aceita 127.0.0.1, então quem conecta é uma cópia deste PC.</summary>
    class LocalCopiesTransport : TelepathyTransport
    {
        // o jogo confere o SteamID pelo endereço da conexão, menos para "localhost" (o próprio host); as cópias
        // locais entram assim, cada uma com o SteamID de cópia dela (Identity)
        public override string ServerGetClientAddress(int connectionId) => "localhost";

        public override string ToString() => $"Party Anywhere local ({port})";
    }

    [HarmonyPatch(typeof(Telepathy.Server), "Listen")]
    static class LoopbackListenPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var create = AccessTools.Method(typeof(TcpListener), nameof(TcpListener.Create), new[] { typeof(int) });
            var loopback = AccessTools.Method(typeof(LocalNet), nameof(LocalNet.CreateLoopbackListener));
            bool found = false;
            foreach (CodeInstruction ci in code)
            {
                if (ci.Calls(create)) { ci.opcode = OpCodes.Call; ci.operand = loopback; found = true; }
                yield return ci;
            }
            if (!found) Plugin.Log.LogError("Telepathy mudou: o servidor local não foi restrito a 127.0.0.1");
        }
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartHost))]
    static class StartHostPatch
    {
        static void Prefix(NetworkManager __instance)
        {
            if (Plugin.IsLeader) LocalNet.BeforeHost(__instance);
        }

        static void Postfix() => LocalNet.AfterHost();
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StopHost))]
    static class StopHostPatch
    {
        static void Postfix(NetworkManager __instance) => LocalNet.AfterStopHost(__instance);
    }
}
