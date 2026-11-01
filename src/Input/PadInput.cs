using MicroWorksCoop.Local;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Input
{
    /// <summary>
    /// O controle de cada vaga, traduzido para as ações do MicroWorks (que só tem teclado e mouse: o adaptador
    /// XInput do jogo vem vazio). Quem pergunta é sempre o jogador em contexto (SeatContext): o código de cada
    /// personagem lê o controle da vaga dele. Lido uma vez por frame para todas as vagas; nada aqui aloca.
    /// </summary>
    static class PadInput
    {
        // bits virtuais além dos botões do pad: gatilhos e direções do stick esquerdo como botões
        public const uint RT = 1u << 16, LT = 1u << 17;
        public const uint StickUp = 1u << 18, StickDown = 1u << 19, StickLeft = 1u << 20, StickRight = 1u << 21;
        const float TriggerOn = 0.5f, StickOn = 0.5f;

        // velocidade da mira com o stick no máximo, em graus por segundo (a sensibilidade do mouse do jogo não muda isso)
        const float LookYawDegPerSec = 220f;
        const float LookPitchDegPerSec = 150f;
        const float ZoomStep = 0.1f; // um "clique" da roda do mouse por toque no direcional
        const float CursorScreensPerSec = 1.1f; // cursor virtual nos menus: altura da tela por segundo, stick no máximo
        const float ScrollPerSec = 6f;

        static int frame = -1;
        static readonly Pad[] pads = new Pad[Client.MaxSeats];
        static readonly bool[] active = new bool[Client.MaxSeats];
        static readonly uint[] held = new uint[Client.MaxSeats];
        static readonly uint[] pressed = new uint[Client.MaxSeats];
        static readonly uint[] released = new uint[Client.MaxSeats];
        // um painel do mod (responder a conta, escolher a resposta) está com o controle da vaga: o jogo não o vê
        static readonly bool[] captured = new bool[Client.MaxSeats];
        static readonly uint[] panelHeld = new uint[Client.MaxSeats];
        static readonly uint[] panelPressed = new uint[Client.MaxSeats];
        // a câmera por arrastar o dedo no celular (graus desde o quadro anterior), somada ao analógico direito
        static readonly float[] lookX = new float[Client.MaxSeats];
        static readonly float[] lookY = new float[Client.MaxSeats];
        static bool hostRouting;
        static bool menu;
        static Vector2 cursor;

        static readonly CVar<float> mouseSensitivity = CVars.GetBinding<float>("prf_fMouseSensitivity");

        /// <summary>Menu aberto (o jogo soltou o mouse): o stick de quem hospeda move um cursor na tela.</summary>
        public static bool InMenu { get { Tick(); return hostRouting && menu; } }
        public static Vector2 CursorPosition { get { Tick(); return cursor; } }

        /// <summary>
        /// true quando o jogo deve ouvir o controle da vaga (e ignorar teclado, mouse e controles do PC): sempre para
        /// um jogador extra; para quem hospeda, quando a vaga dele tem alguém no Party Anywhere.
        /// </summary>
        public static bool Routing { get { Tick(); return SeatContext.IsExtra || hostRouting; } }

        /// <summary>Lê o Hub uma vez por frame, na primeira pergunta do jogo ou no Update do plugin.</summary>
        public static void Tick()
        {
            int now = Time.frameCount;
            if (now == frame) return;
            frame = now;

            Client hub = Plugin.Hub;
            if (hub != null) hub.Poll();
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                active[s] = hub != null && Plugin.Active && hub.GetPad(s, out pads[s]);
                if (!active[s]) pads[s] = default;
                Pad pad = pads[s];
                uint h = (uint)pad.Held;
                if (pad.Rt > TriggerOn) h |= RT;
                if (pad.Lt > TriggerOn) h |= LT;
                if (pad.Ly > StickOn) h |= StickUp;
                if (pad.Ly < -StickOn) h |= StickDown;
                if (pad.Lx < -StickOn) h |= StickLeft;
                if (pad.Lx > StickOn) h |= StickRight;
                uint down = h & ~panelHeld[s];
                panelPressed[s] = down;
                panelHeld[s] = h;
                // com um painel aberto, o personagem fica parado (como no PDA do jogo) e o painel lê os botões
                // o celular manda o quanto a pessoa arrastou o dedo na metade direita; pedir também avisa o Hub que o
                // jogo lê a câmera assim (o celular mostra a área de câmera)
                if (hub == null || !Plugin.Active || !hub.TakeLook(s, out lookX[s], out lookY[s])) lookX[s] = lookY[s] = 0f;
                if (captured[s]) { pads[s] = default; h = 0; lookX[s] = lookY[s] = 0f; }
                pressed[s] = h & ~held[s];
                released[s] = ~h & held[s];
                held[s] = h;
            }

            bool was = hostRouting;
            hostRouting = active[Plugin.Seat];
            if (hostRouting != was)
                Plugin.Log.LogInfo(hostRouting ? $"P{Plugin.Seat + 1}: obedece ao controle da vaga" : $"P{Plugin.Seat + 1}: sem jogador na vaga; teclado e mouse do PC de volta");

            bool wasMenu = menu;
            menu = Cursor.lockState != CursorLockMode.Locked;
            if (menu && (!wasMenu || cursor.x < 0f)) cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (menu && hostRouting)
            {
                Pad p = pads[Plugin.Seat];
                float step = Screen.height * CursorScreensPerSec * Time.unscaledDeltaTime;
                cursor.x = Mathf.Clamp(cursor.x + Curve(p.Lx) * step, 0f, Screen.width - 1);
                cursor.y = Mathf.Clamp(cursor.y + Curve(p.Ly) * step, 0f, Screen.height - 1);
            }
        }

        /// <summary>Roda do mouse nos menus: stick direito de quem hospeda para cima e para baixo.</summary>
        public static Vector2 Scroll => new Vector2(0f, Menu ? Curve(pads[Plugin.Seat].Ry) * ScrollPerSec * Time.unscaledDeltaTime : 0f);

        // os menus são de quem hospeda: no contexto de um jogador extra, nunca há menu
        static bool Menu => menu && !SeatContext.IsExtra;
        static int Seat => SeatContext.Seat;

        // ---- ações do InputManager (Luminosity) ----

        /// <summary>false: ação que o mod não conhece (o jogo responde neutro, sem teclado nem mouse).</summary>
        public static bool TryAxis(string name, out float value)
        {
            Pad pad = pads[Seat];
            switch (name)
            {
                case "Horizontal": value = pad.Lx; return true;
                case "Vertical": value = pad.Ly; return true;
                case "Mouse X": value = Look(pad.Rx, LookYawDegPerSec) + Degrees(lookX[Seat]); return true;
                case "Mouse Y": value = Look(pad.Ry, LookPitchDegPerSec) + Degrees(lookY[Seat]); return true;
                case "Mouse ScrollWheel":
                    value = Pressed((uint)Buttons.Up) ? ZoomStep : Pressed((uint)Buttons.Down) ? -ZoomStep : 0f;
                    return true;
                default:
                    bool known = ActionBits(name, out uint bits);
                    value = known && Held(bits) ? 1f : 0f;
                    return known;
            }
        }

        public static bool TryButton(string name, Edge edge, out bool value)
        {
            if (name == "Horizontal" || name == "Vertical")
            {
                float v = name == "Horizontal" ? pads[Seat].Lx : pads[Seat].Ly;
                value = edge == Edge.Held && (v > 0.1f || v < -0.1f);
                return true;
            }
            if (!ActionBits(name, out uint bits)) { value = false; return false; }
            value = Is(bits, edge);
            return true;
        }

        /// <summary>Os botões de cada ação. Mesmo padrão de jogos de tiro em terceira pessoa no Xbox.</summary>
        static bool ActionBits(string name, out uint bits)
        {
            switch (name)
            {
                case "Jump": bits = (uint)Buttons.A; return true;
                case "Crouch": bits = (uint)Buttons.B; return true;
                case "Use": bits = (uint)Buttons.X; return true;
                case "EmoteWheel": bits = (uint)Buttons.Y; return true;
                case "Sprint": bits = (uint)Buttons.LB; return true;
                case "Push": bits = (uint)Buttons.RB; return true;
                case "Fire": bits = RT; return true;
                case "AltFire": bits = LT; return true;
                case "FrontView": bits = (uint)Buttons.RS; return true;
                case "PlayerList": bits = (uint)Buttons.Back; return true;
                case "Dismiss": bits = (uint)Buttons.A; return true;
                // menus: A confirma; B volta só com um menu aberto (no jogo, B é agachar); Start pausa
                case "UI_Submit": bits = Menu ? 0u : (uint)Buttons.A; return true; // com o cursor virtual, A já clica
                case "UI_Cancel": bits = (uint)Buttons.Start | (NonExclusiveState.IsNonExclusive ? (uint)Buttons.B : 0u); return true;
                case "UI_Up": bits = (uint)Buttons.Up | StickUp; return true;
                case "UI_Down": bits = (uint)Buttons.Down | StickDown; return true;
                case "UI_Left": bits = (uint)Buttons.Left | StickLeft; return true;
                case "UI_Right": bits = (uint)Buttons.Right | StickRight; return true;
                // sem equivalente no sofá: o chat e o rádio são para quem joga pela internet
                case "Chat": case "VoiceChat": bits = 0; return true;
                default: bits = 0; return false;
            }
        }

        // ---- teclas e mouse lidos direto (menus, votação, microgames em Lua) ----

        public static bool Key(KeyCode key, Edge edge)
        {
            uint bits;
            switch (key)
            {
                case KeyCode.Mouse0: bits = Menu ? RT | (uint)Buttons.A : RT; break; // nos menus, A clica
                case KeyCode.Mouse1: bits = LT; break;
                case KeyCode.Mouse2: bits = (uint)Buttons.LS; break;
                case KeyCode.Escape: bits = (uint)Buttons.Start; break;
                case KeyCode.Space: case KeyCode.Return: case KeyCode.KeypadEnter: bits = (uint)Buttons.A; break;
                case KeyCode.LeftControl: case KeyCode.C: bits = (uint)Buttons.B; break;
                case KeyCode.E: case KeyCode.F: bits = (uint)Buttons.X; break;
                case KeyCode.Q: bits = (uint)Buttons.Y; break;
                case KeyCode.LeftShift: bits = (uint)Buttons.LB; break;
                case KeyCode.R: bits = (uint)Buttons.RB; break;
                case KeyCode.Tab: bits = (uint)Buttons.Back; break;
                case KeyCode.F3: bits = (uint)Buttons.Right; break; // votar sim
                case KeyCode.F4: bits = (uint)Buttons.Left; break;  // votar não
                case KeyCode.W: bits = StickUp; break;
                case KeyCode.S: bits = StickDown; break;
                case KeyCode.A: bits = StickLeft; break;
                case KeyCode.D: bits = StickRight; break;
                case KeyCode.UpArrow: bits = (uint)Buttons.Up; break;
                case KeyCode.DownArrow: bits = (uint)Buttons.Down; break;
                case KeyCode.LeftArrow: bits = (uint)Buttons.Left; break;
                case KeyCode.RightArrow: bits = (uint)Buttons.Right; break;
                default: return false;
            }
            return Is(bits, edge);
        }

        public static bool Mouse(int button, Edge edge) => button >= 0 && button <= 2 && Key(KeyCode.Mouse0 + button, edge);

        public static bool AnyKey(bool down) => down ? pressed[Seat] != 0 : held[Seat] != 0;

        /// <summary>Um painel do mod passa a ler o controle da vaga (o jogo deixa de ver) ou devolve.</summary>
        public static void Capture(int seat, bool on) => captured[seat] = on;

        /// <summary>Os botões da vaga para um painel do mod: os segurados e os que desceram neste quadro.</summary>
        public static uint PanelHeld(int seat) { Tick(); return panelHeld[seat]; }
        public static uint PanelPressed(int seat) { Tick(); return panelPressed[seat]; }

        /// <summary>A vaga tem alguém conectado agora (controle ou celular).</summary>
        public static bool SeatActive(int seat) { Tick(); return active[seat]; }

        // ---- interno ----

        static float Look(float stick, float degPerSec)
        {
            float sens = mouseSensitivity != null && mouseSensitivity.Value > 0.01f ? mouseSensitivity.Value : 1f;
            return Curve(stick) * degPerSec * Time.unscaledDeltaTime / sens;
        }

        // graus girados → o eixo do mouse do jogo (que multiplica pela sensibilidade dele)
        static float Degrees(float deg)
        {
            float sens = mouseSensitivity != null && mouseSensitivity.Value > 0.01f ? mouseSensitivity.Value : 1f;
            return deg / sens;
        }

        // curva quadrática: precisão perto do centro
        static float Curve(float v) => v * (v < 0 ? -v : v);

        static bool Is(uint bits, Edge edge)
        {
            switch (edge)
            {
                case Edge.Down: return Pressed(bits);
                case Edge.Up: return (released[Seat] & bits) != 0;
                default: return Held(bits);
            }
        }

        static bool Held(uint bits) => (held[Seat] & bits) != 0;
        static bool Pressed(uint bits) => (pressed[Seat] & bits) != 0;
    }

    enum Edge { Held, Down, Up }
}
