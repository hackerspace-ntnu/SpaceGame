using System.Collections.Generic;
using System.Globalization;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using SpaceGame.Characters;

namespace SpaceGame.Core
{
    /// <summary>Where a session is in its life. The view renders from this and nothing else.</summary>
    public enum LobbyState { Idle, InLobby, InGame }

    /// <summary>
    /// The pure half of <see cref="LobbySession"/>: the option objects handed to the Lobby service.
    ///
    /// Separated from the MonoBehaviour half so it can be tested without a live service, because
    /// this is where the bugs that made the lobby unusable actually lived — a relay code written a
    /// moment too late and a lock that made joining a running session impossible.
    ///
    /// There are no passwords here. A session is either listed in the browser or reachable only by
    /// its code, and the code is already the thing you have to be told; a second secret on top of it
    /// guarded nothing the code did not already guard.
    /// </summary>
    public partial class LobbySession
    {
        /// <summary>Relay join code, so a member can reach the server the host allocated.</summary>
        public const string KeyRelayJoinCode = "RelayJoinCode";

        public const string KeyPlayerName = "PlayerName";

        /// <summary>
        /// The player's suit colour, as an index into <c>SuitPalette.Swatches</c>.
        ///
        /// Member-visible like the name: it is only meaningful to people looking at the rank of
        /// astronauts inside the lobby, and the browser has no use for it.
        /// </summary>
        public const string KeySuitColor = "SuitColor";

        /// <summary>Whether the host is still in the lobby or already playing.</summary>
        public const string KeyGameState = "GameState";

        public const string StateWaiting = "waiting";
        public const string StateInGame = "in-game";

        /// <summary>
        /// The options a lobby is created with.
        ///
        /// The relay code goes in here rather than into a follow-up UpdateLobbyAsync: a client
        /// polling in the gap between the two saw a lobby with no join code and read straight past
        /// the missing key.
        /// </summary>
        public static CreateLobbyOptions BuildCreateOptions(bool isPrivate, string relayJoinCode,
            string playerName, int suitColor) => new()
        {
            IsPrivate = isPrivate,
            Player = BuildPlayer(playerName, suitColor),
            Data = new Dictionary<string, DataObject>
            {
                { KeyRelayJoinCode, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) },

                // Public, not Member: the browser labels rows the player has not joined.
                { KeyGameState, new DataObject(DataObject.VisibilityOptions.Public, StateWaiting) }
            }
        };

        /// <summary>
        /// The options that mark a lobby as playing.
        ///
        /// Deliberately does NOT set IsLocked. Locking here is what made joining a session already
        /// in progress impossible — and the host is usually alone when the first friend tries.
        /// </summary>
        public static UpdateLobbyOptions BuildBeginGameOptions() => new()
        {
            Data = new Dictionary<string, DataObject>
            {
                { KeyGameState, new DataObject(DataObject.VisibilityOptions.Public, StateInGame) }
            }
        };

        /// <summary>
        /// The options that change a live lobby's privacy.
        ///
        /// Privacy is set after the lobby exists because the host is never asked for it before: the
        /// session is created the moment the lobby page opens, named after the world they already
        /// chose. Asking first would put back the create form that page exists to remove.
        ///
        /// Private here means delisted, nothing more. The lobby stays reachable by its code, which
        /// is the whole point — a host turns this on to stop strangers arriving from the browser,
        /// not to shut out the people they sent the code to.
        /// </summary>
        public static UpdateLobbyOptions BuildPrivacyOptions(bool isPrivate) => new()
        {
            IsPrivate = isPrivate
        };

        /// <summary>
        /// The options that change this player's suit colour on a lobby they are already in.
        ///
        /// Only the colour is sent. Including the name would make every arrow press also rewrite the
        /// name, so a rename typed on another screen mid-lobby could be reverted by a colour change.
        /// </summary>
        public static UpdatePlayerOptions BuildSuitColorOptions(int suitColor) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeySuitColor, SuitColorData(suitColor) }
            }
        };

        /// <summary>"3/4" — taken over total. Lobby reports FREE slots, which reads inverted.</summary>
        public static string DescribeOccupancy(int maxPlayers, int availableSlots) =>
            $"{maxPlayers - availableSlots}/{maxPlayers}";

        public static Player BuildPlayer(string playerName, int suitColor) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyPlayerName, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) },
                { KeySuitColor, SuitColorData(suitColor) }
            }
        };

        private static PlayerDataObject SuitColorData(int suitColor) =>
            new(PlayerDataObject.VisibilityOptions.Member,
                SuitPalette.Clamp(suitColor).ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// The suit colours to draw the rank in, in lobby order and index-aligned with
        /// <see cref="PlayerNames"/>.
        ///
        /// Guarded on every step for the same reason that method is: a player object written by an
        /// older build, or one still mid-join, may not carry the key at all — and this one has a
        /// second way to go wrong that the name does not, because the value has to be parsed. A peer
        /// on a build with a longer palette sends an index this one has never heard of, so
        /// everything lands through <c>SuitPalette.Clamp</c>. Anything unreadable falls back to
        /// swatch 0 rather than skipping the player, because a missing figure in the rank is much
        /// harder to understand than one wearing the wrong orange.
        /// </summary>
        public static int[] SuitColors(Lobby lobby)
        {
            if (lobby?.Players == null) return System.Array.Empty<int>();

            var colors = new int[lobby.Players.Count];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                Player p = lobby.Players[i];

                colors[i] = p?.Data != null
                            && p.Data.TryGetValue(KeySuitColor, out PlayerDataObject value)
                            && int.TryParse(value.Value, NumberStyles.Integer,
                                            CultureInfo.InvariantCulture, out int parsed)
                    ? SuitPalette.Clamp(parsed)
                    : 0;
            }

            return colors;
        }

        /// <summary>
        /// Which row of the roster is us, or -1 when that cannot be answered.
        ///
        /// Needed because the cycler belongs under one specific figure. Keyed on the
        /// authentication service's player id, which is what <c>IsHost</c> already compares against
        /// — the alternative, matching on name, breaks the moment two friends are both called Pilot.
        /// </summary>
        public static int SlotOf(Lobby lobby, string localPlayerId)
        {
            if (lobby?.Players == null || string.IsNullOrEmpty(localPlayerId)) return -1;

            for (int i = 0; i < lobby.Players.Count; i++)
                if (lobby.Players[i] != null && lobby.Players[i].Id == localPlayerId)
                    return i;

            return -1;
        }

        /// <summary>Which row of the roster is the host, or -1. Marked in the rank with an underline.</summary>
        public static int HostSlot(Lobby lobby)
        {
            if (lobby?.Players == null || string.IsNullOrEmpty(lobby.HostId)) return -1;

            for (int i = 0; i < lobby.Players.Count; i++)
                if (lobby.Players[i] != null && lobby.Players[i].Id == lobby.HostId)
                    return i;

            return -1;
        }
    }
}
