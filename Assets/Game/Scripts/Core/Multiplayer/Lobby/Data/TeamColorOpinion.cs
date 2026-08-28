using System.Globalization;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// One player's opinion of their VS team's colour, packed as <c>"swatch:stampMs"</c> into
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
    /// highest-stamped opinion among the players standing on that team. Last writer wins, which
    /// is exactly what pressing the colour cycler means: the stamp exists so that two members
    /// racing the arrow keys converge on whichever press landed last, on every peer, instead of
    /// disagreeing forever.
    /// </para>
    /// </summary>
    public static class TeamColorOpinion
    {
        private const char Separator = ':';

        /// <summary>Packs a swatch and the moment it was chosen into one player-data string.</summary>
        public static string Encode(int swatch, long stampMs) =>
            LobbyData.Invariant(swatch) + Separator + LobbyData.Invariant(stampMs);

        /// <summary>
        /// Reads back a value <see cref="Encode"/> wrote. Answers false rather than throwing on
        /// anything unrecognised — a peer on an older build, a value truncated by a service hiccup,
        /// or plain garbage must not take the roster down with it.
        /// </summary>
        public static bool TryDecode(string value, out int swatch, out long stampMs)
        {
            swatch = 0;
            stampMs = 0;

            if (string.IsNullOrEmpty(value)) return false;

            int separator = value.IndexOf(Separator);
            if (separator < 0) return false;

            return int.TryParse(value.Substring(0, separator), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out swatch)
                && long.TryParse(value.Substring(separator + 1), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out stampMs);
        }
    }
}
