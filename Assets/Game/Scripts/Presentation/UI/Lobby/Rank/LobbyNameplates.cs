using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The names over the astronauts' heads, and the underline that marks the host's.
    ///
    /// <para>
    /// Each name is white over a navy copy of itself, three pixels off — see
    /// <see cref="UIBuilder.ShadowedLabel"/> for why. At the authored framing the heads sit BELOW
    /// the horizon, so a plain white name lands on bright sand and disappears, while a navy one
    /// would vanish the moment somebody stood against the sky instead.
    /// </para>
    ///
    /// <para>
    /// One row per slot, grown as the roster grows and never shrunk: a nameplate for a player who
    /// has left is switched off, not destroyed, in case they rejoin.
    /// </para>
    /// </summary>
    internal sealed class LobbyNameplates
    {
        private const int NameSize = 40;
        private const float RowWidth = 600f;
        private const float RowHeight = 60f;

        private static readonly Vector2 ShadowOffset = new(3f, -3f);

        // An underline instead of the word "host": there is no room for a caption per figure, and
        // the rank only ever needs to mark one of them. Navy, not white: it sits under the name, so
        // it is over whatever the name is over, and sand is the likelier of the two.
        private const float UnderlineLift = 8f;
        private const float UnderlineThickness = 3f;
        private const float UnderlineAlpha = 0.85f;

        private readonly LobbyOverlayLayer layer;

        /// <summary>How far above the head bone the name floats, in metres.</summary>
        private readonly float lift;

        private readonly List<RectTransform> rows = new();
        private readonly List<TextMeshProUGUI> labels = new();
        private readonly List<TextMeshProUGUI> shadows = new();
        private readonly List<RectTransform> underlines = new();

        public LobbyNameplates(LobbyOverlayLayer layer, float lift)
        {
            this.layer = layer;
            this.lift = lift;
        }

        /// <summary>Writes a slot's name, building its plate on first use.</summary>
        public void Set(int slot, string name, bool isHost)
        {
            Ensure(slot);

            labels[slot].text = name;
            shadows[slot].text = name;
            underlines[slot].gameObject.SetActive(isHost);
        }

        /// <summary>
        /// Keeps every plate over its head. A plate whose slot is empty, or whose head is behind
        /// the camera, is hidden.
        /// </summary>
        public void Position(Camera camera, IReadOnlyList<Transform> heads, IReadOnlyList<bool> occupied)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;

                bool visible = i < occupied.Count && occupied[i]
                               && i < heads.Count && heads[i] != null
                               && layer.Place(camera, rows[i], heads[i].position + Vector3.up * lift);

                rows[i].gameObject.SetActive(visible);
            }
        }

        private void Ensure(int slot)
        {
            SlotLists.Grow(rows, slot);
            SlotLists.Grow(labels, slot);
            SlotLists.Grow(shadows, slot);
            SlotLists.Grow(underlines, slot);

            if (rows[slot] != null) return;

            RectTransform row = layer.Centred($"Name{slot}", RowWidth, RowHeight);

            labels[slot] = UIBuilder.ShadowedLabel(row, string.Empty, NameSize, MenuEntry.Title, MenuEntry.Idle,
                                                   ShadowOffset, TextAlignmentOptions.Center,
                                                   out TextMeshProUGUI shadow);
            shadows[slot] = shadow;

            RectTransform rule = UIBuilder.Rect("HostRule", row);
            rule.anchorMin = new Vector2(0.5f, 0f);
            rule.anchorMax = new Vector2(0.5f, 0f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.anchoredPosition = new Vector2(0f, UnderlineLift);
            rule.sizeDelta = new Vector2(NameSize * 3f, UnderlineThickness);
            UIBuilder.Solid(rule, new Color(MenuEntry.Idle.r, MenuEntry.Idle.g, MenuEntry.Idle.b, UnderlineAlpha));
            rule.gameObject.SetActive(false);

            underlines[slot] = rule;
            rows[slot] = row;
        }
    }
}
