using System.Collections.Generic;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

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
            string playerName) => new()
        {
            IsPrivate = isPrivate,
            Player = BuildPlayer(playerName),
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

        /// <summary>"3/4" — taken over total. Lobby reports FREE slots, which reads inverted.</summary>
        public static string DescribeOccupancy(int maxPlayers, int availableSlots) =>
            $"{maxPlayers - availableSlots}/{maxPlayers}";

        public static Player BuildPlayer(string playerName) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyPlayerName, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
            }
        };
    }
}
