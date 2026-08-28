using System;
using Unity.Services.Lobbies.Models;
using SpaceGame.Characters;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Reads who is in a <see cref="Lobby"/>: names, suit colours, which row is us and which is
    /// the host, and whether the host has already started.
    ///
    /// Pure and static so the views can be exercised without a network, an authentication service
    /// or Unity Gaming Services at all — the one thing that genuinely needs the authentication
    /// service, the local player's id, arrives as a parameter. The VS half of the roster (teams,
    /// team colours, occupancy) is <see cref="LobbyTeams"/>; <see cref="Snapshot"/> is where the
    /// two meet.
    /// </summary>
    public static class LobbyRoster
    {
        private const string UnnamedPlayer = "Player";

        /// <summary>The names to show in the roster, in lobby order.</summary>
        public static string[] Names(Lobby lobby)
        {
            if (lobby?.Players == null) return Array.Empty<string>();

            var names = new string[lobby.Players.Count];

            for (int i = 0; i < names.Length; i++)
                names[i] = LobbyData.Text(lobby.Players[i], LobbyKeys.PlayerName) ?? UnnamedPlayer;

            return names;
        }

        /// <summary>
        /// The suit colours to draw the rank in, in lobby order and index-aligned with
        /// <see cref="Names"/>.
        ///
        /// A peer on a build with a longer palette sends an index this one has never heard of, so
        /// everything lands through <c>SuitPalette.Clamp</c>. Anything unreadable falls back to
        /// swatch 0 rather than skipping the player, because a missing figure in the rank is much
        /// harder to understand than one wearing the wrong orange.
        /// </summary>
        public static int[] SuitColors(Lobby lobby)
        {
            if (lobby?.Players == null) return Array.Empty<int>();

            var colors = new int[lobby.Players.Count];

            for (int i = 0; i < colors.Length; i++)
                colors[i] = SuitPalette.Clamp(LobbyData.Int(lobby.Players[i], LobbyKeys.SuitColor, 0));

            return colors;
        }

        /// <summary>
        /// Which row of the roster is us, or -1 when that cannot be answered.
        ///
        /// Needed because the cycler belongs under one specific figure. Keyed on the
        /// authentication service's player id, which is what <c>LobbySession.IsHost</c> already
        /// compares against — the alternative, matching on name, breaks the moment two friends are
        /// both called Pilot.
        /// </summary>
        public static int SlotOf(Lobby lobby, string playerId)
        {
            if (lobby?.Players == null || string.IsNullOrEmpty(playerId)) return -1;

            for (int i = 0; i < lobby.Players.Count; i++)
                if (lobby.Players[i] != null && lobby.Players[i].Id == playerId)
                    return i;

            return -1;
        }

        /// <summary>Which row of the roster is the host, or -1. Marked in the rank with an underline.</summary>
        public static int HostSlot(Lobby lobby) => SlotOf(lobby, lobby?.HostId);

        /// <summary>True when this lobby's host is already playing, so a joiner skips the lobby screen.</summary>
        public static bool IsPlaying(Lobby lobby) =>
            LobbyData.Text(lobby, LobbyKeys.GameState) == LobbyKeys.StateInGame;

        /// <summary>"3/4" — taken over total. Lobby reports FREE slots, which reads inverted.</summary>
        public static string DescribeOccupancy(int maxPlayers, int availableSlots) =>
            $"{maxPlayers - availableSlots}/{maxPlayers}";

        /// <summary>
        /// Everything a roster view needs, taken off this lobby. A null lobby produces a safe empty
        /// snapshot rather than throwing — see <see cref="RosterSnapshot"/>.
        ///
        /// <para>
        /// The team rules are read once and the roster walked once, and both results are threaded
        /// into the occupancy and colour readers instead of letting each recompute the same team
        /// assignment from <paramref name="lobby"/> a second and third time.
        /// </para>
        ///
        /// <para>
        /// Takes <paramref name="localSlot"/> as a parameter but computes the host slot itself.
        /// The two are not symmetric: the local slot needs the authentication service's player
        /// id, which is exactly what this pure half is kept away from, so the session has to hand
        /// it in. The host slot is answerable from the lobby alone, and accepting it as a
        /// parameter would only invite a caller to pass one computed from a different, stale
        /// lobby than the one being snapshotted.
        /// </para>
        /// </summary>
        public static RosterSnapshot Snapshot(Lobby lobby, int localSlot, int swatchCount)
        {
            LobbyTeams.ReadRules(lobby, out int teamCount, out int teamSize);
            int[] teams = LobbyTeams.Teams(lobby, teamCount);

            return new RosterSnapshot(
                Names(lobby),
                SuitColors(lobby),
                teams,
                LobbyTeams.TeamColorsOf(lobby, teams, teamCount, swatchCount),
                LobbyTeams.Occupancy(teams, teamCount),
                teamCount,
                teamSize,
                localSlot,
                HostSlot(lobby),
                LobbyTeams.IsVersus(lobby));
        }
    }
}
