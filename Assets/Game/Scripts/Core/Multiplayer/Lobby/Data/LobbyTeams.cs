using System;
using Unity.Services.Lobbies.Models;
using SpaceGame.Gameplay;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Reads the VS shape of a <see cref="Lobby"/>: whether it is a match at all, its team rules,
    /// who stands on which team, and what colour each team wears.
    ///
    /// Every public reader takes the lobby alone. The internal overloads take a team assignment
    /// and rule counts already computed by the caller, so <see cref="LobbyRoster.Snapshot"/> can
    /// walk the roster once instead of three times.
    /// </summary>
    public static class LobbyTeams
    {
        /// <summary>
        /// Whether this lobby is a VS match rather than the story campaign.
        ///
        /// A lobby with no mode key at all reads as story. That default matters, not just for
        /// symmetry: a lobby created by a build that shipped before VS existed carries no
        /// <see cref="LobbyKeys.Mode"/> key at all, and it must keep reading as the story lobby it
        /// always was rather than suddenly being offered as a match.
        /// </summary>
        public static bool IsVersus(Lobby lobby) =>
            LobbyData.Text(lobby, LobbyKeys.Mode) == LobbyKeys.ModeVersus;

        /// <summary>
        /// How many teams this lobby is split into. Falls back to
        /// <see cref="VersusRules.DefaultTeams"/> when the key is absent or unparseable — a story
        /// lobby, or one from a build that predates VS — and is always clamped, so a value written
        /// by a peer with looser limits still lands somewhere this build's rules recognise.
        /// </summary>
        public static int TeamCountOf(Lobby lobby)
        {
            ReadRules(lobby, out int teamCount, out _);
            return teamCount;
        }

        /// <summary>How big each team is. See <see cref="TeamCountOf"/> — the same fallback and clamp.</summary>
        public static int TeamSizeOf(Lobby lobby)
        {
            ReadRules(lobby, out _, out int teamSize);
            return teamSize;
        }

        /// <summary>
        /// Which team each player stands on, in lobby order and index-aligned with
        /// <see cref="LobbyRoster.Names"/>. A player with no team key is on team 0.
        ///
        /// A team index this lobby's own rules do not recognise — a peer from a build that allows
        /// more teams than this one's <see cref="TeamCountOf"/> — is folded back into range with
        /// <see cref="FoldTeam"/> rather than dropped or clamped to 0. See that method for why.
        /// </summary>
        public static int[] Teams(Lobby lobby) => Teams(lobby, TeamCountOf(lobby));

        /// <summary>Heads standing on each team, index-aligned with team number.</summary>
        public static int[] Occupancy(Lobby lobby) => Occupancy(Teams(lobby), TeamCountOf(lobby));

        /// <summary>
        /// One swatch per team: the highest-stamped opinion among that team's members, else
        /// <see cref="TeamColorRules.DefaultColors"/>. Always exactly <see cref="TeamCountOf"/>
        /// entries long, even for a lobby with nobody in it at all.
        ///
        /// Ties — two players on the same team publishing the same stamp — go to whichever comes
        /// first in lobby order. That has to be resolved the same way on every peer or two machines
        /// paint the same team's rank in different colours; it is why the comparison is strict
        /// (<c>&gt;</c>, never <c>&gt;=</c>) — a later, equally-stamped opinion never displaces
        /// the earlier one that already claimed the team.
        /// </summary>
        public static int[] TeamColorsOf(Lobby lobby, int swatchCount) =>
            TeamColorsOf(lobby, Teams(lobby), TeamCountOf(lobby), swatchCount);

        /// <summary>
        /// Reads and clamps both team-rule keys together, in the order <see cref="VersusRules"/>'s
        /// docs pin as its pairing contract: <see cref="VersusRules.ClampTeams"/> first, against the
        /// raw size, then <see cref="VersusRules.ClampTeamSize"/> fed that already-clamped count.
        /// Clamping either axis alone, against an unclamped partner, is how a host ends up with
        /// numbers whose product is nowhere near <see cref="VersusRules.MaxSeats"/>.
        /// </summary>
        internal static void ReadRules(Lobby lobby, out int teamCount, out int teamSize)
        {
            int rawTeams = LobbyData.Int(lobby, LobbyKeys.TeamCount, VersusRules.DefaultTeams);
            int rawSize = LobbyData.Int(lobby, LobbyKeys.TeamSize, VersusRules.DefaultTeamSize);

            teamCount = VersusRules.ClampTeams(rawTeams, rawSize);
            teamSize = VersusRules.ClampTeamSize(rawSize, teamCount);
        }

        internal static int[] Teams(Lobby lobby, int teamCount)
        {
            if (lobby?.Players == null) return Array.Empty<int>();

            var teams = new int[lobby.Players.Count];

            for (int i = 0; i < teams.Length; i++)
                teams[i] = FoldTeam(LobbyData.Int(lobby.Players[i], LobbyKeys.Team, 0), teamCount);

            return teams;
        }

        internal static int[] Occupancy(int[] teams, int teamCount)
        {
            var occupancy = new int[teamCount];

            foreach (int team in teams)
                occupancy[team]++;

            return occupancy;
        }

        internal static int[] TeamColorsOf(Lobby lobby, int[] teams, int teamCount, int swatchCount)
        {
            int[] colors = TeamColorRules.DefaultColors(teamCount, swatchCount);

            if (lobby?.Players == null) return colors;

            var bestStamp = new long[teamCount];
            var hasOpinion = new bool[teamCount];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                string opinion = LobbyData.Text(lobby.Players[i], LobbyKeys.TeamColor);
                if (!TeamColorOpinion.TryDecode(opinion, out int swatch, out long stampMs)) continue;

                int team = teams[i];

                // Strict: a later opinion only wins on a HIGHER stamp. An equal stamp leaves the
                // earlier player's colour standing, which is the tie-break the doc above promises.
                if (hasOpinion[team] && stampMs <= bestStamp[team]) continue;

                bestStamp[team] = stampMs;
                hasOpinion[team] = true;
                colors[team] = ClampSwatch(swatch, swatchCount);
            }

            return colors;
        }

        /// <summary>
        /// Wraps a team index that does not belong to this lobby's rules back into
        /// <c>[0, teamCount)</c>, by modulus rather than by clamping to 0.
        ///
        /// Clamping every out-of-range index to 0 would mean every peer running a build with a
        /// bigger palette of teams — team 7 and team 70 alike — lands on the SAME team, piling
        /// every stray player onto team one specifically. Wrapping spreads them out instead, which
        /// is no less arbitrary but does not single out one team as the dumping ground. Either way
        /// the result is always inside <c>[0, teamCount)</c>, which <see cref="Occupancy(int[], int)"/>
        /// and <see cref="TeamColorsOf(Lobby, int[], int, int)"/> depend on to index their per-team
        /// arrays without a bounds check of their own.
        /// </summary>
        private static int FoldTeam(int team, int teamCount)
        {
            if (teamCount <= 0) return 0;

            int folded = team % teamCount;
            return folded < 0 ? folded + teamCount : folded;
        }

        private static int ClampSwatch(int swatch, int swatchCount)
        {
            if (swatchCount <= 0 || swatch < 0) return 0;
            return swatch >= swatchCount ? swatchCount - 1 : swatch;
        }
    }
}
