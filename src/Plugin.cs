using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mirror;
using MicroWorksCoop.Input;
using MicroWorksCoop.Net;
using PartyAnywhere;
using UnityEngine;
using EventType = PartyAnywhere.EventType;

namespace MicroWorksCoop
{
    /// <summary>
    /// Cada jogador roda a sua cópia do MicroWorks neste PC. A cópia 1 (sem argumento) conecta ao Hub; as
    /// outras abrem com "-pa-seat N" (N = 1..9, a vaga P2..P10) e só leem a vaga delas.
    /// </summary>
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.joaocaioo.microworks-coop";
        public const string Name = "MicroWorks Co-op Local";
        public const string Version = "0.1.0";

        const float ConnectRetrySeconds = 2f;

        internal static ManualLogSource Log;

        /// <summary>A vaga desta cópia (0..9 → P1..P10).</summary>
        public static int Seat { get; private set; }
        /// <summary>A vaga da cópia que hospeda a partida e conecta ao Hub (o Hub passa em PA_HOST_SEAT).</summary>
        public static int HostSeat { get; private set; }
        public static bool IsLeader => Seat == HostSeat;

        /// <summary>O Hub abriu esta cópia num jogo de uma cópia por jogador (ela ocupa só a parte dela da tela).</summary>
        public static bool OpenedByHubAsCopy { get; private set; }

        /// <summary>Preset de vídeo do jogo para as cópias abertas pelo Hub ("" = as configurações da pessoa).</summary>
        public static string CopyQualityPreset { get; private set; } = "";

        /// <summary>
        /// false quando a cópia 1 entra na partida de outra pessoa pela internet: ali as cópias não entram, então o
        /// mod não mexe em nada e o jogo segue como sempre (teclado, mouse e controles do PC).
        /// </summary>
        public static bool Active => !IsLeader || !NetworkClient.isConnected || NetworkServer.active;

        public static Client Hub { get; private set; }

        public static bool DevSkipTutorial { get; private set; }

        float nextConnectTry;
        bool announced;

        void Awake()
        {
            Log = Logger;
            string[] args = Environment.GetCommandLineArgs();
            // a vaga desta cópia e a da que hospeda: PA_SEAT e PA_HOST_SEAT (0 a 9), como o Hub passa a cada
            // cópia; "-pa-seat" para testar à mão (aí quem hospeda é P1)
            OpenedByHubAsCopy = EnvSeat("PA_SEAT").HasValue;
            Seat = EnvSeat("PA_SEAT") ?? ReadIntArg(args, "-pa-seat", 0, Client.MaxSeats - 1, 0);
            HostSeat = EnvSeat("PA_HOST_SEAT") ?? 0;
            LocalNet.Port = (ushort)ReadIntArg(args, "-pa-port", 1024, 65535, LocalNet.DefaultPort);
            Log.LogInfo($"{Name} {Version}: cópia da vaga P{Seat + 1} ({(IsLeader ? "hospeda e conecta ao Hub" : "cópia extra")})");
            // cada cópia segue rodando e lendo o controle dela sem ser a janela em foco
            Application.runInBackground = true;
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<CursorOverlay>();
            if (!IsLeader) gameObject.AddComponent<CopyJoiner>();
            // para desenvolver: pular o tutorial e criar a partida sozinho (nada é gravado nas configurações do
            // jogo); pela linha de comando ou pelo BepInEx\config\com.joaocaioo.microworks-coop.cfg
            bool devSkip = Config.Bind("Desenvolvimento", "PularTutorial", false, "A cópia que hospeda pula o tutorial.").Value;
            bool devHost = Config.Bind("Desenvolvimento", "CriarPartidaSozinho", false, "A cópia que hospeda cria a partida assim que o menu carrega.").Value;
            CopyQualityPreset = Config.Bind("Video", "QualidadeNasCopias", "VeryLow",
                "Preset de vídeo do jogo nas cópias abertas pelo Hub (VeryLow, Low, Medium, High, Ultra; vazio = as suas configurações). Não muda as suas configurações.").Value;
            if (Config.Bind("Desenvolvimento", "MedirDesempenho", false, "Cada cópia anota fps e memória no log a cada 10 s.").Value)
                gameObject.AddComponent<PerfLog>();
            DevSkipTutorial = devSkip || Array.IndexOf(args, "-pa-skip-tutorial") >= 0;
            if (IsLeader && (devHost || Array.IndexOf(args, "-pa-dev-host") >= 0)) gameObject.AddComponent<DevHost>();
        }

