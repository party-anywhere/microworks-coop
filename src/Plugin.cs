using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mirror;
using MicroWorksCoop.Input;
using MicroWorksCoop.Local;
using MicroWorksCoop.Net;
using PartyAnywhere;
using UnityEngine;
using EventType = PartyAnywhere.EventType;

namespace MicroWorksCoop
{
    /// <summary>
    /// Um MicroWorks só, com todo mundo dentro dele: quem hospeda cria a partida e conecta ao Hub; cada pessoa do
    /// lobby do Party Anywhere entra nela como um jogador de verdade (LocalPlayers), com a sua parte da tela
    /// (SplitScreen) ou a imagem no celular (PhoneViews). Ver docs/PLANO-UM-JOGO.md.
    /// </summary>
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.joaocaioo.microworks-coop";
        public const string Name = "MicroWorks Co-op Local";
        public const string Version = "0.1.0";

        const float ConnectRetrySeconds = 2f;

        internal static ManualLogSource Log;

        /// <summary>A vaga de quem hospeda (P1): o personagem do próprio jogo. As outras vagas são os jogadores extras.</summary>
        public const int Seat = 0;

        /// <summary>
        /// false quando quem hospeda entra na partida de outra pessoa pela internet: ali os jogadores do sofá não
        /// entram, então o mod não mexe em nada e o jogo segue como sempre (teclado, mouse e controles do PC).
        /// </summary>
        public static bool Active => !NetworkClient.isConnected || NetworkServer.active;

        public static Client Hub { get; private set; }

        public static bool DevSkipTutorial { get; private set; }

        float nextConnectTry;
        bool announced;

        void Awake()
        {
            Log = Logger;
            string[] args = Environment.GetCommandLineArgs();
            Log.LogInfo($"{Name} {Version}: um jogo só, com os jogadores do Party Anywhere dentro dele");
            // o jogo segue lendo os controles do Hub sem ser a janela em foco (o Hub ou o navegador por cima)
            Application.runInBackground = true;
            var harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Local.SeatTriggers.Install(harmony);
            Local.SeatClients.InstallEvents(harmony);
            gameObject.AddComponent<CursorOverlay>();
            // os outros jogadores do lobby entram nesta partida, com a tela dividida aqui
            gameObject.AddComponent<LocalPlayers>();
            gameObject.AddComponent<SplitScreen>();
            gameObject.AddComponent<PhoneViews>();
            gameObject.AddComponent<SeatPads>();
            gameObject.AddComponent<SeatMarkers>();
            if (Config.Bind("Desenvolvimento", "DiagnosticoTelaDividida", false, "Anota no log onde cada jogador aparece na câmera de quem hospeda.").Value)
                gameObject.AddComponent<SplitDiag>();
            // para desenvolver: pular o tutorial e criar a partida sozinho (nada é gravado nas configurações do
            // jogo); pela linha de comando ou pelo BepInEx\config\com.joaocaioo.microworks-coop.cfg
            bool devSkip = Config.Bind("Desenvolvimento", "PularTutorial", false, "A cópia que hospeda pula o tutorial.").Value;
            bool devHost = Config.Bind("Desenvolvimento", "CriarPartidaSozinho", false, "A cópia que hospeda cria a partida assim que o menu carrega.").Value;
            SplitGraphics.Preset = Config.Bind("Video", "QualidadeComVariosJogadores", "VeryLow",
                "Preset de vídeo do jogo enquanto a tela está dividida (VeryLow, Low, Medium, High, Ultra; vazio = as suas configurações). Com um jogador só, voltam as suas. Não muda as suas configurações.").Value;
            if (Config.Bind("Desenvolvimento", "MedirDesempenho", false, "Cada cópia anota fps e memória no log a cada 10 s.").Value)
                gameObject.AddComponent<PerfLog>();
            DevSkipTutorial = devSkip || Array.IndexOf(args, "-pa-skip-tutorial") >= 0;
            if (devHost || Array.IndexOf(args, "-pa-dev-host") >= 0) gameObject.AddComponent<DevHost>();
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
            Result r = Client.Connect(out Client c);
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
            Log.LogInfo($"Party Anywhere: conectado; {Hub.ActiveSeatCount} jogador(es) no lobby");
            if (!announced)
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
                if (ev.Type == EventType.SeatJoined)
                    Hub.Toast(ev.Seat, $"MicroWorks Co-op: você é o P{ev.Seat + 1}");
            }
        }

        static void UpdateSeatColor()
        {
            if (Hub.GetSeat(Seat, out SeatInfo me))
                CursorOverlay.SeatColor = new Color32(me.R, me.G, me.B, 255);
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
