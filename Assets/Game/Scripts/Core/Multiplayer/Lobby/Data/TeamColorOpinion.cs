using System.Globalization;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// One player's opinion of a VS team's colour, packed as <c>"swatch:stampMs:team"</c> into
    /// their own player data under <see cref="LobbyKeys.TeamColor"/>.
    ///
    /// <para>
    /// PLAYER data rather than a shared table in lobby data, and that split is load-bearing:
    /// <c>LobbyService.UpdateLobbyAsync</c> is host-only, so a table of team colours living in
    /// lobby data could only ever be written by the host — and the design requires that ANY
    /// member standing in a team may recolour it, not just whoever happens to be hosting.
    /// <c>UpdatePlayerAsync</c> carries no such restriction; a member can always write their own
    /// player entry.
    /// </para>
    ///
    /// <para>
    /// So a team's colour is not stored anywhere directly — it is derived, in
    /// <see cref="LobbyTeams.TeamColorsOf(Unity.Services.Lobbies.Models.Lobby, int)"/>, as the
    /// highest-stamped opinion CAST FOR that team. Last writer wins, which is exactly what
    /// pressing the colour arrows means: the stamp exists so that two members racing the arrow
    /// keys converge on whichever press landed last, on every peer, instead of disagreeing
    /// forever.
    /// </para>
    ///
    /// <para>
    /// The team is part of the vote, not read off where the voter happens to stand. A vote that
    /// followed its voter around would let a player switching teams drag their colour onto the
    /// new team — and strip it off the old one — when the rule is that you continue with the
    /// colour the new team already had. The tagged vote also keeps colouring the old team after
    /// its caster has moved on, so the members left behind never see their team flip because
    /// somebody walked away.
    /// </para>
    /// </summary>
    public static class TeamColorOpinion
    {
        private const char Separator = ':';

        /// <summary>No team recorded — an older build's two-part vote. See <see cref="TryDecode"/>.</summary>
        public const int NoTeam = -1;

        /// <summary>Packs a swatch, the moment it was chosen, and the team it was chosen FOR.</summary>
        public static string Encode(int swatch, long stampMs, int team) =>
            LobbyData.Invariant(swatch) + Separator + LobbyData.Invariant(stampMs)
            + Separator + LobbyData.Invariant(team);

        /// <summary>
        /// Reads back a value <see cref="Encode"/> wrote. Answers false rather than throwing on
        /// anything unrecognised — a peer on an older build, a value truncated by a service hiccup,
        /// or plain garbage must not take the roster down with it.
        ///
        /// A two-part <c>"swatch:stampMs"</c> from a build that predates the team tag still decodes,
        /// with <paramref name="team"/> as <see cref="NoTeam"/> — the reader falls back to the
        /// voter's current team, which is exactly what that build meant by it.
        /// </summary>
        public static bool TryDecode(string value, out int swatch, out long stampMs, out int team)
        {
            swatch = 0;
            stampMs = 0;
            team = NoTeam;

            if (string.IsNullOrEmpty(value)) return false;

            string[] parts = value.Split(Separator);
            if (parts.Length < 2 || parts.Length > 3) return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out swatch)
                || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out stampMs))
                return false;

            if (parts.Length == 2) return true;

            return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out team);
        }
    }
}
