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
        private const int PlateSize = 46;

        /// <summary>What a plate fades to when it cannot be joined right now. Present, not gone.</summary>
        private const float DimmedAlpha = 0.45f;

        private static readonly Vector2 ShadowOffset = new(3f, -3f);

        private sealed class Plate
        {
            public RectTransform Row;
            public TextMeshProUGUI Label;
            public Button Button;
        }

        private readonly LobbyOverlayLayer layer;
        private readonly Action<int> onJoinTeam;

        /// <summary>How far above a team's centre its plate floats, in metres.</summary>
        private readonly float lift;

        private readonly List<Plate> plates = new();

        /// <summary>The team shape the plates were last built for, so they are rebuilt only when it moves.</summary>
        private int teamCount = -1;
        private int teamSize = -1;

        public LobbyTeamPlates(LobbyOverlayLayer layer, float lift, Action<int> onJoinTeam)
        {
            this.layer = layer;
            this.lift = lift;
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
            teamCount = -1;
            teamSize = -1;
        }

        /// <summary>
        /// Keeps every plate over its cluster. Recomputed every frame from the cached team shape:
        /// the anchor never moves, but a plate still goes through the same behind-the-camera guard
        /// the nameplates rely on.
        /// </summary>
        public void Position(Camera camera, Transform anchor)
        {
            if (anchor == null) return;

            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null) continue;

                Vector3 worldPoint = anchor.TransformPoint(
                    RankLayout.TeamCenter(team, teamCount, teamSize) + Vector3.up * lift);

                plate.Row.gameObject.SetActive(layer.Place(camera, plate.Row, worldPoint));
            }
        }

        private Plate Build(int team)
        {
            RectTransform row = layer.Centred($"TeamPlate{team}", PlateWidth, PlateHeight);

            TextMeshProUGUI label = UIBuilder.ShadowedLabel(row, VersusRules.TeamName(team), PlateSize,
                                                            MenuEntry.Title, MenuEntry.Idle, ShadowOffset,
                                                            TextAlignmentOptions.Center, out _);
            label.raycastTarget = true;

            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            button.transition = Selectable.Transition.None;

            int capturedTeam = team;
            button.onClick.AddListener(() => onJoinTeam?.Invoke(capturedTeam));

            return new Plate { Row = row, Label = label, Button = button };
        }
    }
}
