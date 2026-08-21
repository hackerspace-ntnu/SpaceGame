using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Core
{
    /// <summary>Where a session is in its life. The view renders from this and nothing else.</summary>
    public enum LobbyState { Idle, InLobby, InGame }

    /// <summary>
    /// The pure half of <see cref="LobbySession"/>: the option objects handed to the Lobby service,
    /// and the retry policy wrapped around joining.
    ///
    /// Separated from the MonoBehaviour half so it can be tested without a live service, because
    /// this is where the bugs that made the lobby unusable actually lived — a relay code written a
    /// moment too late, a lock that made joining a running session impossible, and a join refused
    /// outright because a dead session's membership was never given back.
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

        /// <summary>
        /// Joins, and if the service refuses because this player is already listed somewhere,
        /// releases every membership they still hold and tries once more.
        ///
        /// <para>
        /// A lobby membership outlives the session that created it. It is given up in exactly one
        /// place — pressing Leave — so a host that crashed, a Relay connection that timed out, or a
        /// process that was killed all leave this player's id sitting in a lobby they are no longer
        /// in. Anonymous authentication hands back the SAME player id on the next launch, so those
        /// ghosts are still ours and they accumulate; joining a lobby one of them occupies is
        /// answered with 409 <i>player is already a member of the lobby</i>.
        /// </para>
        ///
        /// <para>
        /// The Lobby SDK has its own 409 recovery and it cannot be leaned on. Joining by id, it
        /// gives up unless <c>GetJoinedLobbies</c> returns EXACTLY one lobby — and it then joins
        /// that lobby rather than the one that was asked for. Two ghosts and it rethrows the raw
        /// HttpException, which is exactly what a couple of playtests leave behind.
        /// </para>
        ///
        /// <para>
        /// Retried once and no more. A conflict that outlives the sweep is a refusal the player
        /// needs to read, not something to keep hammering a rate limiter over.
        /// </para>
        ///
        /// The service calls arrive as delegates so this can be tested without one.
        /// </summary>
        /// <param name="join">Performs the join. Called twice at most.</param>
        /// <param name="joinedLobbies">Ids of every lobby this player is still a member of.</param>
        /// <param name="leave">Removes this player from one lobby.</param>
        public static async Task<Lobby> JoinWithConflictRecoveryAsync(
            Func<Task<Lobby>> join,
            Func<Task<List<string>>> joinedLobbies,
            Func<string, Task> leave)
        {
            try
            {
                return await join();
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyConflict)
            {
                List<string> stale = await joinedLobbies();

                // Nothing to release means nothing about a second attempt would differ, so the
                // service's own reason is left to reach the player rather than spent on a retry.
                if (stale == null || stale.Count == 0) throw;

                Debug.LogWarning($"[LobbySession] Join refused — this player is still a member of " +
                                 $"{stale.Count} lobby/lobbies. Releasing them and retrying.");

                int released = 0;

                foreach (string lobbyId in stale)
                {
                    // Removals run against a rate limiter, and one refusal must not strand the rest
                    // of the sweep: a player with two ghosts would stay locked out by whichever one
                    // happened to answer first.
                    try
                    {
                        await leave(lobbyId);
                        released++;
                    }
                    catch (Exception removal)
                    {
                        Debug.LogWarning($"[LobbySession] Could not release {lobbyId}: {removal.Message}");
                    }
                }

                if (released == 0) throw;

                return await join();
            }
        }

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
