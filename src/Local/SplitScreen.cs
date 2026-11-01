using System.Collections.Generic;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// A tela dividida dentro da janela do jogo: a câmera de cada jogador deste PC desenha só na parte dela
    /// (a mesma grade do Hub: 1; 2 lado a lado; 2x2; 3x2; 3x3; 4x3), quem hospeda primeiro e os outros em ordem
    /// de vaga. O HUD de cada um fica dentro da parte dele; só a câmera de quem hospeda ouve (um som só).
    ///
    /// Em primeira pessoa o jogo esconde o corpo do jogador local, e aqui todos são locais. Cada corpo vai para
    /// uma camada só dele (das que o jogo não usa) e fica ligado; a câmera de cada um deixa de desenhar só a
    /// camada do próprio corpo. Não depende da ordem em que o HDRP prepara e desenha as câmeras.
    /// </summary>
    class SplitScreen : MonoBehaviour
    {
        const float RescanEvery = 1f; // câmeras e peças do corpo aparecem e somem (morreu, cosméticos): procura de novo
        const string FrameName = "PartyAnywhere.Part";

        static readonly CVar<bool> thirdPerson = CVars.GetBinding<bool>("prf_bThirdPerson");

        readonly List<PlayerController> order = new List<PlayerController>(Client.MaxSeats);
        readonly List<int> seats = new List<int>(Client.MaxSeats); // a vaga de cada um em order (-1: quem hospeda)
        readonly Dictionary<int, Camera[]> cameras = new Dictionary<int, Camera[]>();
        readonly Dictionary<int, AudioListener[]> listeners = new Dictionary<int, AudioListener[]>();
        readonly Dictionary<int, RectTransform[]> huds = new Dictionary<int, RectTransform[]>();      // HUD do jogador
        readonly Dictionary<int, RectTransform[]> matchHuds = new Dictionary<int, RectTransform[]>(); // HUD da partida
        readonly Dictionary<int, Renderer[]> bodies = new Dictionary<int, Renderer[]>();
        readonly Dictionary<int, int> originalMask = new Dictionary<int, int>(); // câmera → máscara do jogo
        int[] bodyLayers;  // uma camada livre por posição na grade
        int allBodyBits;
        float nextRescan;
        int lastCount;
        bool split;

        void LateUpdate()
        {
            HudCopies.Tick();
            order.Clear();
            seats.Clear();
            NetworkIdentity hostId = NetworkServer.active ? NetworkServer.localConnection?.identity : null;
            PlayerController host = hostId != null ? hostId.GetComponent<PlayerController>() : null;
            if (host != null) { order.Add(host); seats.Add(-1); }
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                PlayerController pc = SeatContext.PlayerOf(s);
                if (pc != null) { order.Add(pc); seats.Add(s); }
            }

            if (Time.unscaledTime >= nextRescan || order.Count != lastCount)
            {
                nextRescan = Time.unscaledTime + RescanEvery;
                cameras.Clear();
                listeners.Clear();
                huds.Clear();
                matchHuds.Clear();
                bodies.Clear();
            }
            SplitGraphics.Update(order.Count);
            bool rescan = cameras.Count == 0;
            bool wasSplit = split;
            split = order.Count > 1 && Layers();
            lastCount = order.Count;
            if (!split && wasSplit) RestoreMasks();
            if (split && rescan) OtherCamerasSeeBodies();
            if (order.Count == 0) return;

            for (int i = 0; i < order.Count; i++)
            {
                Rect part = split ? Grid.Part(order.Count, i) : new Rect(0f, 0f, 1f, 1f);
                Place(order[i], seats[i], i, part, listen: i == 0);
            }
        }

        // com a última linha incompleta (3, 5, 7, 8, 10 jogadores) sobram cantos que nenhuma câmera desenha: ali
        // ficaria a imagem de quadros antigos
        void OnGUI()
        {
            if (!split || UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;
            int n = order.Count;
            (int cols, int rows) = Grid.Shape(n);
            int empty = cols * rows - n;
            if (empty == 0) return;
            float w = Screen.width / (float)cols, h = Screen.height / (float)rows;
            float shift = empty * w / 2f, y = Screen.height - h; // a última linha, de cima para baixo na tela
            Color was = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, y, shift, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - shift, y, shift, h), Texture2D.whiteTexture);
            GUI.color = was;
        }

        void Place(PlayerController pc, int seat, int index, Rect part, bool listen)
        {
            int id = pc.GetInstanceID();
            if (!cameras.TryGetValue(id, out Camera[] cams)) cameras[id] = cams = pc.GetComponentsInChildren<Camera>(true);
            foreach (Camera cam in cams)
            {
                // câmeras que desenham numa imagem (retrato, espelho) ficam como estão
                if (cam == null || cam.targetTexture != null) continue;
                if (cam.rect != part) cam.rect = part;
                if (!split) continue;
                int camId = cam.GetInstanceID();
                if (!originalMask.ContainsKey(camId)) originalMask[camId] = cam.cullingMask;
                int mask = (originalMask[camId] | allBodyBits) & ~(1 << bodyLayers[index]); // os outros sim, o próprio corpo não
                if (cam.cullingMask != mask) cam.cullingMask = mask;
            }
            if (!listeners.TryGetValue(id, out AudioListener[] ears)) listeners[id] = ears = pc.GetComponentsInChildren<AudioListener>(true);
            foreach (AudioListener ear in ears)
                if (ear != null && ear.enabled != listen) ear.enabled = listen;

            if (split) ShowBody(pc, id, bodyLayers[index]);

            // os HUDs ficam por cima de tudo, como no jogo, mas dentro da parte do jogador: o conteúdo de cada tela
            // do HUD vai para uma moldura no lugar da parte dele.
            // O do jogador (mira, avisos de "usar") se estica até a parte, um pouco menor quanto menor ela for.
            if (!huds.TryGetValue(id, out RectTransform[] frames))
                huds[id] = frames = pc.playerHUD != null ? Frames(pc.playerHUD.GetComponentsInChildren<Canvas>(true)) : new RectTransform[0];
            float scale = Mathf.Clamp(Mathf.Sqrt(part.width * part.height) * 1.25f, 0.35f, 1f);
            Vector2 center = part.center;
            foreach (RectTransform frame in frames)
                if (frame != null) Fit(frame, center, Size(frame, part.size / scale), scale, Vector2.zero);
            // O da partida (rodada, placar, resultado) é montado a partir do meio da tela: encolhe inteiro, sem
            // mudar a proporção, no alto da parte. O meio da moldura continua sendo o meio (as animações do jogo
            // contam a partir dele); ela só sobe até encostar no alto
            if (!matchHuds.TryGetValue(id, out RectTransform[] match)) matchHuds[id] = match = Frames(HudCopies.CanvasesOf(seat));
            float fit = Mathf.Min(part.width, part.height);
            var top = new Vector2(part.center.x, part.yMax);
            foreach (RectTransform frame in match)
            {
                if (frame == null) continue;
                Vector2 full = Size(frame, Vector2.one);
                Fit(frame, top, full, fit, new Vector2(0f, -full.y * fit / 2f));
            }
        }

        // o tamanho, nas unidades da tela do HUD, de uma fração dela
        static Vector2 Size(RectTransform frame, Vector2 fraction) => Vector2.Scale(((RectTransform)frame.parent).rect.size, fraction);

        static void Fit(RectTransform frame, Vector2 anchor, Vector2 size, float scale, Vector2 offset)
        {
            if (frame.anchorMin != anchor || frame.anchorMax != anchor) frame.anchorMin = frame.anchorMax = anchor;
            if (frame.pivot != new Vector2(0.5f, 0.5f)) frame.pivot = new Vector2(0.5f, 0.5f);
            if (frame.anchoredPosition != offset) frame.anchoredPosition = offset;
            if (frame.sizeDelta != size) frame.sizeDelta = size;
            if (frame.localScale.x != scale) frame.localScale = new Vector3(scale, scale, 1f);
        }

        // o corpo do jogador na camada dele, ligado (o jogo o desliga em primeira pessoa por ser "local")
        void ShowBody(PlayerController pc, int id, int layer)
        {
            // morto, controlando outra coisa (sapo, kart) ou espectador: o jogo cuida do corpo
            if (!pc.Alive || pc.IsPossessingPawn || pc.IsSpectating) return;
            if (!bodies.TryGetValue(id, out Renderer[] parts))
            {
                var list = new List<Renderer>();
                foreach (Renderer r in pc.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.GetComponent<Collider>() == null && r.GetComponentInParent<Canvas>() == null) list.Add(r);
                bodies[id] = parts = list.ToArray();
            }
            foreach (Renderer r in parts)
                if (r != null && r.gameObject.layer != layer) r.gameObject.layer = layer;
            if (!pc.playerMesh.enabled) pc.ToggleModelVisibility(true);
        }

        // as câmeras que não são de jogador (a do sapo, as de introdução, a do retrato no HUD) continuam vendo os
        // corpos, que saíram da camada padrão
        void OtherCamerasSeeBodies()
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || cam.GetComponentInParent<PlayerController>() != null && cam.targetTexture == null) continue;
                if ((cam.cullingMask & 1) != 0 && (cam.cullingMask & allBodyBits) != allBodyBits) cam.cullingMask |= allBodyBits;
            }
        }

        void RestoreMasks()
        {
            foreach (Camera[] cams in cameras.Values)
                foreach (Camera cam in cams)
                    if (cam != null && originalMask.TryGetValue(cam.GetInstanceID(), out int mask)) cam.cullingMask = mask;
            originalMask.Clear();
            // de volta ao que o jogo espera de um jogador local sozinho
            bool third = thirdPerson != null && thirdPerson.Value;
            foreach (PlayerController pc in order)
                if (pc != null && pc.Alive && pc.playerMesh.enabled != third) pc.ToggleModelVisibility(third);
        }

        // as camadas livres (sem nome no jogo), uma por posição na grade; sem camadas suficientes, sem divisão
        bool Layers()
        {
            if (bodyLayers != null) return bodyLayers.Length >= order.Count;
            var free = new List<int>();
            for (int l = 31; l >= 8 && free.Count < Client.MaxSeats; l--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) free.Add(l);
            bodyLayers = free.ToArray();
            foreach (int l in bodyLayers) allBodyBits |= 1 << l;
            Plugin.Log.LogInfo($"Tela dividida: camadas dos corpos {string.Join(",", bodyLayers)}");
            return bodyLayers.Length >= order.Count;
        }

        // cada tela (Canvas) raiz do HUD ganha uma moldura que ocupa a parte do jogador; o que estava nela vai junto
        static RectTransform[] Frames(IEnumerable<Canvas> canvases)
        {
            var frames = new List<RectTransform>();
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                Transform existing = canvas.transform.Find(FrameName);
                if (existing != null) { frames.Add((RectTransform)existing); continue; }
                var go = new GameObject(FrameName, typeof(RectTransform));
                var frame = (RectTransform)go.transform;
                frame.SetParent(canvas.transform, false);
                // na mesma ordem (é a ordem de desenho)
                var children = new List<Transform>();
                foreach (Transform child in canvas.transform) if (child != frame) children.Add(child);
                foreach (Transform child in children) child.SetParent(frame, false);
                frames.Add(frame);
            }
            return frames.ToArray();
        }
    }

    /// <summary>A grade da tela dividida (igual à do Hub, pa-core::split), em coordenadas de câmera (0..1, y para cima).</summary>
    static class Grid
    {
        public static (int cols, int rows) Shape(int n)
        {
            if (n <= 1) return (1, 1);
            if (n == 2) return (2, 1);
            if (n <= 4) return (2, 2);
            if (n <= 6) return (3, 2);
            if (n <= 9) return (3, 3);
            return (4, 3);
        }

        public static Rect Part(int n, int i)
        {
            (int cols, int rows) = Shape(n);
            int row = i / cols, col = i % cols;
            int inRow = Mathf.Min(n - row * cols, cols);
            float w = 1f / cols, h = 1f / rows;
            float shift = (cols - inRow) * w / 2f; // linha incompleta: no meio
            // a grade conta as linhas de cima para baixo; a câmera conta y de baixo para cima
            return new Rect(col * w + shift, 1f - (row + 1) * h, w, h);
        }
    }
}
