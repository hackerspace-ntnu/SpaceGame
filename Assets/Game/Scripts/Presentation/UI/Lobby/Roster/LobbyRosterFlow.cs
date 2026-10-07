using System;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// What the roster page does while the player stands in a lobby: copies the code, flips
    /// privacy, steps colours, moves between teams, retunes the team rules. Owns the
    /// <see cref="LobbyRosterView"/> it drives — the counterpart of <see cref="LobbyJoinFlow"/>.
    ///
    /// The two things that leave the page — starting the game and leaving the lobby — are handed
    /// in as callbacks, because both end in the screen navigating somewhere this flow cannot see.
    /// </summary>
    public sealed class LobbyRosterFlow
    {
        private readonly LobbySession session;
        private readonly LobbyRoute route;
        private readonly LobbyRosterView view;

        /// <param name="onStart">What the footer's Start does.</param>
        /// <param name="onLeave">What the footer's Leave does.</param>
        public LobbyRosterFlow(LobbySession session, LobbyRoute route, RectTransform root, GameObject entryPrefab,
            Action onStart, Action onLeave)
        {
            this.session = session;
            this.route = route;

            view = new LobbyRosterView(root, entryPrefab,
                new LobbyRosterView.Actions(onStart, onLeave, CopyCode, SetPrivacy, StepColor, JoinTeam,
                                            SetTeamRules));
        }

        /// <summary>
        /// Hands the view the two things it needs to redraw and asks it to. Both the poll and the
        /// first paint come through here, so the strip along the top is never a poll behind the
        /// astronauts underneath it.
        /// </summary>
        /// <param name="hostTitle">The world's own display name, for a story host only — else null.</param>
        public void Render(string hostTitle)
        {
            Lobby lobby = session.Current;

            view.SetSession(
                lobby?.Name,
                lobby?.LobbyCode,
                lobby != null && LobbyRoster.IsPlaying(lobby),
                lobby != null && lobby.IsPrivate);

            view.Render(session.CurrentSnapshot(), session.IsHost, hostTitle);
        }

        /// <summary>Holds the page while the session is still being created. See <see cref="LobbyRosterView.SetBusy"/>.</summary>
        public void SetBusy(bool isBusy, string caption = null) => view.SetBusy(isBusy, caption);

        /// <summary>A transient line. The next poll replaces it.</summary>
        public void Say(string message) => view.SetStatus(message);

        /// <summary>A failure the player has to actually read. Survives the poll's redraws.</summary>
        public void Warn(string message) => view.SetWarning(message);

        /// <summary>Tears the astronauts down with the page.</summary>
        public void Dispose() => view.Dispose();

        private void CopyCode()
        {
            Lobby lobby = session.Current;
            if (lobby == null || string.IsNullOrEmpty(lobby.LobbyCode)) return;

            GUIUtility.systemCopyBuffer = lobby.LobbyCode;
            view.SetStatus($"Copied {lobby.LobbyCode} to the clipboard.");
        }

        private async void SetPrivacy(bool isPrivate) => await session.SetPrivacyAsync(isPrivate);

        /// <summary>
        /// Steps a suit colour by one swatch — the local player's own, in a story lobby; the local
        /// player's whole TEAM's colour, in a VS one.
        ///
        /// In the story case, three things happen, in this order and for three different reasons.
        /// The preference is stored first, because it is the player's outfit and it has to survive
        /// them backing out of the lobby without starting anything. Our own astronaut is repainted
        /// second, synchronously, because a cycler that waits on a service call before showing
        /// anything feels broken. Everyone else is told last, through a debounced publish, because
        /// Lobby rate-limits player updates and browsing the whole palette is a dozen presses.
        /// </summary>
        private void StepColor(int direction)
        {
            if (route.IsVersus())
            {
                StepTeamColor(direction);
                return;
            }

            int next = SuitPalette.Step(GameSettings.SuitColorIndex, direction);

            GameSettings.SuitColorIndex = next;
            GameSettings.Save();

            view.SetLocalColor(next);
            session.PublishSuitColor(next);
        }

        /// <summary>
        /// The VS case does not touch <see cref="GameSettings.SuitColorIndex"/> at all: that
        /// preference belongs to the install, and a team colour is a property of the match.
        /// <see cref="TeamColorRules.Step"/> is handed every OTHER team's colour so it never lands
        /// the local team on a swatch a rival would then be unable to tell apart.
        /// </summary>
        private void StepTeamColor(int direction)
        {
            RosterSnapshot snapshot = session.CurrentSnapshot();
            int team = snapshot.LocalTeam;
            if (team < 0) return;

            int next = TeamColorRules.Step(snapshot.ColorOfTeam(team), direction, SuitPalette.Count,
                                           snapshot.ColorsOfOtherTeams(team));

            view.SetLocalColor(next);
            session.PublishTeamColor(next);
        }

        /// <summary>Moves the local player onto a different team, refusing one that has no room.</summary>
        private void JoinTeam(int team)
        {
            RosterSnapshot snapshot = session.CurrentSnapshot();
            if (team < 0 || team >= snapshot.TeamCount || team == snapshot.LocalTeam) return;

            if (!snapshot.HasRoomOn(team))
            {
                view.SetWarning($"{VersusRules.TeamName(team)} is full.");
                return;
            }

            view.SetStatus(string.Empty);
            session.PublishTeam(team);
        }

        /// <summary>Retunes the VS lobby's team rules. A refusal — someone displaced — arrives through Failed.</summary>
        private async void SetTeamRules(int teamCount, int teamSize) =>
            await session.SetTeamRulesAsync(teamCount, teamSize);
    }
}
