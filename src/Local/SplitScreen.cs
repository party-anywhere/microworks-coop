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
    /// Quem ligou a tela do jogo no celular sai da grade: a câmera e o HUD dele desenham na imagem do celular
    /// (PhoneViews), e a TV se divide entre os outros.
    ///
    /// Em primeira pessoa o jogo esconde o corpo do jogador local, e aqui todos são locais. Cada corpo vai para
    /// uma camada só dele (das que o jogo não usa) e fica ligado; a câmera de cada um deixa de desenhar só a
    /// camada do próprio corpo. O que é de primeira pessoa (os braços, a arma presa à câmera) vai para outra
    /// camada dele, que só a câmera dele desenha; e o modelo da arma na mão, que o jogo esconde do jogador
    /// local, aparece para os outros (a lata, a bola). Não depende da ordem em que o HDRP prepara e desenha as
    /// câmeras.
    /// </summary>
    class SplitScreen : MonoBehaviour
    {
        const float RescanEvery = 1f; // câmeras e peças do corpo aparecem e somem (morreu, cosméticos): procura de novo
        const string FrameName = "PartyAnywhere.Part";

        static readonly CVar<bool> thirdPerson = CVars.GetBinding<bool>("prf_bThirdPerson");

        readonly List<PlayerController> order = new List<PlayerController>(Client.MaxSeats);
        readonly List<int> seats = new List<int>(Client.MaxSeats); // a vaga de cada um em order (-1: quem hospeda)
        readonly List<int> tv = new List<int>(Client.MaxSeats);    // quem de order está na TV (o resto, no celular)
        readonly bool[] onPhone = new bool[Client.MaxSeats];
        readonly Dictionary<int, (RenderMode mode, Camera camera, float distance)> canvasMode = new Dictionary<int, (RenderMode, Camera, float)>();
        readonly Dictionary<int, Camera[]> cameras = new Dictionary<int, Camera[]>();
        readonly Dictionary<int, AudioListener[]> listeners = new Dictionary<int, AudioListener[]>();
        readonly Dictionary<int, RectTransform[]> huds = new Dictionary<int, RectTransform[]>();      // HUD do jogador
        readonly Dictionary<int, RectTransform[]> matchHuds = new Dictionary<int, RectTransform[]>(); // HUD da partida
        readonly Dictionary<int, (Renderer renderer, bool view)[]> bodies = new Dictionary<int, (Renderer, bool)[]>();
        readonly Dictionary<int, int> originalMask = new Dictionary<int, int>(); // câmera → máscara do jogo
        readonly Dictionary<Renderer, int> originalLayer = new Dictionary<Renderer, int>(); // peça → camada do jogo
        int[] bodyLayers;  // uma camada livre por posição na grade: o corpo (os outros veem)
        int[] viewLayers;  // e outra: a primeira pessoa (só ele vê); vazio se o jogo não deixar camadas livres
        int allBodyBits, allViewBits;
        float nextRescan;
        int lastCount;
        bool split;

        /// <summary>Jogadores do sofá na partida (quem hospeda e os extras).</summary>
        public static int LocalPlayers { get; private set; }
        /// <summary>A parte da TV de quem hospeda (coordenadas de câmera, 0..1); a tela toda sem divisão.</summary>
        public static Rect HostPart { get; private set; } = new Rect(0f, 0f, 1f, 1f);
        /// <summary>Quem hospeda está jogando pela tela do celular (fora da TV).</summary>
        public static bool HostOnPhone { get; private set; }

        void Awake()
        {
            // uma arma foi para a mão de alguém (a lata, a bola): as peças dela entram nas camadas já
            GlobalEvent<WeaponAttachedEvent>.AddListener(_ => bodies.Clear());
        }

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
            tv.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                int hubSeat = HubSeat(i);
                bool phone = PhoneViews.Wants(hubSeat);
                if (!phone) tv.Add(i);
                if (phone != onPhone[hubSeat])
                {
                    onPhone[hubSeat] = phone;
                    if (!phone) PhoneViews.Release(hubSeat);
                    huds.Clear(); // as telas do HUD mudam de modo (por cima da TV / dentro da imagem do celular)
                    matchHuds.Clear();
                }
            }
            // quem saiu da partida estando no celular
            for (int s = 0; s < Client.MaxSeats; s++)
                if (onPhone[s] && !seats.Contains(s) && !(s == Plugin.Seat && seats.Contains(-1))) { onPhone[s] = false; PhoneViews.Release(s); }

            SplitGraphics.Update(order.Count);
            bool rescan = cameras.Count == 0;
            bool wasSplit = split;
            split = order.Count > 1 && Layers();
            lastCount = order.Count;
            LocalPlayers = order.Count;
            if (!split && wasSplit) RestoreMasks();
            if (split && rescan) OtherCamerasSeeBodies();
            if (order.Count == 0) return;

            var full = new Rect(0f, 0f, 1f, 1f);
            for (int i = 0; i < order.Count; i++)
            {
                int place = tv.IndexOf(i);
                RenderTexture phone = place < 0 ? PhoneViews.TargetFor(HubSeat(i)) : null;
                Rect part = phone != null || tv.Count == 1 ? full : Grid.Part(tv.Count, place);
                Place(order[i], seats[i], i, part, listen: i == 0, phone);
                if (seats[i] < 0) { HostPart = part; HostOnPhone = phone != null; }
            }
        }

        int HubSeat(int i) => seats[i] < 0 ? Plugin.Seat : seats[i];

        // todos jogando pelo celular: nenhuma câmera desenha na TV
        void OnGUI()
        {
            if (order.Count == 0 || UnityEngine.Event.current.type != UnityEngine.EventType.Repaint) return;
            if (tv.Count > 0) return;
            Color was = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Screen.height / 24 };
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), "Todos jogando pelo celular", style);
            GUI.color = was;
        }

        void Place(PlayerController pc, int seat, int index, Rect part, bool listen, RenderTexture phone)
        {
            int id = pc.GetInstanceID();
            // dirigindo o kart ou sendo o sapo, a câmera é a do veículo (fora do personagem): entra na divisão também
            Component pawn = pc.IsPossessingPawn ? pc.PossessedPawn : null;
            int camKey = pawn != null ? pawn.GetInstanceID() : id;
            if (!cameras.TryGetValue(camKey, out Camera[] cams))
            {
                Camera[] own = pc.GetComponentsInChildren<Camera>(true);
                Camera[] extra = pawn != null ? pawn.GetComponentsInChildren<Camera>(true) : new Camera[0];
                cams = new Camera[own.Length + extra.Length];
                own.CopyTo(cams, 0);
                extra.CopyTo(cams, own.Length);
                cameras[camKey] = cams;
            }
            foreach (Camera cam in cams)
            {
                // câmeras que desenham numa imagem do jogo (retrato, espelho) ficam como estão
                if (cam == null || cam.targetTexture != null && !PhoneViews.IsOurs(cam.targetTexture)) continue;
                if (cam.targetTexture != phone)
                {
                    cam.targetTexture = phone;
                    if (phone == null) cam.ResetAspect(); // de volta à TV: o formato da parte dele
                }
                // no celular, o formato da imagem dele: sem isso a câmera guardava o da TV e a imagem saía esticada
                if (phone != null)
                {
                    float aspect = (float)phone.width / phone.height;
                    if (Mathf.Abs(cam.aspect - aspect) > 0.001f) cam.aspect = aspect;
                }
                if (cam.rect != part) cam.rect = part;
                if (!split) continue;
                int camId = cam.GetInstanceID();
                if (!originalMask.ContainsKey(camId)) originalMask[camId] = cam.cullingMask;
                // os corpos dos outros sim, o próprio não; a primeira pessoa só a própria
                int mask = (originalMask[camId] | allBodyBits) & ~(1 << bodyLayers[index]) & ~allViewBits;
                if (viewLayers.Length > index) mask |= 1 << viewLayers[index];
                if (cam.cullingMask != mask) cam.cullingMask = mask;
            }
            if (!listeners.TryGetValue(id, out AudioListener[] ears)) listeners[id] = ears = pc.GetComponentsInChildren<AudioListener>(true);
            foreach (AudioListener ear in ears)
                if (ear != null && ear.enabled != listen) ear.enabled = listen;

            if (split) ShowBody(pc, id, index);

            // os HUDs ficam por cima de tudo, como no jogo, mas dentro da parte do jogador: o conteúdo de cada tela
            // do HUD vai para uma moldura no lugar da parte dele.
            // O do jogador (mira, avisos de "usar") se estica até a parte, um pouco menor quanto menor ela for.
            if (!huds.TryGetValue(id, out RectTransform[] frames))
            {
                huds[id] = frames = Frames(PlayerCanvases(pc));
                Draw(frames, phone != null ? pc.playerCamera : null);
            }
            float scale = Mathf.Clamp(Mathf.Sqrt(part.width * part.height) * 1.25f, 0.35f, 1f);
            Vector2 center = part.center;
            foreach (RectTransform frame in frames)
                if (frame != null) Fit(frame, center, Vector2.Scale(Design(frame, out float unit), part.size / scale), scale * unit, Vector2.zero);
            // O da partida (rodada, placar, resultado) é montado a partir do meio da tela: encolhe inteiro, sem
            // mudar a proporção, no alto da parte. O meio da moldura continua sendo o meio (as animações do jogo
            // contam a partir dele); ela só sobe até encostar no alto
            if (!matchHuds.TryGetValue(id, out RectTransform[] match))
            {
                matchHuds[id] = match = Frames(HudCopies.CanvasesOf(seat));
                Draw(match, phone != null ? pc.playerCamera : null);
            }
            float fit = Mathf.Min(part.width, part.height);
            var top = new Vector2(part.center.x, part.yMax);
            foreach (RectTransform frame in match)
            {
                if (frame == null) continue;
                Vector2 full = Design(frame, out float unit);
                Fit(frame, top, full, fit * unit, new Vector2(0f, -full.y * fit * unit / 2f));
            }
        }

        // no celular, o HUD vai dentro da imagem que a câmera do jogador desenha; na TV, volta a ser como o jogo fez
        void Draw(RectTransform[] frames, Camera phoneCamera)
        {
            foreach (RectTransform frame in frames)
            {
                if (frame == null || !frame.parent.TryGetComponent(out Canvas canvas)) continue;
                int canvasId = canvas.GetInstanceID();
                if (phoneCamera != null)
                {
                    if (!canvasMode.ContainsKey(canvasId)) canvasMode[canvasId] = (canvas.renderMode, canvas.worldCamera, canvas.planeDistance);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = phoneCamera;
                    canvas.planeDistance = phoneCamera.nearClipPlane + 0.01f;
                }
                else if (canvasMode.TryGetValue(canvasId, out var was))
                {
                    canvasMode.Remove(canvasId);
                    canvas.renderMode = was.mode;
                    canvas.worldCamera = was.camera;
                    canvas.planeDistance = was.distance;
                }
            }
        }

        // o tamanho para o qual o HUD foi feito: o da janela, nas unidades da tela do HUD. Na imagem do celular a tela
        // do HUD é menor (o ajuste de escala do jogo mede a janela, não a imagem): unit é quanto encolher para caber
        static Vector2 Design(RectTransform frame, out float unit)
        {
            var parent = (RectTransform)frame.parent;
            float factor = parent.TryGetComponent(out Canvas canvas) && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            var design = new Vector2(Screen.width, Screen.height) / factor;
            unit = parent.rect.width > 0f ? parent.rect.width / design.x : 1f;
            return design;
        }

        static void Fit(RectTransform frame, Vector2 anchor, Vector2 size, float scale, Vector2 offset)
        {
            if (frame.anchorMin != anchor || frame.anchorMax != anchor) frame.anchorMin = frame.anchorMax = anchor;
            if (frame.pivot != new Vector2(0.5f, 0.5f)) frame.pivot = new Vector2(0.5f, 0.5f);
            if (frame.anchoredPosition != offset) frame.anchoredPosition = offset;
            if (frame.sizeDelta != size) frame.sizeDelta = size;
            if (frame.localScale.x != scale) frame.localScale = new Vector3(scale, scale, 1f);
        }

        // o corpo do jogador na camada dele, ligado (o jogo o desliga em primeira pessoa por ser "local"); a primeira
        // pessoa na camada que só ele vê; a arma na mão ligada para os outros
        void ShowBody(PlayerController pc, int id, int index)
        {
            // morto, controlando outra coisa (sapo, kart) ou espectador: o jogo cuida do corpo
            if (!pc.Alive || pc.IsPossessingPawn || pc.IsSpectating) return;
            if (!bodies.TryGetValue(id, out var parts))
            {
                Transform view = pc.lookRoot != null ? pc.lookRoot.transform : null;
                var list = new List<(Renderer, bool)>();
                foreach (Renderer r in pc.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.GetComponent<Collider>() == null && r.GetComponentInParent<Canvas>() == null)
                        list.Add((r, view != null && r.transform.IsChildOf(view)));
                bodies[id] = parts = list.ToArray();
            }
            int body = bodyLayers[index], own = viewLayers.Length > index ? viewLayers[index] : -1;
            foreach ((Renderer r, bool isView) in parts)
            {
                if (r == null) continue;
                // sem camada de primeira pessoa (o jogo não deixou camadas livres), ela fica como o jogo fez
                int layer = isView ? own : body;
                if (layer < 0 || r.gameObject.layer == layer) continue;
                if (!originalLayer.ContainsKey(r)) originalLayer[r] = r.gameObject.layer;
                r.gameObject.layer = layer;
            }
            if (!pc.playerMesh.enabled) pc.ToggleModelVisibility(true);
            ShowHeldWeapon(pc);
            // o fogo do jetpack (pulo duplo): o jogo apaga para o jogador local em primeira pessoa; os outros veem
            PlayerJetpack jet = pc.colorCustomizer != null ? pc.colorCustomizer.jetpack : null;
            if (jet != null && jet.allRenderers != null)
                foreach (Renderer r in jet.allRenderers) if (r != null && !r.enabled) r.enabled = true;
        }

        // o modelo da arma na mão (não o da câmera): o jogo esconde do jogador local, os outros precisam ver
        static void ShowHeldWeapon(PlayerController pc)
        {
            WeaponBase weapon = pc.Weapon;
            if (weapon == null || weapon.Attachments == null) return;
            foreach (WeaponModelAttachment a in weapon.Attachments)
            {
                if (a == null || a.Attachment == WeaponAttachment.FirstPersonLookRoot) continue;
                if (a.Mesh != null && !a.Mesh.enabled) a.Mesh.enabled = true;
                if (a.meshChildren == null) continue;
                foreach (Renderer r in a.meshChildren) if (r != null && !r.enabled) r.enabled = true;
            }
        }

        // as câmeras que não são de jogador (a do sapo, as de introdução, a do retrato no HUD) continuam vendo os
        // corpos, que saíram da camada padrão
        void OtherCamerasSeeBodies()
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || cam.GetComponentInParent<PlayerController>() != null && cam.targetTexture == null) continue;
                if ((cam.cullingMask & 1) != 0 && (cam.cullingMask & allBodyBits) != allBodyBits) cam.cullingMask |= allBodyBits;
                if ((cam.cullingMask & allViewBits) != 0) cam.cullingMask &= ~allViewBits; // primeira pessoa: só a do dono
            }
        }

        void RestoreMasks()
        {
            foreach (Camera[] cams in cameras.Values)
                foreach (Camera cam in cams)
                    if (cam != null && originalMask.TryGetValue(cam.GetInstanceID(), out int mask)) cam.cullingMask = mask;
            originalMask.Clear();
            // cada peça de volta à camada em que o jogo a pôs (sem isso, os braços de quem hospeda sumiriam)
            foreach (KeyValuePair<Renderer, int> kv in originalLayer)
                if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
            originalLayer.Clear();
            // de volta ao que o jogo espera de um jogador local sozinho
            bool third = thirdPerson != null && thirdPerson.Value;
            foreach (PlayerController pc in order)
                if (pc != null && pc.Alive && pc.playerMesh.enabled != third) pc.ToggleModelVisibility(third);
        }

        // as camadas livres (sem nome no jogo): uma por posição na grade para os corpos e, se sobrar, outra para a
        // primeira pessoa de cada um; sem camadas para os corpos, sem divisão
        bool Layers()
        {
            if (bodyLayers != null) return bodyLayers.Length >= order.Count;
            var free = new List<int>();
            for (int l = 31; l >= 8 && free.Count < 2 * Client.MaxSeats; l--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) free.Add(l);
            int bodiesCount = Mathf.Min(free.Count, Client.MaxSeats);
            bodyLayers = free.GetRange(0, bodiesCount).ToArray();
            viewLayers = free.GetRange(bodiesCount, free.Count - bodiesCount).ToArray();
            foreach (int l in bodyLayers) allBodyBits |= 1 << l;
            foreach (int l in viewLayers) allViewBits |= 1 << l;
            Plugin.Log.LogInfo($"Tela dividida: camadas dos corpos {string.Join(",", bodyLayers)}; da primeira pessoa {string.Join(",", viewLayers)}");
            return bodyLayers.Length >= order.Count;
        }

        // cada tela (Canvas) raiz do HUD ganha uma moldura que ocupa a parte do jogador; o que estava nela vai junto
        // as telas do HUD do jogador e as de espectador ("Assistindo a", que aparece quando ele morre): as de espectador
        // são da tela inteira, como o HUD, e iam para o meio da TV em vez da parte de quem morreu
        static IEnumerable<Canvas> PlayerCanvases(PlayerController pc)
        {
            if (pc.playerHUD != null)
                foreach (Canvas c in pc.playerHUD.GetComponentsInChildren<Canvas>(true)) yield return c;
            if (pc.spectatorMaster == null) yield break;
            foreach (Canvas c in pc.spectatorMaster.GetComponentsInChildren<Canvas>(true))
                if (c.renderMode == RenderMode.ScreenSpaceOverlay && (pc.playerHUD == null || !c.transform.IsChildOf(pc.playerHUD.transform)))
                    yield return c;
            // a caixa "Assistindo a" pode estar numa tela fora da câmera de espectador
            if (pc.spectatorMaster.spectatorParent != null)
            {
                Canvas root = null;
                for (Transform t = pc.spectatorMaster.spectatorParent.transform; t != null; t = t.parent)
                    if (t.TryGetComponent(out Canvas c)) root = c;
                if (root != null && root.renderMode == RenderMode.ScreenSpaceOverlay
                    && !root.transform.IsChildOf(pc.spectatorMaster.transform) && (pc.playerHUD == null || !root.transform.IsChildOf(pc.playerHUD.transform)))
                    yield return root;
            }
        }

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

    /// <summary>
    /// A grade da tela dividida, em coordenadas de câmera (0..1, y para cima). As linhas são as da grade do Hub
    /// (1; 2 lado a lado; 2x2; 3x2; 3x3; 4x3), mas os jogadores se dividem por igual entre elas e cada linha ocupa
    /// a largura toda: nunca sobra canto vazio (10 = 4+3+3; 7 = 3+2+2; 3 = 2+1).
    /// </summary>
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

        /// <summary>Quantos jogadores ficam na linha (de cima para baixo): as primeiras levam a sobra.</summary>
        public static int InRow(int n, int row)
        {
            int rows = Shape(n).rows;
            return n / rows + (row < n % rows ? 1 : 0);
        }

        public static Rect Part(int n, int i)
        {
            int rows = Shape(n).rows;
            float h = 1f / rows;
            for (int row = 0, first = 0; row < rows; first += InRow(n, row), row++)
            {
                int inRow = InRow(n, row);
                if (i >= first + inRow) continue;
                float w = 1f / inRow;
                // a grade conta as linhas de cima para baixo; a câmera conta y de baixo para cima
                return new Rect((i - first) * w, 1f - (row + 1) * h, w, h);
            }
            return new Rect(0f, 0f, 1f, 1f);
        }
    }
}
