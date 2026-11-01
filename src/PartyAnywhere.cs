// PartyAnywhere.cs: o SDK do Party Anywhere (ABI 1) em C#, sem DLL nativa. Um arquivo: copie para o projeto
// (Unity com BepInEx ou MelonLoader, .NET, Godot C#) e marque "permitir código unsafe".
//
// Espelha sdk/c/party_anywhere.h (memória compartilhada "Local\PartyAnywhere.v1"); o contrato completo está em
// `pa docs sdk`. Mudar este layout é mudança de ABI.
//
//   if (Client.Connect(out var hub) != Result.Ok) { /* Hub fechado: tente de novo a cada 1 ou 2 s */ }
//   // a cada frame, na thread do jogo:
//   hub.Poll();
//   while (hub.NextEvent(out var ev)) { /* SeatJoined, SeatLeft... */ }
//   for (int s = 0; s < Client.MaxSeats; s++) if (hub.GetPad(s, out var pad)) { /* pad.Lx, pad.Held... */ }
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PartyAnywhere
{
    public enum Result { Ok = 0, HubNotRunning = 1, Version = 2, Busy = 3, InvalidArg = 4 }
    public enum SeatState : byte { Empty = 0, Active = 1, Reconnecting = 2 }
    public enum DeviceKind : byte { Gamepad = 0, Phone = 1, Keyboard = 2 }
    public enum EventType { None = 0, SeatJoined = 1, SeatReconnecting = 2, SeatResumed = 3, SeatLeft = 4, SeatRenamed = 5, HubLost = 6 }
    public enum Layout { Gamepad = 0, Simple = 1, Driving = 2, Menu = 3 }

    [Flags]
    public enum Buttons : uint
    {
        None = 0,
        A = 1u << 0, B = 1u << 1, X = 1u << 2, Y = 1u << 3,
        LB = 1u << 4, RB = 1u << 5, Back = 1u << 6, Start = 1u << 7,
        LS = 1u << 8, RS = 1u << 9,
        Up = 1u << 10, Down = 1u << 11, Left = 1u << 12, Right = 1u << 13,
        Guide = 1u << 14,
        // só para SetLabel: gatilhos e analógicos não são botões do pad, mas têm nome no celular
        LabelLT = 1u << 16, LabelRT = 1u << 17, LabelStickL = 1u << 18, LabelStickR = 1u << 19,
    }

    public struct Event
    {
        public EventType Type;
        public int Seat; // 0..9 → P1..P10; -1 em HubLost
    }

    /// <summary>Sempre no formato de controle de Xbox. Eixos -1..1 (direita e cima positivos), gatilhos 0..1.</summary>
    public struct Pad
    {
        public Buttons Held, Pressed, Released;
        public float Lx, Ly, Rx, Ry, Lt, Rt;
    }

    public struct SeatInfo
    {
        public int Index;
        public SeatState State;
        public DeviceKind Device;
        public byte R, G, B;
        public string Name;
    }

    public sealed unsafe class Client : IDisposable
    {
        public const int AbiVersion = 1;
        public const int MaxSeats = 10;
        public const int AllSeats = -1;
        public const int ViewMaxWidth = 1280;
        public const int ViewMaxHeight = 720;
        /// <summary>Altura da imagem do celular (a largura segue o formato da tela dele).</summary>
        public const int ViewHeight = 360;

        const string ShmName = @"Local\PartyAnywhere.v1";
        const string ViewPrefix = @"Local\PartyAnywhere.v1.view.";
        const uint ShmMagic = 0x31484150u; // "PAH1"
        const uint ViewMagic = 0x57454956u; // "VIEW"
        const int ShmSizeV1 = 4976;         // a primeira versão da ABI 1: um Hub assim continua servindo
        const int ShmSize = 5056;           // com a câmera por deslocamento
        const int ViewHeaderSize = 64;
        const int ViewBytes = ViewHeaderSize + ViewMaxWidth * ViewMaxHeight * 4;
        const int Cmds = 64;
        const int EventCap = 64;
        const long HubLostMs = 1500;
        const int ReadTries = 1000;
        const uint CapLook = 1;

        enum Cmd : byte { Rumble = 1, Toast = 2, Accepting = 3, Label = 4, Layout = 5, Light = 6, Mode = 7 }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct ShmSeat
        {
            public uint Seq; // seqlock: ímpar = o Hub está escrevendo
            public byte State, Device, R, G, B;
            public fixed byte Pad_[3];
            public fixed byte Name[32];
            public uint Buttons;
            public float Lx, Ly, Rx, Ry, Lt, Rt;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct ShmCmd
        {
            public byte Type;
            public sbyte Seat;
            public ushort DurationMs;
            public float Low, High;
            public uint Value;
            public fixed byte Text[48];
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct ShmHeader
        {
            public uint Magic, Abi, Size, HubHeartbeat;
            public int GamePid;
            public int GameCaps; // o jogo escreve o que usa (CapLook)
            public fixed byte JoinUrl[128];
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct ViewHeader
        {
            public uint Magic, Version, Wanted, Width, Height, Stride, Seq, Reserved;
            public ulong TimestampUs;
            public uint AspectMilli; // o Hub escreve: largura / altura da tela do celular x 1000 (0 = não sabe)
            public fixed byte Pad_[20];
        }

        // deslocamentos dentro de pa_shm
        const int SeatsOffset = 152;
        const int CmdWriteOffset = SeatsOffset + MaxSeats * 72; // 872
        const int CmdReadOffset = CmdWriteOffset + 4;
        const int CmdsOffset = CmdReadOffset + 4;               // 880
        const int LookOffset = CmdsOffset + Cmds * 64;          // 4976

        static Client()
        {
            if (sizeof(ShmSeat) != 72 || sizeof(ShmCmd) != 64 || sizeof(ShmHeader) != SeatsOffset || sizeof(ViewHeader) != ViewHeaderSize || LookOffset != ShmSizeV1)
                throw new InvalidOperationException("PartyAnywhere: layout da memória compartilhada não bate com a ABI 1");
        }

        IntPtr mapping;
        byte* shm;
        readonly int pid;
        readonly bool hasLook;
        bool saidLook;
        bool lost;
        uint lastHeartbeat;
        long heartbeatAt;
        readonly ShmSeat[] seen = new ShmSeat[MaxSeats];
        readonly uint[] prevButtons = new uint[MaxSeats];
        readonly uint[] pressed = new uint[MaxSeats];
        readonly uint[] released = new uint[MaxSeats];
        readonly Event[] events = new Event[EventCap];
        uint evHead, evTail;
        byte sentAccepting;
        readonly IntPtr[] viewMap = new IntPtr[MaxSeats];
        readonly byte*[] view = new byte*[MaxSeats];
        readonly long[] viewNextTry = new long[MaxSeats];

        Client(IntPtr mapping, byte* shm, int pid)
        {
            this.mapping = mapping;
            this.shm = shm;
            this.pid = pid;
            hasLook = Header->Size >= ShmSize;
            lastHeartbeat = Header->HubHeartbeat;
            heartbeatAt = NowMs();
        }

        ShmHeader* Header => (ShmHeader*)shm;
        ShmSeat* Seat(int s) => (ShmSeat*)(shm + SeatsOffset + s * sizeof(ShmSeat));

        /// <summary>Conecta ao Hub aberto. Um jogo por vez: <see cref="Result.Busy"/> se outro já está.</summary>
        public static Result Connect(out Client client)
        {
            client = null;
            IntPtr h = Native.OpenFileMappingW(Native.FILE_MAP_ALL_ACCESS, false, ShmName);
            if (h == IntPtr.Zero) return Result.HubNotRunning;
            // a memória inteira: um Hub mais velho tem menos (sem a câmera por deslocamento)
            byte* p = (byte*)Native.MapViewOfFile(h, Native.FILE_MAP_ALL_ACCESS, 0, 0, UIntPtr.Zero);
            if (p == null) { Native.CloseHandle(h); return Result.HubNotRunning; }
            var hdr = (ShmHeader*)p;
            int me = Process.GetCurrentProcess().Id;
            Result r = Result.Ok;
            if (hdr->Magic != ShmMagic) r = Result.HubNotRunning;
            else if (hdr->Abi != AbiVersion || hdr->Size < ShmSizeV1) r = Result.Version;
            else
            {
                int prev = Interlocked.CompareExchange(ref hdr->GamePid, me, 0);
                if (prev != 0 && prev != me) r = Result.Busy;
            }
            if (r != Result.Ok) { Native.UnmapViewOfFile((IntPtr)p); Native.CloseHandle(h); return r; }
            client = new Client(h, p, me);
            return Result.Ok;
        }

        public bool IsConnected => shm != null && !lost;

        /// <summary>O endereço do QR, para o jogo desenhar o próprio lobby.</summary>
        public string JoinUrl => shm == null ? "" : Utf8(Header->JoinUrl, 128);

        /// <summary>Uma vez por frame, na thread do jogo.</summary>
        public void Poll()
        {
            if (shm == null) return;
            long now = NowMs();
            uint hb = Volatile.Read(ref Header->HubHeartbeat);
            if (hb != lastHeartbeat)
            {
                lastHeartbeat = hb;
                heartbeatAt = now;
                lost = false;
            }
            else if (!lost && now - heartbeatAt > HubLostMs)
            {
                lost = true;
                Push(EventType.HubLost, -1);
            }
            for (int s = 0; s < MaxSeats; s++)
            {
                ShmSeat old = seen[s];
                if (!ReadSeat(Seat(s), out ShmSeat cur)) cur = old; // sem leitura inteira, fica a anterior
                if (lost) cur.State = old.State; // sem Hub: a foto congela, os pads ficam neutros
                if (cur.State != old.State)
                {
                    if (old.State == (byte)SeatState.Empty) Push(EventType.SeatJoined, s);
                    else if (cur.State == (byte)SeatState.Reconnecting) Push(EventType.SeatReconnecting, s);
                    else if (old.State == (byte)SeatState.Reconnecting && cur.State == (byte)SeatState.Active) Push(EventType.SeatResumed, s);
                    else if (cur.State == (byte)SeatState.Empty) Push(EventType.SeatLeft, s);
                }
                else if (cur.State != (byte)SeatState.Empty && !SameName(&cur, &old))
                {
                    Push(EventType.SeatRenamed, s);
                }
                if (cur.State != (byte)SeatState.Active || lost)
                {
                    // nunca o último estado preso: vaga fora do ar devolve controle neutro
                    cur.Buttons = 0;
                    cur.Lx = cur.Ly = cur.Rx = cur.Ry = cur.Lt = cur.Rt = 0f;
                }
                pressed[s] = cur.Buttons & ~prevButtons[s];
                released[s] = ~cur.Buttons & prevButtons[s];
                prevButtons[s] = cur.Buttons;
                seen[s] = cur;
            }
        }

        public bool NextEvent(out Event ev)
        {
            if (evHead == evTail) { ev = default; return false; }
            ev = events[evHead % EventCap];
            evHead++;
            return true;
        }

        /// <summary>Aloca o nome: chame em eventos, não a cada frame.</summary>
        public bool GetSeat(int seat, out SeatInfo info)
        {
            info = default;
            if (seat < 0 || seat >= MaxSeats) return false;
            ShmSeat s = seen[seat];
            info.Index = seat;
            info.State = (SeatState)s.State;
            info.Device = (DeviceKind)s.Device;
            info.R = s.R; info.G = s.G; info.B = s.B;
            info.Name = Utf8(s.Name, 32);
            return s.State != (byte)SeatState.Empty;
        }

        public SeatState GetSeatState(int seat) => seat < 0 || seat >= MaxSeats ? SeatState.Empty : (SeatState)seen[seat].State;

        public int ActiveSeatCount
        {
            get
            {
                int n = 0;
                for (int s = 0; s < MaxSeats; s++) if (seen[s].State == (byte)SeatState.Active) n++;
                return n;
            }
        }

        /// <summary>false (e zerado) se a vaga não está ativa. Não aloca.</summary>
        public bool GetPad(int seat, out Pad pad)
        {
            pad = default;
            if (shm == null || seat < 0 || seat >= MaxSeats || lost) return false;
            ShmSeat s = seen[seat];
            if (s.State != (byte)SeatState.Active) return false;
            pad.Held = (Buttons)s.Buttons;
            pad.Pressed = (Buttons)pressed[seat];
            pad.Released = (Buttons)released[seat];
            pad.Lx = s.Lx; pad.Ly = s.Ly; pad.Rx = s.Rx; pad.Ry = s.Ry;
            pad.Lt = s.Lt; pad.Rt = s.Rt;
            return true;
        }

        /// <summary>
        /// Câmera por deslocamento: quanto o jogador arrastou o dedo na direita do celular desde a última chamada, em
        /// graus (direita e para cima positivos). Some ao ângulo da câmera, como o mouse, e continue lendo o analógico
        /// direito para quem joga com controle. Chamar já avisa o Hub, que troca o analógico direito do celular pela
        /// área de câmera. false: sem movimento (ou um Hub mais velho).
        /// </summary>
        public bool TakeLook(int seat, out float dxDeg, out float dyDeg)
        {
            dxDeg = dyDeg = 0f;
            if (shm == null || !hasLook || seat < 0 || seat >= MaxSeats) return false;
            if (!saidLook)
            {
                InterlockedOr(ref Header->GameCaps, (int)CapLook);
                saidLook = true;
            }
            int* look = (int*)(shm + LookOffset) + seat * 2;
            int x = Interlocked.Exchange(ref look[0], 0);
            int y = Interlocked.Exchange(ref look[1], 0);
            dxDeg = x / 1000f;
            dyDeg = y / 1000f;
            return x != 0 || y != 0;
        }

        // ---- do jogo para o jogador ----

        public void SetAcceptingPlayers(bool accepting)
        {
            byte want = accepting ? (byte)2 : (byte)1;
            if (sentAccepting == want) return;
            ShmCmd* c = BeginCmd(Cmd.Accepting, AllSeats);
            if (c == null) return;
            c->Value = accepting ? 1u : 0u;
            Commit();
            sentAccepting = want;
        }

        /// <summary>Gamepad ou Simple no celular da vaga, no lugar do "Padrão do jogo". Driving e Menu: reservados.</summary>
        public void SetLayout(int seat, Layout layout)
        {
            ShmCmd* c = BeginCmd(Cmd.Layout, seat);
            if (c == null) return;
            c->Value = (uint)layout;
            Commit();
        }

        /// <summary>Modo do controle do celular, na ordem dos modos da ficha (0 = o primeiro, "A pé").</summary>
        public void SetMode(int seat, int mode)
        {
            if (mode < 0) return;
            ShmCmd* c = BeginCmd(Cmd.Mode, seat);
            if (c == null) return;
            c->Value = (uint)mode;
            Commit();
        }

        /// <summary>Nome da ação no botão do celular ("Pular"); "" tira. Até 12 letras.</summary>
        public void SetLabel(int seat, Buttons button, string label)
        {
            ShmCmd* c = BeginCmd(Cmd.Label, seat);
            if (c == null) return;
            c->Value = (uint)button;
            CopyText(c->Text, 48, label);
            Commit();
        }

        public void Toast(int seat, string text)
        {
            ShmCmd* c = BeginCmd(Cmd.Toast, seat);
            if (c == null) return;
            CopyText(c->Text, 48, text);
            Commit();
        }

        public void Rumble(int seat, float low, float high, int durationMs)
        {
            ShmCmd* c = BeginCmd(Cmd.Rumble, seat);
            if (c == null) return;
            c->Low = Clamp01(low);
            c->High = Clamp01(high);
            c->DurationMs = (ushort)(durationMs < 0 ? 0 : durationMs > 5000 ? 5000 : durationMs);
            Commit();
        }

        public void SetLight(int seat, byte r, byte g, byte b)
        {
            ShmCmd* c = BeginCmd(Cmd.Light, seat);
            if (c == null) return;
            c->Value = ((uint)r << 16) | ((uint)g << 8) | b;
            Commit();
        }

        // ---- a tela do jogo no celular (o jogador liga no menu do celular) ----

        /// <summary>true quando o celular desta vaga mostra a tela do jogo: tire a parte dela da TV e entregue a visão.</summary>
        public bool ViewWanted(int seat)
        {
            byte* v = View(seat);
            if (v == null || Volatile.Read(ref ((ViewHeader*)v)->Wanted) == 0) return false;
            return seen[seat].State == (byte)SeatState.Active; // reconectando, a vaga fica na TV como sempre
        }

        /// <summary>O tamanho da imagem para o celular desta vaga: 360 de altura, na proporção da tela dele (640x360 se não souber).</summary>
        public void ViewSize(int seat, out int width, out int height)
        {
            byte* v = View(seat);
            uint a = v == null ? 0 : Volatile.Read(ref ((ViewHeader*)v)->AspectMilli);
            if (a < 1000 || a > 3000) a = 16000 / 9;
            int w = (int)((ViewHeight * a + 500) / 1000) & ~1;
            width = w > ViewMaxWidth ? ViewMaxWidth : w;
            height = ViewHeight;
        }

        /// <summary>
        /// Entrega a visão do jogador (BGRA, até 1280x720). Copia, então o buffer pode ser reusado. Stride negativo:
        /// imagem de baixo para cima (como a leitura da placa de vídeo), com <paramref name="bgra"/> na última linha.
        /// </summary>
        public bool SubmitView(int seat, byte* bgra, int width, int height, int strideBytes)
        {
            if (bgra == null || width <= 0 || height <= 0 || !ViewWanted(seat)) return false;
            var v = (ViewHeader*)view[seat];
            int w = width > ViewMaxWidth ? ViewMaxWidth : width;
            int h = height > ViewMaxHeight ? ViewMaxHeight : height;
            int row = w * 4;
            byte* dst = (byte*)v + ViewHeaderSize;
            Interlocked.Increment(ref *(int*)&v->Seq); // ímpar: escrevendo
            for (int y = 0; y < h; y++) Buffer.MemoryCopy(bgra + (long)y * strideBytes, dst + (long)y * row, row, row);
            v->Width = (uint)w;
            v->Height = (uint)h;
            v->Stride = (uint)row;
            long t = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
            v->TimestampUs = (ulong)(t / f) * 1000000UL + (ulong)(t % f) * 1000000UL / (ulong)f;
            Interlocked.Increment(ref *(int*)&v->Seq); // par: pronto
            return true;
        }

        public bool SubmitView(int seat, byte[] bgra, int width, int height, int strideBytes)
        {
            if (bgra == null || strideBytes < 0 || bgra.Length < (long)strideBytes * height) return false;
            fixed (byte* p = bgra) return SubmitView(seat, p, width, height, strideBytes);
        }

        public void Dispose()
        {
            if (shm == null) return;
            for (int s = 0; s < MaxSeats; s++)
            {
                if (view[s] != null) Native.UnmapViewOfFile((IntPtr)view[s]);
                if (viewMap[s] != IntPtr.Zero) Native.CloseHandle(viewMap[s]);
                view[s] = null;
                viewMap[s] = IntPtr.Zero;
            }
            Interlocked.CompareExchange(ref Header->GamePid, 0, pid);
            Native.UnmapViewOfFile((IntPtr)shm);
            Native.CloseHandle(mapping);
            shm = null;
            mapping = IntPtr.Zero;
        }

        // ---- interno ----

        byte* View(int seat)
        {
            if (shm == null || seat < 0 || seat >= MaxSeats) return null;
            if (view[seat] != null) return view[seat];
            long now = NowMs();
            if (now < viewNextTry[seat]) return null; // o Hub cria quando o celular liga a tela; procura a cada ~1 s
            viewNextTry[seat] = now + 1000;
            IntPtr h = Native.OpenFileMappingW(Native.FILE_MAP_ALL_ACCESS, false, ViewPrefix + seat);
            if (h == IntPtr.Zero) return null;
            byte* v = (byte*)Native.MapViewOfFile(h, Native.FILE_MAP_ALL_ACCESS, 0, 0, (UIntPtr)ViewBytes);
            if (v == null) { Native.CloseHandle(h); return null; }
            if (((ViewHeader*)v)->Magic != ViewMagic) { Native.UnmapViewOfFile((IntPtr)v); Native.CloseHandle(h); return null; }
            viewMap[seat] = h;
            view[seat] = v;
            return v;
        }

        // Não pega o Hub no meio da escrita. Com limite de tentativas: um Hub encerrado à força no meio de uma
        // escrita deixa a vaga "escrevendo" para sempre, e o jogo não pode travar esperando.
        static bool ReadSeat(ShmSeat* src, out ShmSeat copy)
        {
            for (int tries = 0; tries < ReadTries; tries++)
            {
                uint a = Volatile.Read(ref src->Seq);
                if ((a & 1u) != 0) { Thread.SpinWait(1); continue; }
                copy = *src;
                Thread.MemoryBarrier();
                if (Volatile.Read(ref src->Seq) == a) return true;
            }
            copy = default;
            return false;
        }

        ShmCmd* BeginCmd(Cmd type, int seat)
        {
            if (shm == null || seat < AllSeats || seat >= MaxSeats) return null;
            uint write = *(uint*)(shm + CmdWriteOffset);
            uint read = Volatile.Read(ref *(uint*)(shm + CmdReadOffset));
            if (write - read >= Cmds) return null; // Hub atrasado: descarta
            var c = (ShmCmd*)(shm + CmdsOffset + (write % Cmds) * sizeof(ShmCmd));
            *c = default;
            c->Type = (byte)type;
            c->Seat = (sbyte)seat;
            return c;
        }

        void Commit()
        {
            Thread.MemoryBarrier();
            Interlocked.Increment(ref *(int*)(shm + CmdWriteOffset));
        }

        void Push(EventType type, int seat)
        {
            if (evTail - evHead >= EventCap) return; // o jogo não leu: descarta o mais novo
            events[evTail % EventCap] = new Event { Type = type, Seat = seat };
            evTail++;
        }

        // Interlocked.Or só existe no .NET 5+; o Unity antigo (Mono, .NET 4.x) não tem
        static void InterlockedOr(ref int target, int bits)
        {
            int seen = Volatile.Read(ref target);
            while (true)
            {
                int prev = Interlocked.CompareExchange(ref target, seen | bits, seen);
                if (prev == seen) return;
                seen = prev;
            }
        }

        static bool SameName(ShmSeat* a, ShmSeat* b)
        {
            for (int i = 0; i < 32; i++) if (a->Name[i] != b->Name[i]) return false;
            return true;
        }

        static string Utf8(byte* p, int cap)
        {
            int n = 0;
            while (n < cap && p[n] != 0) n++;
            return n == 0 ? "" : Encoding.UTF8.GetString(p, n);
        }

        static void CopyText(byte* dst, int cap, string text)
        {
            if (string.IsNullOrEmpty(text)) { dst[0] = 0; return; }
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int n = bytes.Length >= cap ? cap - 1 : bytes.Length;
            while (n > 0 && n < bytes.Length && (bytes[n] & 0xC0) == 0x80) n--; // não corta um caractere no meio
            Marshal.Copy(bytes, 0, (IntPtr)dst, n);
            dst[n] = 0;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        static long NowMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

        static class Native
        {
            public const uint FILE_MAP_ALL_ACCESS = 0xF001F;

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

            [DllImport("kernel32", SetLastError = true)]
            public static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool UnmapViewOfFile(IntPtr address);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr handle);
        }
    }
}
