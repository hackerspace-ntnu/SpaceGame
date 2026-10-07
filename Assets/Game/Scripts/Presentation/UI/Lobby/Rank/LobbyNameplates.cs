using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SpaceGame.Gameplay;

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

        /// <summary>Which team each slot is on, and which slot is the host's — what the thinning needs.</summary>
        private readonly List<int> teamOf = new();

        private readonly List<bool> hosts = new();

        /// <summary>The size last written per slot, so an unchanged plate costs no mesh rebuild.</summary>
        private readonly List<float> appliedSize = new();

        private int localSlot = -1;
        private int localTeam = -1;

        public LobbyNameplates(LobbyOverlayLayer layer, float lift)
        {
            this.layer = layer;
            this.lift = lift;
        }

        /// <summary>
        /// Tells the plates which figure is ours and which team we are on, so the thinning rungs can
        /// keep the two names that matter — yours and the host's — and drop the rest.
        /// </summary>
        public void SetContext(int slot, int team)
        {
            localSlot = slot;
            localTeam = team;
        }

        /// <summary>Writes a slot's name, building its plate on first use.</summary>
        public void Set(int slot, string name, bool isHost, int team)
        {
            Ensure(slot);

            SlotLists.Grow(teamOf, slot);
            SlotLists.Grow(hosts, slot);

            teamOf[slot] = team;
            hosts[slot] = isHost;

            labels[slot].text = name;
            shadows[slot].text = name;
            underlines[slot].gameObject.SetActive(isHost);
        }

        /// <summary>
        /// Takes every name down. A VS lobby shows no over-head names at all — the member lists on
        /// the team plates are the roster there, and a name in both places reads as two rosters
        /// disagreeing about how to say the same thing.
        /// </summary>
        public void Hide()
        {
            foreach (RectTransform row in rows)
                if (row != null) row.gameObject.SetActive(false);
        }

        /// <summary>
        /// Keeps every plate over its head, at the size the space between heads allows.
        ///
        /// A plate whose slot is empty, or whose head is behind the camera, is hidden. So is one the
        /// ladder has thinned away — but never yours and never the host's: you must always be able
        /// to find yourself in the rank, however many people are standing in it.
        /// </summary>
        public void Position(Camera camera, IReadOnlyList<Transform> heads, IReadOnlyList<bool> occupied)
        {
            float nameWidth = WidestName();
            float pitch = SeatPitchOnCanvas(camera, heads, occupied);

            RankNameVisibility visibility = RankOverlayScale.NamesFor(pitch, nameWidth);
            float size = Mathf.Max(RankOverlayScale.MinFontSize,
                                   RankOverlayScale.SizeFor(NameSize, nameWidth, pitch));

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;

                bool standing = i < occupied.Count && occupied[i]
                                && i < heads.Count && heads[i] != null;

                bool visible = standing && Wanted(i, visibility)
                               && layer.Place(camera, rows[i], heads[i].position + Vector3.up * lift);

                rows[i].gameObject.SetActive(visible);

                if (visible) Resize(i, size);
            }
        }

        /// <summary>Whether a slot's name survives the current rung.</summary>
        private bool Wanted(int slot, RankNameVisibility visibility)
        {
            if (visibility == RankNameVisibility.All) return true;

            bool mine = slot == localSlot;
            bool host = slot < hosts.Count && hosts[slot];

            if (visibility == RankNameVisibility.YouAndHost) return mine || host;

            bool sameTeam = localTeam >= 0 && slot < teamOf.Count && teamOf[slot] == localTeam;
            return mine || host || sameTeam;
        }

        /// <summary>
        /// How far apart the two nearest heads are on the canvas, which is the room one name has.
        ///
        /// <para>
        /// Measured between real heads rather than derived from <see cref="RankLayout"/>, because
        /// that distance depends on where the camera ended up — and where the camera ended up is the
        /// thing being adapted to. Heads more than a row apart vertically are skipped: one well
        /// above another is not competing with it for horizontal space.
        /// </para>
        ///
        /// <para>
        /// Measured through <see cref="LobbyOverlayLayer.TryToCanvas"/>, so the answer is already in
        /// the canvas pixels every size in this file is written in. It used to project to SCREEN
        /// pixels and convert with a constant asserting the canvas matched width at 1920 — which
        /// described neither the scaler the lobby actually draws on nor, after the project settled
        /// on one rule, any scaler at all. The error was the ratio between the two rules, about 15%
        /// on a 21:9 monitor and the other way on a narrow window, and it fed straight into the font
        /// size ladder below.
        /// </para>
        /// </summary>
        private float SeatPitchOnCanvas(Camera camera, IReadOnlyList<Transform> heads,
            IReadOnlyList<bool> occupied)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i < heads.Count; i++)
            {
                if (i >= occupied.Count || !occupied[i] || heads[i] == null) continue;
                if (!layer.TryToCanvas(camera, heads[i].position, out Vector2 a)) continue;

                for (int j = i + 1; j < heads.Count; j++)
                {
                    if (j >= occupied.Count || !occupied[j] || heads[j] == null) continue;
                    if (!layer.TryToCanvas(camera, heads[j].position, out Vector2 b)) continue;

                    if (Mathf.Abs(a.y - b.y) > RowHeight) continue;

                    nearest = Mathf.Min(nearest, Mathf.Abs(a.x - b.x));
                }
            }

            // One person standing alone has the whole row to themselves.
            return nearest == float.MaxValue ? RowWidth : nearest;
        }

        /// <summary>The longest name currently standing, measured at the authored size.</summary>
        private float WidestName()
        {
            float widest = 0f;

            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == null || string.IsNullOrEmpty(labels[i].text)) continue;

                float current = labels[i].fontSize;

                labels[i].fontSize = NameSize;
                widest = Mathf.Max(widest, labels[i].GetPreferredValues(labels[i].text, 0f, 0f).x);
                labels[i].fontSize = current;
            }

            return widest > 0f ? widest : RowWidth;
        }

        /// <summary>Writes a size to both copies, and only when it actually changed.</summary>
        private void Resize(int slot, float size)
        {
            SlotLists.Grow(appliedSize, slot);

            if (Mathf.Abs(appliedSize[slot] - size) <= 0.25f) return;

            appliedSize[slot] = size;

            if (labels[slot] != null) labels[slot].fontSize = size;
            if (shadows[slot] != null) shadows[slot].fontSize = size;
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