        void Update()
        {
            if (Hub == null)
            {
                if (Time.unscaledTime >= nextConnectTry) TryConnect();
            }
            else
            {
                PadInput.Tick();
                DrainEvents();
            }
        }

        void OnApplicationQuit()
        {
            Hub?.Dispose();
            Hub = null;
        }

        void TryConnect()
        {
            nextConnectTry = Time.unscaledTime + ConnectRetrySeconds;
            Result r = IsLeader ? Client.Connect(out Client c) : Client.Observe(out c);
            if (r != Result.Ok)
            {
                if (r != Result.HubNotRunning) Log.LogWarning($"Party Anywhere: {Describe(r)}");
                return;
            }
            Hub = c;
            Hub.Poll();
            UpdateSeatColor();
            for (int s = 0; s < Client.MaxSeats; s++)
                if (Hub.GetSeat(s, out SeatInfo seat)) Identity.SetSeat(s, seat);
            Log.LogInfo($"Party Anywhere: {(IsLeader ? "conectado" : "lendo a vaga")}; {Hub.ActiveSeatCount} jogador(es) no lobby");
            if (IsLeader && !announced)
            {
                announced = true;
                Hub.Toast(Client.AllSeats, "MicroWorks Co-op conectado");
                PhoneLabels.Send(Hub, Client.AllSeats);
            }
        }

        void DrainEvents()
        {
            while (Hub.NextEvent(out PartyAnywhere.Event ev))
            {
                if (ev.Type == EventType.HubLost)
                {
                    Log.LogWarning("Party Anywhere: o Hub fechou; os controles ficam parados até ele voltar");
                    continue;
                }
                Hub.GetSeat(ev.Seat, out SeatInfo info);
                if (ev.Seat == Seat) UpdateSeatColor();
                if (ev.Type == EventType.SeatJoined || ev.Type == EventType.SeatRenamed) Identity.SetSeat(ev.Seat, info);
                Log.LogInfo($"P{ev.Seat + 1} {Describe(ev.Type)}: {info.Name} ({info.Device})");
                if (IsLeader && ev.Type == EventType.SeatJoined)
                    Hub.Toast(ev.Seat, $"MicroWorks Co-op: você é o P{ev.Seat + 1}");
            }
        }

        static void UpdateSeatColor()
        {
            if (Hub.GetSeat(Seat, out SeatInfo me))
                CursorOverlay.SeatColor = new Color32(me.R, me.G, me.B, 255);
        }

        /// <summary>Um número da linha de comando ("-screen-width 683"), ou `fallback`.</summary>
        public static int ArgInt(string name, int fallback) =>
            ReadIntArg(Environment.GetCommandLineArgs(), name, int.MinValue, int.MaxValue, fallback);

        static int? EnvSeat(string name) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out int v) && v >= 0 && v < Client.MaxSeats ? v : (int?)null;

        static int ReadIntArg(string[] args, string name, int min, int max, int fallback)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == name && int.TryParse(args[i + 1], out int v) && v >= min && v <= max)
                    return v;
            }
            return fallback;
        }

        static string Describe(Result r)
        {
            switch (r)
            {
                case Result.Version: return "o Hub fala outra versão do SDK; atualize o Party Anywhere ou o mod";
                case Result.Busy: return "outro jogo já está conectado ao Hub";
                default: return r.ToString();
            }
        }

        static string Describe(EventType t)
        {
            switch (t)
            {
                case EventType.SeatJoined: return "entrou";
                case EventType.SeatReconnecting: return "caiu (vaga guardada)";
                case EventType.SeatResumed: return "voltou";
                case EventType.SeatLeft: return "saiu";
                case EventType.SeatRenamed: return "trocou de nome";
                default: return t.ToString();
            }
        }
    }
}
