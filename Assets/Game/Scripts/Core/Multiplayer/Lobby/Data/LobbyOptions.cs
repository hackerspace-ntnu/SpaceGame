using System.Collections.Generic;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The option objects <see cref="LobbySession"/> hands to the Lobby service.
    ///
    /// Pure and static so they can be tested without a live service, because this is where the
    /// bugs that made the lobby unusable actually lived — a relay code written a moment too late,
    /// and a lock that made joining a running session impossible.
    /// </summary>
    public static class LobbyOptions
    {
        /// <summary>
        /// The options a lobby is created with.
        ///
        /// <para>
        /// The relay code goes in here rather than into a follow-up UpdateLobbyAsync: a client
        /// polling in the gap between the two saw a lobby with no join code and read straight past
        /// the missing key.
        /// </para>
        ///
        /// <para>
        /// The mode — and, for a VS lobby, the team rules — are stamped here for the identical
        /// reason. A lobby briefly missing <see cref="LobbyKeys.Mode"/> would be read as story by
        /// <see cref="LobbyTeams.IsVersus"/> (see that method for why absent means story), which
        /// would flash a VS lobby into the story browser for whichever poll landed in the gap.
        /// </para>
        /// </summary>
        public static CreateLobbyOptions Create(bool isPrivate, string relayJoinCode, string playerName,
            int suitColor, in VersusSetup versus)
        {
            var data = new Dictionary<string, DataObject>
            {
                { LobbyKeys.RelayJoinCode, Member(relayJoinCode) },
                { LobbyKeys.GameState, Public(LobbyKeys.StateWaiting) },
                { LobbyKeys.Mode, Public(versus.IsVersus ? LobbyKeys.ModeVersus : LobbyKeys.ModeStory) }
            };

            if (versus.IsVersus)
            {
                data[LobbyKeys.TeamCount] = Member(LobbyData.Invariant(versus.TeamCount));
                data[LobbyKeys.TeamSize] = Member(LobbyData.Invariant(versus.TeamSize));
            }

            return new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = LocalPlayer(playerName, suitColor),
                Data = data
            };
        }

        /// <summary>
        /// The options that mark a lobby as playing.
        ///
        /// Deliberately does NOT set IsLocked. Locking here is what made joining a session already
        /// in progress impossible — and the host is usually alone when the first friend tries.
        /// </summary>
        public static UpdateLobbyOptions BeginGame() => new()
        {
            Data = new Dictionary<string, DataObject>
            {
                { LobbyKeys.GameState, Public(LobbyKeys.StateInGame) }
            }
        };

        /// <summary>
        /// The options that change a live lobby's privacy.
        ///
        /// Privacy is set after the lobby exists because the host is never asked for it before: the
        /// session is created the moment the lobby page opens, named after the world they already
        /// chose. Private here means delisted, nothing more — the lobby stays reachable by its code,
        /// which is the whole point. A host turns this on to stop strangers arriving from the
        /// browser, not to shut out the people they sent the code to.
        /// </summary>
        public static UpdateLobbyOptions Privacy(bool isPrivate) => new()
        {
            IsPrivate = isPrivate
        };

        /// <summary>
        /// The options that retune a live VS lobby's team rules: how many teams, how big.
        ///
        /// <c>MaxPlayers</c> follows <see cref="VersusRules.Seats"/> so the lobby never advertises
        /// more seats than the rules it is showing allow — a joiner reading "3/8" from the browser
        /// has to be able to trust that an eighth seat actually exists. Values arrive already
        /// checked: the caller routes every change through <see cref="VersusRules"/> before this is
        /// built, because that is what refuses a change that would evict somebody already standing
        /// in a team about to shrink — this method has no roster to check that against.
        /// </summary>
        public static UpdateLobbyOptions TeamRules(int teamCount, int teamSize) => new()
        {
            MaxPlayers = VersusRules.Seats(teamCount, teamSize),
            Data = new Dictionary<string, DataObject>
            {
                { LobbyKeys.TeamCount, Member(LobbyData.Invariant(teamCount)) },
                { LobbyKeys.TeamSize, Member(LobbyData.Invariant(teamSize)) }
            }
        };

        /// <summary>
        /// The options that change this player's suit colour on a lobby they are already in.
        ///
        /// Only the colour is sent. Including the name would make every arrow press also rewrite the
        /// name, so a rename typed on another screen mid-lobby could be reverted by a colour change.
        /// </summary>
        public static UpdatePlayerOptions SuitColor(int suitColor) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.SuitColor, SuitColorData(suitColor) }
            }
        };

        /// <summary>
        /// The options that move this player onto a different team. Only the team key, for the
        /// reason <see cref="SuitColor"/> sends only the colour.
        /// </summary>
        public static UpdatePlayerOptions Team(int team) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.Team, PlayerMember(LobbyData.Invariant(team)) }
            }
        };

        /// <summary>
        /// The options that publish this player's opinion of their team's colour. See
        /// <see cref="TeamColorOpinion"/> for why this is player data with a stamp on it.
        /// </summary>
        public static UpdatePlayerOptions TeamColor(int swatch, long stampMs) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.TeamColor, PlayerMember(TeamColorOpinion.Encode(swatch, stampMs)) }
            }
        };

        /// <summary>The local player's own entry, as sent when creating or joining a lobby.</summary>
        public static Player LocalPlayer(string playerName, int suitColor) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { LobbyKeys.PlayerName, PlayerMember(playerName) },
                { LobbyKeys.SuitColor, SuitColorData(suitColor) }
            }
        };

        private static PlayerDataObject SuitColorData(int suitColor) =>
            PlayerMember(LobbyData.Invariant(SuitPalette.Clamp(suitColor)));

        private static DataObject Public(string value) => new(DataObject.VisibilityOptions.Public, value);

        private static DataObject Member(string value) => new(DataObject.VisibilityOptions.Member, value);

        private static PlayerDataObject PlayerMember(string value) =>
            new(PlayerDataObject.VisibilityOptions.Member, value);
    }
}
