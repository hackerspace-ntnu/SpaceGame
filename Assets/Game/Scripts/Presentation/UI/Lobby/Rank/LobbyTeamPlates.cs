using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// One clickable nameplate per team, floating above its cluster in a VS lobby.
    ///
    /// <para>
    /// Drawn the same white-over-navy way the nameplates are, because a plate hangs over the same
    /// sky and heads, in the team's own colour. A <see cref="Button"/> over the whole row rather
    /// than just the text, so the click target is as wide as the plate reads. Rebuilt only when the
    /// team shape changes — that pair is the only thing that ever moves a plate, so anything else in
    /// a poll must not pay for a rebuild.
    /// </para>
    ///
    /// <para>
    /// The local team's plate is also the team-colour control: a chevron on either side of the
    /// name steps the colour, and the colour itself is shown as nothing more than the colour the
    /// name is drawn in. The chevrons live only on YOUR team's plate, because that is the only
    /// colour that is yours to change.
    /// </para>
    ///
    /// <para>
    /// Each plate also lists its team's members vertically, in small lowercase — hanging BELOW a
    /// front-row plate and stacked ABOVE a back-row one, because a back-row plate already hangs
    /// high (<see cref="RankLayout.PlateLift"/>) and a list below it would descend into the front
    /// row's band of screen. The list is a child of the plate row, so it follows the plate's
    /// world-tracking and visibility for free.
    /// </para>
    /// </summary>
    internal sealed class LobbyTeamPlates
    {
        private const float PlateWidth = 520f;
        private const float PlateHeight = 72f;
        private const int PlateSize = 40;

        private const float ArrowWidth = 56f;

        /// <summary>
        /// Between the name's edge and its chevron. The chevrons hug the TEXT, not the plate's
        /// 520px rect — a plate row is far wider than the word on it, and a chevron parked at the
        /// row's edge lands over the neighbouring team's plate the moment plates crowd.
        /// </summary>
        private const float ArrowGap = 8f;

        /// <summary>Deliberately well under <see cref="PlateSize"/>: the members are a caption to the plate.</summary>
        private const int MemberSize = 26;

        private const float MemberRowHeight = 32f;

        /// <summary>Between the plate's edge and the first member name.</summary>
        private const float MemberGap = 6f;

        /// <summary>What a plate fades to when it cannot be joined right now. Present, not gone.</summary>
        private const float DimmedAlpha = 0.45f;

        private static readonly Vector2 ShadowOffset = new(3f, -3f);

        /// <summary>A shallower shadow than the plate's own — at 26pt the plate's 3px reads as smear.</summary>
        private static readonly Vector2 MemberShadowOffset = new(2f, -2f);

        private sealed class Plate
        {
            public RectTransform Row;
            public TextMeshProUGUI Label;

            /// <summary>The navy copy behind the label. Written with it or the shadow goes stale.</summary>
            public TextMeshProUGUI Shadow;

            public Button Button;

            /// <summary>The colour chevrons at the plate's edges. Shown only on the local team's plate.</summary>
            public GameObject LeftArrow;

            public GameObject RightArrow;

            /// <summary>The member list's rows, grown as the team grows and switched off, never destroyed.</summary>
            public readonly List<RectTransform> MemberRows = new();

            public readonly List<TextMeshProUGUI> MemberLabels = new();

            /// <summary>The navy copies behind the member names. Written with them or the shadows go stale.</summary>
            public readonly List<TextMeshProUGUI> MemberShadows = new();

            /// <summary>Where this plate landed on the canvas last frame, for the collision measure.</summary>
            public Vector2 Screen;

            public bool Visible;

            /// <summary>The size and text last written, so an unchanged plate costs no mesh rebuild.</summary>
            public float AppliedSize = -1f;

            public string AppliedText;
        }

        private readonly LobbyOverlayLayer layer;
        private readonly GameObject entryPrefab;
        private readonly Action<int> onJoinTeam;
        private readonly Action<int> onStepColor;

        private readonly List<Plate> plates = new();

        /// <summary>The team shape the plates were last built for, so they are rebuilt only when it moves.</summary>
        private int teamCount = -1;
        private int teamSize = -1;

        /// <summary>Which team the local player stands on, from the last <see cref="Update"/>.</summary>
        private int localTeam = -1;

        /// <summary>
        /// How many heads are on each team, kept from the last <see cref="Update"/> so
        /// <see cref="Position"/> can build the shortened label's "2/3" without a snapshot.
        /// </summary>
        private readonly List<int> headsOn = new();

        /// <summary>One team's member names, gathered per team per repaint. Reused, never grown per poll.</summary>
        private readonly List<string> memberBuffer = new();

        /// <param name="onStepColor">Called with -1 or +1 when a colour chevron is pressed.</param>
        public LobbyTeamPlates(LobbyOverlayLayer layer, GameObject entryPrefab,
            Action<int> onJoinTeam, Action<int> onStepColor)
        {
            this.layer = layer;
            this.entryPrefab = entryPrefab;
            this.onJoinTeam = onJoinTeam;
            this.onStepColor = onStepColor;
        }

        /// <summary>Builds one plate per team, only when the team shape is not what is standing.</summary>
        public void Ensure(int newTeamCount, int newTeamSize)
        {
            if (newTeamCount == teamCount && newTeamSize == teamSize) return;

            Clear();

            for (int team = 0; team < newTeamCount; team++)
                plates.Add(Build(team));

            teamCount = newTeamCount;
            teamSize = newTeamSize;
        }

        /// <summary>
        /// Repaints every plate from the current snapshot: the team's own colour, and whether it can
        /// be joined right now — greyed and unclickable when it cannot, rather than hidden, because
        /// a full or your-own team is still part of the match.
        /// </summary>
        public void Update(RosterSnapshot snapshot)
        {
            localTeam = snapshot.LocalTeam;

            headsOn.Clear();
            for (int team = 0; team < plates.Count; team++) headsOn.Add(snapshot.HeadsOn(team));

            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null) continue;

                bool isLocal = team == localTeam;

                Color color = SuitPalette.ColorOf(snapshot.ColorOfTeam(team));
                bool canJoin = LobbyPreviewRank.CanJoin(team, snapshot.LocalTeam, snapshot.HeadsOn(team), teamSize);
                float alpha = canJoin ? 1f : DimmedAlpha;

                if (plate.Label != null) plate.Label.color = new Color(color.r, color.g, color.b, alpha);
                if (plate.Button != null) plate.Button.interactable = canJoin;

                if (plate.LeftArrow != null) plate.LeftArrow.SetActive(isLocal);
                if (plate.RightArrow != null) plate.RightArrow.SetActive(isLocal);

                RenderMembers(plate, team, snapshot);
            }
        }

        /// <summary>
        /// Fills one plate's vertical member list from the snapshot, in lobby order and lowercase.
        /// Rows hang downward from a front-row plate; a back-row plate stacks them upward instead,
        /// with the ORDER kept top-to-bottom either way — a list that grows upward still has to read
        /// downward, so the first member's row is simply placed highest.
        /// </summary>
        private void RenderMembers(Plate plate, int team, RosterSnapshot snapshot)
        {
            memberBuffer.Clear();
            for (int slot = 0; slot < snapshot.Names.Length; slot++)
                if (slot < snapshot.Teams.Length && snapshot.Teams[slot] == team
                    && !string.IsNullOrEmpty(snapshot.Names[slot]))
                    memberBuffer.Add(snapshot.Names[slot].ToLowerInvariant());

            bool above = team / RankLayout.TeamsPerRow(Mathf.Max(1, teamCount)) > 0;

            for (int i = 0; i < memberBuffer.Count; i++)
            {
                EnsureMemberRow(plate, i);

                float fromEdge = MemberGap + MemberRowHeight *
                                 ((above ? memberBuffer.Count - 1 - i : i) + 0.5f);
                float y = above ? PlateHeight * 0.5f + fromEdge : -PlateHeight * 0.5f - fromEdge;

                plate.MemberRows[i].anchoredPosition = new Vector2(0f, y);
                plate.MemberRows[i].gameObject.SetActive(true);

                // Assigning TMP text rebuilds its mesh, and this runs on every poll for every seat.
                if (plate.MemberLabels[i].text != memberBuffer[i])
                {
                    plate.MemberLabels[i].text = memberBuffer[i];
                    plate.MemberShadows[i].text = memberBuffer[i];
                }
            }

            for (int i = memberBuffer.Count; i < plate.MemberRows.Count; i++)
                plate.MemberRows[i].gameObject.SetActive(false);
        }

        private static void EnsureMemberRow(Plate plate, int index)
        {
            while (plate.MemberRows.Count <= index)
            {
                RectTransform row = UIBuilder.Rect($"Member{plate.MemberRows.Count}", plate.Row);
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
                row.pivot = new Vector2(0.5f, 0.5f);
                row.sizeDelta = new Vector2(PlateWidth, MemberRowHeight);

                plate.MemberLabels.Add(UIBuilder.ShadowedLabel(row, string.Empty, MemberSize,
                                                               MenuEntry.Title, MenuEntry.Idle,
                                                               MemberShadowOffset,
                                                               TextAlignmentOptions.Center,
                                                               out TextMeshProUGUI shadow));
                plate.MemberShadows.Add(shadow);
                plate.MemberRows.Add(row);
            }
        }

        /// <summary>
        /// Repaints the local team's plate for the frame a chevron is pressed, without waiting for
        /// the publish-then-poll round trip. Only the hue moves — the alpha keeps whatever the
        /// dimming rule last decided.
        /// </summary>
        public void ShowLocalColor(int colorIndex)
        {
            if (localTeam < 0 || localTeam >= plates.Count) return;

            Plate plate = plates[localTeam];
            if (plate.Label == null) return;

            Color color = SuitPalette.ColorOf(colorIndex);
            plate.Label.color = new Color(color.r, color.g, color.b, plate.Label.color.a);
        }

        /// <summary>A story lobby shows no plates at all — there is nothing to click with only one line.</summary>
        public void Clear()
        {
            foreach (Plate plate in plates)
                if (plate.Row != null) UnityEngine.Object.Destroy(plate.Row.gameObject);

            plates.Clear();
            headsOn.Clear();
            teamCount = -1;
            teamSize = -1;
            localTeam = -1;
        }

        /// <summary>
        /// Keeps every plate over its cluster, at the size the room beside it allows. Recomputed
        /// every frame from the cached team shape: the anchor never moves, but a plate still goes
        /// through the same behind-the-camera guard the nameplates rely on.
        ///
        /// <paramref name="groundOfTeam"/> answers the height a team is actually standing at, so a
        /// plate over a team on a rise hangs over that team rather than at the anchor's height with
        /// its own astronauts above it.
        /// </summary>
        public void Position(Camera camera, Transform anchor, Func<int, int, int, float> groundOfTeam)
        {
            if (anchor == null) return;

            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null) continue;

                Vector3 flat = anchor.TransformPoint(RankLayout.TeamCenter(team, teamCount, teamSize));
                float groundY = groundOfTeam != null ? groundOfTeam(team, teamCount, teamSize) : flat.y;

                // Per-row lift: a back row hangs its plates higher, so the rows stay apart on screen.
                var worldPoint = new Vector3(flat.x, groundY + RankLayout.PlateLift(team, teamCount), flat.z);

                plate.Visible = layer.Place(camera, plate.Row, worldPoint);
                plate.Screen = plate.Row.anchoredPosition;
                plate.Row.gameObject.SetActive(plate.Visible);
            }

            ApplyLadder();
        }

        /// <summary>
        /// Shrinks and shortens each plate to the room it has beside its nearest neighbour.
        ///
        /// <para>
        /// Room is measured against every OTHER plate rather than against the team next door in the
        /// layout: once teams wrap into two rows the nearest plate on screen is often the staggered
        /// one behind, and a plate sized against its own row would still collide with it. Only
        /// plates within a plate-height vertically are counted — one sitting well above another does
        /// not compete with it for horizontal space.
        /// </para>
        ///
        /// <para>
        /// The floor rung is a number and an occupancy, never a bare colour swatch: which team a
        /// plate belongs to must survive in text, not only in its tint.
        /// </para>
        /// </summary>
        private void ApplyLadder()
        {
            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null || !plate.Visible || plate.Label == null) continue;

                float available = float.MaxValue;

                for (int other = 0; other < plates.Count; other++)
                {
                    if (other == team) continue;

                    Plate rival = plates[other];
                    if (rival.Row == null || !rival.Visible) continue;
                    if (Mathf.Abs(rival.Screen.y - plate.Screen.y) > PlateHeight) continue;

                    available = Mathf.Min(available, Mathf.Abs(rival.Screen.x - plate.Screen.x));
                }

                if (available == float.MaxValue) available = PlateWidth;

                string full = VersusRules.TeamName(team);
                string shortened = VersusRules.ShortTeamName(team) + " " + Occupancy(team);
                string floor = (team + 1) + " " + Occupancy(team);

                LabelFit fit = RankOverlayScale.Fit(PlateSize, available,
                                                    Width(plate.Label, full),
                                                    Width(plate.Label, shortened),
                                                    Width(plate.Label, floor));

                string text = fit.Rung == RankLabelRung.Shortened ? shortened
                            : fit.Rung == RankLabelRung.Floor ? floor
                            : full;

                Write(plate, text, fit.FontSize);
            }
        }

        private string Occupancy(int team) =>
            team < headsOn.Count ? headsOn[team] + "/" + Mathf.Max(1, teamSize) : string.Empty;

        /// <summary>
        /// How wide a string is at the plate's AUTHORED size, whatever size the label happens to be
        /// drawn at right now.
        ///
        /// The size is set, measured and put back rather than scaled arithmetically:
        /// <see cref="RankOverlayScale"/> scales from the authored size, so measuring at the CURRENT
        /// one would feed last frame's answer into this frame's and let the size walk down to
        /// nothing over a few seconds.
        /// </summary>
        private static float Width(TextMeshProUGUI label, string text)
        {
            float current = label.fontSize;

            label.fontSize = PlateSize;
            float width = label.GetPreferredValues(text, 0f, 0f).x;
            label.fontSize = current;

            return width;
        }

        /// <summary>
        /// Writes text and size to both copies of the label, and only when either actually changed —
        /// assigning to a TMP label rebuilds its mesh, and this runs every frame for every team.
        /// </summary>
        private static void Write(Plate plate, string text, float size)
        {
            if (Mathf.Abs(plate.AppliedSize - size) <= 0.25f && plate.AppliedText == text) return;

            plate.AppliedSize = size;
            plate.AppliedText = text;

            if (plate.Label != null)
            {
                plate.Label.fontSize = size;
                plate.Label.text = text;
            }

            if (plate.Shadow != null)
            {
                plate.Shadow.fontSize = size;
                plate.Shadow.text = text;
            }

            // The name just changed width, and the chevrons hug the name.
            PlaceArrows(plate);
        }

        private Plate Build(int team)
        {
            RectTransform row = layer.Centred($"TeamPlate{team}", PlateWidth, PlateHeight);

            TextMeshProUGUI label = UIBuilder.ShadowedLabel(row, VersusRules.TeamName(team), PlateSize,
                                                            MenuEntry.Title, MenuEntry.Idle, ShadowOffset,
                                                            TextAlignmentOptions.Center,
                                                            out TextMeshProUGUI shadow);
            label.raycastTarget = true;

            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            button.transition = Selectable.Transition.None;

            // The pointer feedback: a joinable plate zooms under the cursor and dips on the press.
            // Scale rather than tint, because the label is already in the team's own colour.
            row.gameObject.AddComponent<HoverScale>().Bind(button);

            int capturedTeam = team;
            button.onClick.AddListener(() => onJoinTeam?.Invoke(capturedTeam));

            var plate = new Plate
            {
                Row = row, Label = label, Shadow = shadow, Button = button,
                LeftArrow = Arrow(row, "PrevTeamColor", "<", -1),
                RightArrow = Arrow(row, "NextTeamColor", ">", 1),
            };

            PlaceArrows(plate);
            return plate;
        }

        /// <summary>
        /// One colour chevron beside the plate's name. Its own <see cref="Button"/> on top of the
        /// plate's, so a press steps the colour rather than falling through to the join click
        /// underneath — and white via <see cref="MenuEntry.MakeLight"/>, like everything else drawn
        /// over sky, because the TEXT between the chevrons is what carries the team's colour.
        /// Centre-anchored so <see cref="PlaceArrows"/> can slide it against the text's current
        /// width; hidden until <see cref="Update"/> puts it on the local team's plate.
        /// </summary>
        private GameObject Arrow(RectTransform row, string name, string glyph, int direction)
        {
            RectTransform slot = UIBuilder.Rect(name, row);
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.sizeDelta = new Vector2(ArrowWidth, PlateHeight);

            Button button = MenuEntry.Create(entryPrefab, slot, name, glyph, PlateSize, PlateHeight,
                                             () => onStepColor?.Invoke(direction),
                                             out TextMeshProUGUI label);
            label.alignment = TextAlignmentOptions.Center;
            MenuEntry.MakeLight(button, label);

            slot.gameObject.SetActive(false);
            return slot.gameObject;
        }

        /// <summary>
        /// Slides both chevrons up against the name's rendered width — measured at whatever text
        /// and size the ladder last applied, so they follow the plate as it shrinks and shortens.
        /// </summary>
        private static void PlaceArrows(Plate plate)
        {
            if (plate.LeftArrow == null || plate.RightArrow == null || plate.Label == null) return;

            float half = plate.Label.GetPreferredValues(plate.Label.text, 0f, 0f).x * 0.5f
                         + ArrowGap + ArrowWidth * 0.5f;

            ((RectTransform)plate.LeftArrow.transform).anchoredPosition = new Vector2(-half, 0f);
            ((RectTransform)plate.RightArrow.transform).anchoredPosition = new Vector2(half, 0f);
        }
    }
}
