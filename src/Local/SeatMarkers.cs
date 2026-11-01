using System.Collections.Generic;
using MicroWorksCoop.Net;
using MicroWorksCoop.Ui;
using Mirror;
using PartyAnywhere;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Quem é quem, com mais de um jogador do sofá na partida:
    /// - no canto de cada parte da tela, a etiqueta do dono dela ("P2 · Ana", na cor da vaga);
    /// - sobre a cabeça de cada jogador do sofá, "P3" na cor dele, na tela de quem está vendo.
    /// Tudo no HUD de cada jogador (dentro da parte dele, ou na imagem do celular). Uma placa no mundo do jogo não
    /// serviria: o HDRP prepara todas as câmeras antes de desenhar, então ela não vira para cada uma.
    /// </summary>
    class SeatMarkers : MonoBehaviour
    {
        const float MaxDistance = 60f;
        const float HeadRoom = 0.35f; // acima do topo do corpo, em metros

        class View
        {
            public RectTransform Root;
            public RectTransform Corner;
            public TextMeshProUGUI CornerText;
            public Image CornerBox;
            public readonly RectTransform[] Tags = new RectTransform[Client.MaxSeats];
            public readonly TextMeshProUGUI[] TagTexts = new TextMeshProUGUI[Client.MaxSeats];
        }

        readonly List<(int seat, PlayerController pc)> players = new List<(int, PlayerController)>(Client.MaxSeats);
        readonly View[] views = new View[Client.MaxSeats];
        readonly string[] shownName = new string[Client.MaxSeats];

        void LateUpdate()
        {
            players.Clear();
            NetworkIdentity hostId = NetworkServer.active && Plugin.Active ? NetworkServer.localConnection?.identity : null;
            PlayerController host = hostId != null ? hostId.GetComponent<PlayerController>() : null;
            if (host != null) players.Add((Plugin.Seat, host));
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                PlayerController pc = SeatContext.PlayerOf(s);
                if (pc != null) players.Add((s, pc));
            }

            bool show = players.Count > 1;
            for (int s = 0; s < Client.MaxSeats; s++)
            {
                PlayerController viewer = Find(s);
                if (!show || viewer == null) { Drop(s); continue; }
                Transform part = HudUi.PartOf(viewer);
                if (part == null) { Drop(s); continue; }
                View v = views[s];
                if (v == null || v.Root == null) views[s] = v = Make(s, part);
                else if (v.Root.parent != part) v.Root.SetParent(part, false);
                Corner(v, s);
                Tags(v, s, viewer);
            }
        }

        PlayerController Find(int seat)
        {
            foreach ((int s, PlayerController pc) in players) if (s == seat) return pc;
            return null;
        }

        View Make(int seat, Transform part)
        {
            var v = new View { Root = HudUi.Rect($"PartyAnywhere.Markers.P{seat + 1}", part) };
            // a moldura toda: os nomes ficam por baixo do resto do HUD
            v.Root.anchorMin = Vector2.zero;
            v.Root.anchorMax = Vector2.one;
            v.Root.sizeDelta = Vector2.zero;
            v.Root.SetAsFirstSibling();

            v.Corner = HudUi.Rect("Corner", v.Root);
            v.Corner.anchorMin = v.Corner.anchorMax = v.Corner.pivot = new Vector2(0f, 1f);
            v.Corner.anchoredPosition = new Vector2(24f, -24f);
            v.Corner.sizeDelta = new Vector2(300f, 52f);
            v.CornerBox = HudUi.Box(v.Corner, Color.gray);
            v.CornerText = HudUi.Label(v.Corner, "", Vector2.zero, new Vector2(284f, 48f), 32f, Color.white);
            v.CornerText.alignment = TextAlignmentOptions.MidlineLeft;
            return v;
        }

        void Corner(View v, int seat)
        {
            Color color = Identity.SeatColor(seat);
            if (v.CornerBox.color != color)
            {
                v.CornerBox.color = color;
                v.CornerText.color = HudUi.InkOn(color);
            }
            string name = Identity.SeatName(seat);
            if (shownName[seat] != name || v.CornerText.text.Length == 0)
            {
                shownName[seat] = name;
                v.CornerText.text = $"<b>P{seat + 1}</b>  {name}";
            }
        }

        // "P3" sobre a cabeça de cada outro jogador do sofá, onde a câmera de quem vê o mostra
        void Tags(View v, int viewerSeat, PlayerController viewer)
        {
            Camera cam = viewer.playerCamera;
            Vector2 size = v.Root.rect.size;
            foreach ((int seat, PlayerController pc) in players)
            {
                RectTransform tag = v.Tags[seat];
                bool visible = seat != viewerSeat && cam != null && cam.isActiveAndEnabled && pc != null && pc.Alive && !pc.IsSpectating;
                Vector3 vp = default;
                if (visible)
                {
                    vp = cam.WorldToViewportPoint(Head(pc));
                    visible = vp.z > 0.3f && vp.z < MaxDistance && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                }
                if (!visible)
                {
                    if (tag != null && tag.gameObject.activeSelf) tag.gameObject.SetActive(false);
                    continue;
                }
                if (tag == null) v.Tags[seat] = tag = MakeTag(v, seat);
                if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
                tag.anchoredPosition = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
                float scale = Mathf.Clamp(8f / vp.z, 0.45f, 1.2f); // longe, menor (mas sempre legível)
                if (Mathf.Abs(tag.localScale.x - scale) > 0.01f) tag.localScale = new Vector3(scale, scale, 1f);
                Color color = Identity.SeatColor(seat);
                if (v.TagTexts[seat].color != color) v.TagTexts[seat].color = color;
            }
        }

        static RectTransform MakeTag(View v, int seat)
        {
            RectTransform tag = HudUi.Rect($"P{seat + 1}", v.Root);
            tag.sizeDelta = new Vector2(120f, 56f);
            TextMeshProUGUI text = HudUi.Label(tag, $"<b>P{seat + 1}</b>", Vector2.zero, new Vector2(120f, 56f), 44f, Identity.SeatColor(seat));
            // contorno escuro: lê em qualquer cenário
            text.outlineWidth = 0.25f;
            text.outlineColor = new Color32(0, 0, 0, 220);
            v.TagTexts[seat] = text;
            return tag;
        }

        static Vector3 Head(PlayerController pc)
        {
            Renderer body = pc.playerMesh;
            if (body != null && body.bounds.size.y > 0.5f) return new Vector3(body.bounds.center.x, body.bounds.max.y + HeadRoom, body.bounds.center.z);
            return pc.transform.position + Vector3.up * 2.1f;
        }

        void Drop(int seat)
        {
            View v = views[seat];
            views[seat] = null;
            if (v != null && v.Root != null) Destroy(v.Root.gameObject);
        }
    }
}
