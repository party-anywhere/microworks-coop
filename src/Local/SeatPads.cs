using System;
using System.Collections.Generic;
using HarmonyLib;
using MicroWorksCoop.Input;
using MicroWorksCoop.Net;
using MicroWorksCoop.Ui;
using Mirror;
using PartyAnywhere;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Os desafios de responder (a conta, digitar a palavra, digitar a cor, trivia, repetir a sequência de cores,
    /// empilhar) usam o PDA do jogo: um aparelho na tela com a pergunta e um campo de digitar. Pela internet cada
    /// pessoa tem o dela; aqui o PDA é um só, de quem hospeda, e o campo é de teclado.
    ///
    /// Cada jogador que joga pelo controle ou pelo celular ganha um painel próprio, dentro da parte dele da tela (e
    /// na imagem do celular de quem joga por lá, porque fica no HUD dele): a pergunta, um teclado numérico, as
    /// opções ou o empilhar, tudo no direcional e no A/B. A resposta vai ao servidor pelo mesmo evento que o PDA
    /// mandaria (no contexto do jogador: conta como dele, no placar dele). Enquanto o painel está aberto o
    /// personagem fica parado, como com o PDA do jogo.
    /// </summary>
    class SeatPads : MonoBehaviour
    {
        enum Kind { Number, Choice, Sequence, Stack }

        class Session
        {
            public MicrogameBase Game;
            public PDAType Type;
            public string Event;
            public Kind Kind;
            public string Question = "";
            public string[] Options = new string[0]; // o que aparece
            public string[] Values = new string[0];  // o que vai no evento
            public Color[] Tints;                     // cor de cada opção (sequência)
            public int Length;                        // sequência: quantas cores; empilhar: quantas fileiras
            public float Ends;
            public bool Ready;
        }

        const float RepeatAfter = 0.35f, RepeatEvery = 0.12f;
        const float PanelW = 760f, PanelH = 560f;

        static readonly string[] numberKeys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "−", "0", "OK" };

        static readonly AccessTools.FieldRef<PDA, int> sequenceLength = AccessTools.FieldRefAccess<PDA, int>("sequenceLength");
        static readonly AccessTools.FieldRef<TypeTheWord, string> wordAnswer = AccessTools.FieldRefAccess<TypeTheWord, string>("correctAnswer");
        static readonly System.Reflection.FieldInfo wordList = AccessTools.Field(typeof(TypeTheWord), "wordlist");

        static Session session;
        static PDAType? pending;
        static float pendingAt;
        static readonly Pad[] pads = new Pad[Client.MaxSeats];

        /// <summary>O jogo abriu o PDA (para quem hospeda): o desafio é de responder.</summary>
        public static void Opened(PDAType type)
        {
            pending = type;
            pendingAt = Time.unscaledTime;
        }

        /// <summary>Fim da rodada: os painéis fecham.</summary>
        public static void EndAll()
        {
            session = null;
            pending = null;
            for (int s = 0; s < pads.Length; s++) Close(s);
        }

        void LateUpdate()
        {
            MicrogameCoordinator coord = MicrogameCoordinator.singleton;
            if (pending.HasValue && Time.unscaledTime - pendingAt > 0.05f) // a pergunta chega logo depois (RPC)
            {
                session = Build(pending.Value, coord);
                pending = null;
            }
            if (session != null && (coord == null || coord.Microgame != session.Game || Time.unscaledTime > session.Ends)) EndAll();
            HostPda.Update(hidden: session != null && HostNeedsPanel());

            for (int seat = 0; seat < Client.MaxSeats; seat++)
            {
                PlayerController pc = Player(seat);
                bool wanted = session != null && session.Ready && pc != null && pc.Alive && pc.State == PlayerController.PlayerState.Player
                    && (seat != Plugin.Seat || HostNeedsPanel());
                Pad pad = pads[seat];
                if (wanted && pad == null && !Answered(seat)) pads[seat] = pad = Open(seat);
                else if (!wanted && pad != null) { Close(seat); pad = null; }
                if (pad == null) continue;
                if (!pad.Place(pc)) { Close(seat); continue; }
                pad.Tick();
                if (pad.Done) Close(seat);
            }
        }

        // quem hospeda joga pelo controle ou pelo celular (não tem teclado): o painel. Empilhar já é no A do PDA.
        static bool HostNeedsPanel() => PadInput.SeatActive(Plugin.Seat) && session.Kind != Kind.Stack;

        static PlayerController Player(int seat)
        {
            if (seat != Plugin.Seat) return SeatContext.PlayerOf(seat);
            NetworkIdentity id = NetworkServer.active ? NetworkServer.localConnection?.identity : null;
            return id != null ? id.GetComponent<PlayerController>() : null;
        }

        static readonly HashSet<int> answered = new HashSet<int>();
        static Session answeredIn;

        static bool Answered(int seat) => answeredIn == session && answered.Contains(seat);

        static Pad Open(int seat)
        {
            PadInput.Capture(seat, true);
            return new Pad(seat, session);
        }

        static void Close(int seat)
        {
            Pad pad = pads[seat];
            pads[seat] = null;
            PadInput.Capture(seat, false);
            if (pad != null && pad.Root != null) Destroy(pad.Root.gameObject);
        }

        /// <summary>Manda a resposta como o jogador da vaga (quem hospeda: como sempre; um extra: no contexto dele).</summary>
        static void Submit(int seat, Session s, string value)
        {
            if (answeredIn != s) { answeredIn = s; answered.Clear(); }
            answered.Add(seat);
            if (s.Game == null) return;
            SeatContext.Token t = seat == Plugin.Seat ? default : SeatContext.Enter(seat);
            try { s.Game.SendClientEvent(s.Event, value); }
            catch (Exception e) { Plugin.Log.LogWarning($"P{seat + 1}: a resposta não foi ({e.GetType().Name}: {e.Message})"); }
            finally { SeatContext.Exit(t); }
            Plugin.Log.LogInfo($"P{seat + 1} respondeu {s.Event}: {value}");
        }

        // ---- o desafio, a partir do PDA e do microgame em curso ----

        static Session Build(PDAType type, MicrogameCoordinator coord)
        {
            PDA pda = PDA.singleton;
            if (coord == null || pda == null || coord.Microgame == null) return null;
            var s = new Session { Game = coord.Microgame, Type = type, Ends = Time.unscaledTime + coord.MicrogameLength + 0.5f, Ready = true };
            MicrogameBase game = coord.Microgame;
            switch (type)
            {
                case PDAType.Input when game is TypeTheWord word:
                    s.Kind = Kind.Choice;
                    s.Event = "MathAnswer";
                    s.Question = pda.inputQuestion.text;
                    s.Options = s.Values = WordChoices(wordAnswer(word));
                    break;
                case PDAType.Input when game is TypeTheColor:
                    s.Kind = Kind.Choice;
                    s.Event = "Answer";
                    s.Question = pda.inputQuestion.text;
                    s.Values = RepeatTheSequence.colors;
                    s.Options = Localized(RepeatTheSequence.colors);
                    s.Tints = GetOnTheXPlatform.RGBYP;
                    break;
                case PDAType.Input:
                    // a conta (TypeTheAnswer, a rodada livre "Ou não?") e qualquer outra pergunta de número
                    s.Kind = Kind.Number;
                    s.Event = "MathAnswer";
                    s.Question = pda.inputQuestion.text;
                    break;
                case PDAType.Trivia:
                    s.Kind = Kind.Choice;
                    s.Event = "TriviaAnswer";
                    s.Question = pda.triviaQuestion.text;
                    var options = new List<string>();
                    var values = new List<string>();
                    for (int i = 0; i < pda.triviaAnswerButtons.Length; i++)
                    {
                        if (!pda.triviaAnswerButtons[i].transform.parent.gameObject.activeSelf) continue;
                        options.Add(pda.triviaAnswerButtons[i].text);
                        values.Add(i.ToString());
                    }
                    s.Options = options.ToArray();
                    s.Values = values.ToArray();
                    break;
                case PDAType.Sequence:
                    s.Kind = Kind.Sequence;
                    s.Event = "SequenceAnswer";
                    s.Question = pda.sequenceQuestion.text;
                    s.Length = sequenceLength(pda);
                    s.Options = Localized(RepeatTheSequence.colors);
                    s.Values = Array.ConvertAll(RepeatTheSequence.colorIdents, c => c.ToString());
                    s.Tints = GetOnTheXPlatform.RGBYP;
                    break;
                case PDAType.StackEm:
                    s.Kind = Kind.Stack;
                    s.Event = "Stacked";
                    s.Question = "Empilhe os blocos";
                    foreach (PDAStackEmStrip strip in pda.stackEmController.strips) if (strip.gameObject.activeSelf) s.Length++;
                    break;
                default:
                    return null;
            }
            if (s.Kind == Kind.Choice && s.Options.Length == 0) return null;
            if ((s.Kind == Kind.Sequence || s.Kind == Kind.Stack) && s.Length <= 0) return null;
            return s;
        }

        static string[] Localized(string[] keys)
        {
            var out_ = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                out_[i] = I18n.Get(in key);
            }
            return out_;
        }

        // digitar a palavra num controle levaria o tempo todo do desafio: a palavra certa e três da lista do jogo
        static string[] WordChoices(string correct)
        {
            var words = new List<string> { correct.Trim() };
            if (wordList.GetValue(null) is string[] all && all.Length > 4)
            {
                for (int tries = 0; words.Count < 4 && tries < 50; tries++)
                {
                    string w = all[Random.Range(0, all.Length)].Trim();
                    if (w.Length > 0 && !words.Exists(x => string.Equals(x, w, StringComparison.OrdinalIgnoreCase))) words.Add(w);
                }
            }
            for (int i = words.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (words[i], words[j]) = (words[j], words[i]);
            }
            return words.ToArray();
        }

        /// <summary>
        /// O PDA do jogo é de quem hospeda e foi feito para a tela inteira: com mais de um jogador do sofá, ele fica só
        /// na parte de quem hospeda (encolhido, como o HUD da partida), e some quando quem hospeda responde pelo
        /// painel (controle ou celular). Assim cada um vê uma pergunta, a dele, e ninguém tem a tela coberta.
        /// </summary>
        static class HostPda
        {
            static RectTransform placed;
            static Vector2 anchorMin, anchorMax, pivot, position, size;
            static Vector3 scale;
            static CanvasGroup fade;

            public static void Update(bool hidden)
            {
                PDA pda = PDA.singleton;
                RectTransform top = pda != null ? TopOf(pda.PDAObject.transform) : null;
                if (top != placed) Restore();
                if (top == null) return;
                if (SplitScreen.LocalPlayers < 2) { Restore(); return; }
                if (placed == null) Save(top);

                // escondido (quem hospeda responde no painel ou joga pelo celular), ou na parte dele na TV
                bool hide = hidden || SplitScreen.HostOnPhone;
                if (fade == null)
                {
                    fade = pda.PDAObject.GetComponent<CanvasGroup>();
                    if (fade == null) fade = pda.PDAObject.AddComponent<CanvasGroup>();
                }
                float alpha = hide ? 0f : 1f;
                if (fade.alpha != alpha) fade.alpha = alpha;

                Rect part = SplitScreen.HostPart;
                var canvas = (RectTransform)top.parent;
                float fit = Mathf.Min(part.width, part.height);
                if (top.anchorMin != part.center) top.anchorMin = top.anchorMax = part.center;
                if (top.pivot != new Vector2(0.5f, 0.5f)) top.pivot = new Vector2(0.5f, 0.5f);
                if (top.anchoredPosition != Vector2.zero) top.anchoredPosition = Vector2.zero;
                if (top.sizeDelta != canvas.rect.size) top.sizeDelta = canvas.rect.size;
                if (top.localScale.x != fit) top.localScale = new Vector3(fit, fit, 1f);
            }

            // a peça de primeiro nível da tela (Canvas raiz) onde o PDA está; já dentro de uma parte: nada a fazer
            static RectTransform TopOf(Transform t)
            {
                Canvas[] canvases = t.GetComponentsInParent<Canvas>(true);
                if (canvases.Length == 0) return null;
                Transform root = canvases[canvases.Length - 1].transform;
                for (Transform x = t; x != null && x.parent != null; x = x.parent)
                {
                    if (x.name == "PartyAnywhere.Part") return null;
                    if (x.parent == root) return x as RectTransform;
                }
                return null;
            }

            static void Save(RectTransform top)
            {
                placed = top;
                anchorMin = top.anchorMin;
                anchorMax = top.anchorMax;
                pivot = top.pivot;
                position = top.anchoredPosition;
                size = top.sizeDelta;
                scale = top.localScale;
            }

            static void Restore()
            {
                if (fade != null) fade.alpha = 1f;
                if (placed != null)
                {
                    placed.anchorMin = anchorMin;
                    placed.anchorMax = anchorMax;
                    placed.pivot = pivot;
                    placed.anchoredPosition = position;
                    placed.sizeDelta = size;
                    placed.localScale = scale;
                }
                placed = null;
            }
        }

        // ---- o painel de uma vaga ----

        class Pad
        {
            public RectTransform Root;
            public bool Done;

            readonly int seat;
            readonly Session s;
            readonly Color seatColor;
            readonly List<Image> keys = new List<Image>();
            readonly List<Color> keyBase = new List<Color>();
            TextMeshProUGUI entry;
            int cols, sel;
            string typed = "";
            readonly List<int> sequence = new List<int>();
            float holdSince, nextRepeat;
            uint lastDir;
            Stacker stacker;
            Image[,] cells;

            public Pad(int seat, Session s)
            {
                this.seat = seat;
                this.s = s;
                seatColor = Identity.SeatColor(seat);
            }

            /// <summary>O painel fica no HUD do jogador (a parte dele na TV, ou a imagem do celular dele).</summary>
            public bool Place(PlayerController pc)
            {
                Transform parent = HudUi.PartOf(pc);
                if (parent == null) return false;
                if (Root == null) Build(parent);
                else if (Root.parent != parent) Root.SetParent(parent, false);
                return true;
            }

            void Build(Transform parent)
            {
                Root = HudUi.Rect($"PartyAnywhere.Answer.P{seat + 1}", parent);
                Root.sizeDelta = new Vector2(PanelW, PanelH);
                Root.SetAsLastSibling();
                HudUi.Box(Root, new Color(0.04f, 0.05f, 0.07f, 0.9f));
                RectTransform bar = HudUi.Rect("Bar", Root);
                bar.anchoredPosition = new Vector2(0f, PanelH / 2f - 4f);
                bar.sizeDelta = new Vector2(PanelW, 8f);
                HudUi.Box(bar, seatColor);
                HudUi.Label(Root, $"P{seat + 1}", new Vector2(-PanelW / 2f + 50f, PanelH / 2f - 40f), new Vector2(80f, 40f), 30f, seatColor);
                HudUi.Label(Root, s.Question, new Vector2(0f, PanelH / 2f - 105f), new Vector2(PanelW - 60f, 110f), 40f, Color.white);

                switch (s.Kind)
                {
                    case Kind.Number:
                        entry = HudUi.Label(Root, "", new Vector2(0f, 120f), new Vector2(PanelW - 200f, 60f), 46f, Color.white);
                        Grid(numberKeys, 3, new Vector2(170f, 64f), new Vector2(0f, -70f), null);
                        Hint("A: tecla   B: apagar   X: confirmar");
                        break;
                    case Kind.Choice:
                        Grid(s.Options, 1, new Vector2(PanelW - 120f, 58f), new Vector2(0f, -40f), s.Tints);
                        Hint("Direcional: escolher   A: confirmar");
                        break;
                    case Kind.Sequence:
                        entry = HudUi.Label(Root, "", new Vector2(0f, 80f), new Vector2(PanelW - 80f, 60f), 34f, Color.white);
                        Grid(s.Options, s.Options.Length, new Vector2(132f, 70f), new Vector2(0f, -60f), s.Tints);
                        Hint("A: põe a cor   B: apaga");
                        break;
                    case Kind.Stack:
                        stacker = new Stacker(s.Length);
                        BuildStack();
                        Hint("A: solta os blocos");
                        break;
                }
                Refresh();
            }

            void Hint(string text) =>
                HudUi.Label(Root, text, new Vector2(0f, -PanelH / 2f + 30f), new Vector2(PanelW - 40f, 36f), 24f, new Color(1f, 1f, 1f, 0.6f));

            void Grid(string[] labels, int columns, Vector2 key, Vector2 center, Color[] tints)
            {
                cols = columns;
                int rows = (labels.Length + columns - 1) / columns;
                const float gap = 12f;
                float w = columns * key.x + (columns - 1) * gap, h = rows * key.y + (rows - 1) * gap;
                for (int i = 0; i < labels.Length; i++)
                {
                    int c = i % columns, r = i / columns;
                    RectTransform k = HudUi.Rect($"Key{i}", Root);
                    k.anchoredPosition = center + new Vector2(-w / 2f + key.x / 2f + c * (key.x + gap), h / 2f - key.y / 2f - r * (key.y + gap));
                    k.sizeDelta = key;
                    Color baseColor = tints != null && i < tints.Length ? tints[i] * 0.55f + new Color(0f, 0f, 0f, 0.45f) : new Color(1f, 1f, 1f, 0.08f);
                    keys.Add(HudUi.Box(k, baseColor));
                    keyBase.Add(baseColor);
                    HudUi.Label(k, labels[i], Vector2.zero, key - new Vector2(10f, 4f), key.y * 0.5f, Color.white);
                }
            }

            void BuildStack()
            {
                const float cell = 44f, gap = 4f;
                int rows = stacker.Rows;
                cells = new Image[rows, Stacker.Columns];
                float w = Stacker.Columns * cell + (Stacker.Columns - 1) * gap, h = rows * cell + (rows - 1) * gap;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < Stacker.Columns; c++)
                    {
                        RectTransform k = HudUi.Rect($"Cell{r}.{c}", Root);
                        // a primeira fileira embaixo
                        k.anchoredPosition = new Vector2(-w / 2f + cell / 2f + c * (cell + gap), -h / 2f + cell / 2f + r * (cell + gap) - 40f);
                        k.sizeDelta = new Vector2(cell, cell);
                        cells[r, c] = HudUi.Box(k, Color.clear);
                    }
            }

            public void Tick()
            {
                uint pressed = PadInput.PanelPressed(seat), held = PadInput.PanelHeld(seat);
                if (s.Kind == Kind.Stack)
                {
                    stacker.Tick(Time.unscaledDeltaTime);
                    if ((pressed & (uint)Buttons.A) != 0) stacker.Drop();
                    if (stacker.Finished) { Submit(seat, s, stacker.Won.ToString()); Done = true; return; }
                    Refresh();
                    return;
                }
                Move(Direction(pressed, held));
                if ((pressed & (uint)Buttons.A) != 0) Press(sel);
                if ((pressed & (uint)Buttons.B) != 0) Erase();
                if ((pressed & (uint)Buttons.X) != 0 && s.Kind == Kind.Number) Press(numberKeys.Length - 1);
                if (!Done) Refresh();
            }

            // um toque move uma casa; segurando, repete
            uint Direction(uint pressed, uint held)
            {
                const uint up = (uint)Buttons.Up | PadInput.StickUp, down = (uint)Buttons.Down | PadInput.StickDown;
                const uint left = (uint)Buttons.Left | PadInput.StickLeft, right = (uint)Buttons.Right | PadInput.StickRight;
                uint dir = (held & up) != 0 ? up : (held & down) != 0 ? down : (held & left) != 0 ? left : (held & right) != 0 ? right : 0u;
                float now = Time.unscaledTime;
                if (dir == 0) { lastDir = 0; return 0; }
                if (dir != lastDir || (pressed & dir) != 0) { lastDir = dir; holdSince = now; nextRepeat = now + RepeatAfter; return dir; }
                if (now >= nextRepeat) { nextRepeat = now + RepeatEvery; return dir; }
                return 0;
            }

            void Move(uint dir)
            {
                if (dir == 0 || keys.Count == 0) return;
                int rows = (keys.Count + cols - 1) / cols;
                int r = sel / cols, c = sel % cols;
                if ((dir & ((uint)Buttons.Up | PadInput.StickUp)) != 0) r = (r + rows - 1) % rows;
                else if ((dir & ((uint)Buttons.Down | PadInput.StickDown)) != 0) r = (r + 1) % rows;
                else if ((dir & ((uint)Buttons.Left | PadInput.StickLeft)) != 0) c = (c + cols - 1) % cols;
                else c = (c + 1) % cols;
                sel = Mathf.Min(r * cols + c, keys.Count - 1);
            }

            void Press(int i)
            {
                switch (s.Kind)
                {
                    case Kind.Number:
                        string k = numberKeys[i];
                        if (k == "OK") { if (typed.Length > 0 && typed != "-") { Submit(seat, s, typed); Done = true; } }
                        else if (k == "−") typed = typed.StartsWith("-") ? typed.Substring(1) : "-" + typed;
                        else if (typed.Replace("-", "").Length < 6) typed += k;
                        break;
                    case Kind.Choice:
                        Submit(seat, s, s.Values[i]);
                        Done = true;
                        break;
                    case Kind.Sequence:
                        sequence.Add(i);
                        if (sequence.Count >= s.Length)
                        {
                            var answer = new System.Text.StringBuilder();
                            foreach (int color in sequence) answer.Append(s.Values[color]);
                            Submit(seat, s, answer.ToString());
                            Done = true;
                        }
                        break;
                }
            }

            void Erase()
            {
                if (s.Kind == Kind.Number && typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
                else if (s.Kind == Kind.Sequence && sequence.Count > 0) sequence.RemoveAt(sequence.Count - 1);
            }

            void Refresh()
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    Color want = i == sel ? seatColor : keyBase[i];
                    if (keys[i].color != want) keys[i].color = want;
                }
                if (entry != null)
                {
                    string text = s.Kind == Kind.Number ? (typed.Length > 0 ? typed : "_") : SequenceText();
                    if (entry.text != text) entry.text = text;
                }
                if (stacker != null)
                    for (int r = 0; r < stacker.Rows; r++)
                        for (int c = 0; c < Stacker.Columns; c++)
                        {
                            Color want = stacker.Placed(r, c) ? seatColor : stacker.Moving(r, c) ? Color.white : new Color(1f, 1f, 1f, 0.06f);
                            if (cells[r, c].color != want) cells[r, c].color = want;
                        }
            }

            string SequenceText()
            {
                if (sequence.Count == 0) return $"0 de {s.Length}";
                var text = new System.Text.StringBuilder();
                foreach (int color in sequence)
                {
                    if (text.Length > 0) text.Append(", ");
                    Color tint = s.Tints != null && color < s.Tints.Length ? s.Tints[color] : Color.white;
                    text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tint)).Append('>').Append(s.Options[color]).Append("</color>");
                }
                return text.ToString();
            }
        }

        /// <summary>
        /// O empilhar do PDA, para cada um: três blocos andam de um lado para o outro numa fileira de 9 casas; o A
        /// solta; só fica o que cai em cima de um bloco da fileira de baixo. Chegou ao alto com algum bloco: venceu.
        /// As mesmas regras e o mesmo ritmo do jogo (PDAStackEm).
        /// </summary>
        class Stacker
        {
            public const int Columns = 9;
            const float MoveEvery = 0.1f, WaitBetween = 0.25f;

            public readonly int Rows;
            public bool Finished, Won;

            readonly bool[,] placed;
            readonly bool[] alive = { true, true, true };
            int row, start, direction = 1;
            bool fromLeft = true;
            float sinceMove, wait;

            public Stacker(int rows)
            {
                Rows = rows;
                placed = new bool[rows, Columns];
                Begin();
            }

            void Begin()
            {
                start = fromLeft ? 0 : Columns - 3;
                direction = fromLeft ? 1 : -1;
                sinceMove = 0f;
            }

            public bool Placed(int r, int c) => placed[r, c];

            public bool Moving(int r, int c) => !Finished && wait <= 0f && r == row && c >= start && c < start + 3 && alive[c - start];

            public void Tick(float dt)
            {
                if (Finished) return;
                if (wait > 0f) { if ((wait -= dt) <= 0f) Begin(); return; }
                if ((sinceMove += dt) < MoveEvery) return;
                sinceMove = 0f;
                int left = start + Array.IndexOf(alive, true), right = start + Array.LastIndexOf(alive, true);
                if (direction == 1 && right >= Columns - 1 || direction == -1 && left <= 0) direction = -direction;
                start += direction;
            }

            public void Drop()
            {
                if (Finished || wait > 0f) return;
                int before = 0, after = 0;
                for (int i = 0; i < 3; i++)
                {
                    if (!alive[i]) continue;
                    before++;
                    int c = start + i;
                    bool keeps = c >= 0 && c < Columns && (row == 0 || placed[row - 1, c]);
                    alive[i] = keeps;
                    if (!keeps) continue;
                    placed[row, c] = true;
                    after++;
                }
                if (after == 0) { Finished = true; Won = false; return; }
                if (after < before) fromLeft = !fromLeft;
                if (++row >= Rows) { Finished = true; Won = true; return; }
                wait = WaitBetween;
            }
        }
    }

    // o jogo abre o PDA para o jogador local (quem hospeda), mesmo quando ele não pode usar (morto): o desafio é de
    // responder, para todos
    [HarmonyPatch(typeof(PDA), nameof(PDA.ShowPDA))]
    static class PdaOpened
    {
        static void Prefix(PDAType type)
        {
            if (Plugin.Active && !SeatContext.IsExtra) SeatPads.Opened(type);
        }
    }

    // fim da rodada (o jogo tira as dicas de todos): os painéis fecham
    [HarmonyPatch(typeof(MicrogameBase), nameof(MicrogameBase.FinishAnyExistingHints))]
    static class PadsCloseAtRoundEnd
    {
        static void Postfix() => SeatPads.EndAll();
    }
}
