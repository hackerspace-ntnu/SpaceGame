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
    /// Drawn the same white-over-navy way the nameplates are, because a plate hangs over the same
    /// sky and heads, in the team's own colour. A <see cref="Button"/> over the whole row rather
    /// than just the text, so the click target is as wide as the plate reads. Rebuilt only when the
    /// team shape changes — that pair is the only thing that ever moves a plate, so anything else in
    /// a poll must not pay for a rebuild.
    /// </summary>
    internal sealed class LobbyTeamPlates
    {
        private const float PlateWidth = 520f;
        private const float PlateHeight = 72f;
        private const int PlateSize = 40;

        /// <summary>What a plate fades to when it cannot be joined right now. Present, not gone.</summary>
        private const float DimmedAlpha = 0.45f;

        private static readonly Vector2 ShadowOffset = new(3f, -3f);

        private sealed class Plate
        {
            public RectTransform Row;
            public TextMeshProUGUI Label;

            /// <summary>The navy copy behind the label. Written with it or the shadow goes stale.</summary>
            public TextMeshProUGUI Shadow;

            public Button Button;

            /// <summary>Where this plate landed on the canvas last frame, for the collision measure.</summary>
            public Vector2 Screen;

            public bool Visible;

            /// <summary>The size and text last written, so an unchanged plate costs no mesh rebuild.</summary>
            public float AppliedSize = -1f;

            public string AppliedText;
        }

        private readonly LobbyOverlayLayer layer;
        private readonly Action<int> onJoinTeam;

        private readonly List<Plate> plates = new();

        /// <summary>The team shape the plates were last built for, so they are rebuilt only when it moves.</summary>
        private int teamCount = -1;
        private int teamSize = -1;

        /// <summary>
        /// How many heads are on each team, kept from the last <see cref="Update"/> so
        /// <see cref="Position"/> can build the shortened label's "2/3" without a snapshot.
        /// </summary>
        private readonly List<int> headsOn = new();

        public LobbyTeamPlates(LobbyOverlayLayer layer, Action<int> onJoinTeam)
        {
            this.layer = layer;
            this.onJoinTeam = onJoinTeam;
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
            headsOn.Clear();
            for (int team = 0; team < plates.Count; team++) headsOn.Add(snapshot.HeadsOn(team));

            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null) continue;

                Color color = SuitPalette.ColorOf(snapshot.ColorOfTeam(team));
                bool canJoin = LobbyPreviewRank.CanJoin(team, snapshot.LocalTeam, snapshot.HeadsOn(team), teamSize);
                float alpha = canJoin ? 1f : DimmedAlpha;

                if (plate.Label != null) plate.Label.color = new Color(color.r, color.g, color.b, alpha);
                if (plate.Button != null) plate.Button.interactable = canJoin;
            }
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

            return new Plate { Row = row, Label = label, Shadow = shadow, Button = button };
        }
    }
}
