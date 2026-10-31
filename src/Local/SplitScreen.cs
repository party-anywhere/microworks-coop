using System.Collections.Generic;
using Mirror;
using PartyAnywhere;
using UnityEngine;
using UnityEngine.Rendering;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// A tela dividida dentro da janela do jogo: a câmera de cada jogador deste PC desenha só na parte dela
    /// (a mesma grade do Hub: 1; 2 lado a lado; 2x2; 3x2; 3x3; 4x3), quem hospeda primeiro e os outros em ordem
    /// de vaga. O HUD de cada um fica dentro da parte dele; só a câmera de quem hospeda ouve (um som só).
    ///
    /// Em primeira pessoa o jogo esconde o corpo do jogador local. Com vários jogadores locais, cada câmera
    /// esconde só o corpo do dono dela: antes de cada câmera desenhar, os corpos certos aparecem.
    /// </summary>
    class SplitScreen : MonoBehaviour
    {
        const float RescanEvery = 1f; // câmeras aparecem e somem (morreu, virou espectador): procura de novo

        static readonly CVar<bool> thirdPerson = CVars.GetBinding<bool>("prf_bThirdPerson");

        readonly List<PlayerController> order = new List<PlayerController>(Client.MaxSeats);
        readonly Dictionary<int, Camera[]> cameras = new Dictionary<int, Camera[]>();
        readonly Dictionary<int, AudioListener[]> listeners = new Dictionary<int, AudioListener[]>();
        readonly Dictionary<int, RectTransform[]> huds = new Dictionary<int, RectTransform[]>();
        const string FrameName = "PartyAnywhere.Part";
        readonly Dictionary<Camera, PlayerController> owner = new Dictionary<Camera, PlayerController>();
        float nextRescan;
        int lastCount;
        bool split;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
        }

        void LateUpdate()
        {
            order.Clear();
            NetworkIdentity hostId = NetworkServer.active ? NetworkServer.localConnection?.identity : null;
            PlayerController host = hostId != null ? hostId.GetComponent<PlayerController>() : null;
            if (host != null) order.Add(host);
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                PlayerController pc = SeatContext.PlayerOf(s);
                if (pc != null) order.Add(pc);
            }

            if (Time.unscaledTime >= nextRescan || order.Count != lastCount)
            {
                nextRescan = Time.unscaledTime + RescanEvery;
                cameras.Clear();
                listeners.Clear();
                huds.Clear();
                owner.Clear();
            }
            split = order.Count > 1;
            lastCount = order.Count;
            if (order.Count == 0) return;

            for (int i = 0; i < order.Count; i++)
            {
                Rect part = split ? Grid.Part(order.Count, i) : new Rect(0f, 0f, 1f, 1f);
                Place(order[i], part, listen: i == 0);
            }
        }

        void Place(PlayerController pc, Rect part, bool listen)
        {
            int id = pc.GetInstanceID();
            if (!cameras.TryGetValue(id, out Camera[] cams))
            {
                cameras[id] = cams = pc.GetComponentsInChildren<Camera>(true);
                foreach (Camera c in cams) if (c != null) owner[c] = pc;
            }
            foreach (Camera cam in cams)
            {
                // câmeras que desenham numa imagem (retrato, espelho) ficam como estão
                if (cam == null || cam.targetTexture != null) continue;
                if (cam.rect != part) cam.rect = part;
            }
            if (!listeners.TryGetValue(id, out AudioListener[] ears)) listeners[id] = ears = pc.GetComponentsInChildren<AudioListener>(true);
            foreach (AudioListener ear in ears)
                if (ear != null && ear.enabled != listen) ear.enabled = listen;

            // o HUD do jogador (mira, avisos de "usar") fica por cima de tudo, como no jogo, mas dentro da parte
            // dele: o conteúdo de cada tela do HUD vai para uma moldura do tamanho e no lugar da parte do jogador
            if (pc.playerHUD == null) return;
            if (!huds.TryGetValue(id, out RectTransform[] frames)) huds[id] = frames = Frames(pc.playerHUD);
            foreach (RectTransform frame in frames)
            {
                if (frame == null) continue;
                if (frame.anchorMin != part.min || frame.anchorMax != part.max)
                {
                    frame.anchorMin = part.min;
                    frame.anchorMax = part.max;
                }
            }
        }

        // cada tela (Canvas) raiz do HUD ganha uma moldura que ocupa a parte do jogador; o que estava nela vai junto
        static RectTransform[] Frames(GameObject hud)
        {
            var frames = new List<RectTransform>();
            foreach (Canvas canvas in hud.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                Transform existing = canvas.transform.Find(FrameName);
                if (existing != null) { frames.Add((RectTransform)existing); continue; }
                var go = new GameObject(FrameName, typeof(RectTransform));
                var frame = (RectTransform)go.transform;
                frame.SetParent(canvas.transform, false);
                frame.offsetMin = Vector2.zero;
                frame.offsetMax = Vector2.zero;
                // na mesma ordem (é a ordem de desenho)
                var children = new List<Transform>();
                foreach (Transform child in canvas.transform) if (child != frame) children.Add(child);
                foreach (Transform child in children) child.SetParent(frame, false);
                frames.Add(frame);
            }
            return frames.ToArray();
        }

        // ---- cada câmera vê os corpos dos outros jogadores locais, menos o do dono dela ----

        void BeforeCamera(ScriptableRenderContext _, Camera cam)
        {
            if (!split || !owner.TryGetValue(cam, out PlayerController camOwner)) return;
            bool third = thirdPerson != null && thirdPerson.Value;
            foreach (PlayerController pc in order)
                if (CanShow(pc)) pc.ToggleModelVisibility(pc != camOwner || third);
        }

        void AfterCamera(ScriptableRenderContext _, Camera cam)
        {
            if (!split || !owner.ContainsKey(cam)) return;
            // de volta ao que o jogo espera de um jogador local (o código dele confere isso)
            bool third = thirdPerson != null && thirdPerson.Value;
            foreach (PlayerController pc in order)
                if (CanShow(pc)) pc.ToggleModelVisibility(third);
        }

        // morto, controlando outra coisa (sapo, kart) ou espectador: o jogo cuida do corpo
        static bool CanShow(PlayerController pc) => pc != null && pc.Alive && !pc.IsPossessingPawn && !pc.IsSpectating;
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
