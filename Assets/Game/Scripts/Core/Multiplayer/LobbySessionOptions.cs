using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

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
        /// Which kind of lobby this is: <see cref="ModeStory"/> or <see cref="ModeVersus"/>.
        ///
        /// Public, not Member, for the same reason <see cref="KeyGameState"/> is: the browser labels
        /// rows the player has not joined, and a VS joiner's list must not offer story lobbies (or a
        /// story joiner's list VS ones) — that filter has to run before anyone has joined anything,
        /// which means before the key could possibly be Member-visible to them.
        /// </summary>
        public const string KeyMode = "Mode";

        public const string ModeStory = "story";
        public const string ModeVersus = "versus";

        /// <summary>How many teams this VS lobby is split into. Member: meaningless until you are in.</summary>
        public const string KeyTeamCount = "TeamCount";

        /// <summary>How many seats each team of this VS lobby holds. Member.</summary>
        public const string KeyTeamSize = "TeamSize";

        /// <summary>Which team this player stands on. PLAYER data, Member.</summary>
        public const string KeyTeam = "Team";

        /// <summary>
        /// This player's opinion of their team's colour, PLAYER data, Member, shaped
        /// <c>"swatch:stampMs"</c> — see <see cref="EncodeTeamColor"/>.
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
        /// <see cref="TeamColorsOf"/>, as the highest-stamped opinion among the players standing on
        /// that team. Last writer wins, which is exactly what pressing the colour cycler means: the
        /// stamp exists so that two members racing the arrow keys converge on whichever press landed
        /// last, on every peer, instead of disagreeing forever.
        /// </para>
        /// </summary>
        public const string KeyTeamColor = "TeamColor";

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
        /// reason. A lobby briefly missing <see cref="KeyMode"/> would be read as story by
        /// <see cref="IsVersus"/> (see that method's doc on why absent means story), which would
        /// flash a VS lobby into the story browser for whichever poll landed in the gap.
        /// </para>
        /// </summary>
        public static CreateLobbyOptions BuildCreateOptions(bool isPrivate, string relayJoinCode,
            string playerName, int suitColor, in VersusSetup versus)
        {
            var data = new Dictionary<string, DataObject>
            {
                { KeyRelayJoinCode, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) },

                // Public, not Member: the browser labels rows the player has not joined.
                { KeyGameState, new DataObject(DataObject.VisibilityOptions.Public, StateWaiting) },
                { KeyMode, new DataObject(DataObject.VisibilityOptions.Public,
                    versus.IsVersus ? ModeVersus : ModeStory) }
            };

            if (versus.IsVersus)
            {
                data[KeyTeamCount] = new DataObject(DataObject.VisibilityOptions.Member,
                    versus.TeamCount.ToString(CultureInfo.InvariantCulture));
                data[KeyTeamSize] = new DataObject(DataObject.VisibilityOptions.Member,
                    versus.TeamSize.ToString(CultureInfo.InvariantCulture));
            }

            return new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = BuildPlayer(playerName, suitColor),
                Data = data
            };
        }

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
        /// The options that retune a live VS lobby's team rules: how many teams, how big.
        ///
        /// <c>MaxPlayers</c> follows <see cref="VersusRules.Seats"/> so the lobby never advertises
        /// more seats than the rules it is showing allow — a joiner reading "3/8" from the browser
        /// has to be able to trust that an eighth seat actually exists. Values arrive already
        /// clamped: the caller routes every change through <see cref="VersusRules"/> and
        /// <c>CanSetTeamSize</c> / <c>CanSetTeamCount</c> before this is ever built, because those
        /// are what refuse a change that would evict somebody already standing in a team about to
        /// shrink — this method has no roster to check that against.
        /// </summary>
        public static UpdateLobbyOptions BuildTeamRulesOptions(int teamCount, int teamSize) => new()
        {
            MaxPlayers = VersusRules.Seats(teamCount, teamSize),
            Data = new Dictionary<string, DataObject>
            {
                { KeyTeamCount, new DataObject(DataObject.VisibilityOptions.Member,
                    teamCount.ToString(CultureInfo.InvariantCulture)) },
                { KeyTeamSize, new DataObject(DataObject.VisibilityOptions.Member,
                    teamSize.ToString(CultureInfo.InvariantCulture)) }
            }
        };

        /// <summary>
        /// The options that move this player onto a different team.
        ///
        /// Only the team key. Including the colour would make every team switch also rewrite it —
        /// the same reasoning as <see cref="BuildSuitColorOptions"/>: a colour cycled on another
        /// screen mid-lobby must not be silently reverted by an unrelated team change.
        /// </summary>
        public static UpdatePlayerOptions BuildTeamOptions(int team) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyTeam, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                    team.ToString(CultureInfo.InvariantCulture)) }
            }
        };

        /// <summary>
        /// The options that publish this player's opinion of their team's colour. See
        /// <see cref="KeyTeamColor"/> for why this is player data with a stamp on it rather than a
        /// shared lobby-data table.
        /// </summary>
        public static UpdatePlayerOptions BuildTeamColorOptions(int swatch, long stampMs) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyTeamColor, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member,
                    EncodeTeamColor(swatch, stampMs)) }
            }
        };

        /// <summary>Packs a swatch and the moment it was chosen into one player-data string.</summary>
        public static string EncodeTeamColor(int swatch, long stampMs) =>
            swatch.ToString(CultureInfo.InvariantCulture) + ":" +
            stampMs.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Reads back a value <see cref="EncodeTeamColor"/> wrote. Answers false rather than
        /// throwing on anything unrecognised — a peer on an older build, a value truncated by a
        /// service hiccup, or plain garbage must not take the roster down with it.
        /// </summary>
        public static bool TryDecodeTeamColor(string value, out int swatch, out long stampMs)
        {
            swatch = 0;
            stampMs = 0;

            if (string.IsNullOrEmpty(value)) return false;

            int separator = value.IndexOf(':');
            if (separator < 0) return false;

            return int.TryParse(value.Substring(0, separator), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out swatch)
                && long.TryParse(value.Substring(separator + 1), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out stampMs);
        }

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

        /// <summary>
        /// Whether an exception is the Lobby SDK falling over on its own error path, rather than a
        /// fault on this side of the boundary.
        ///
        /// <para>
        /// <c>WrappedLobbyService.TryCatchRequest</c> answers an <c>HttpException&lt;ErrorStatus&gt;</c>
        /// with <c>he.ActualError.Code</c>, and <c>ActualError</c> is whatever
        /// <c>ResponseHandler.TryDeserializeResponse</c> made of the response body — which is
        /// <b>null</b> whenever the service answers an HTTP error with an empty or unparseable one.
        /// Its rate limiter does exactly that. So a refused request does not arrive as
        /// <see cref="LobbyServiceException"/> with a reason on it; it arrives as a raw
        /// <see cref="NullReferenceException"/> thrown from inside the package, and the status code
        /// that would have said <i>which</i> refusal it was is destroyed by the same dereference.
        /// </para>
        ///
        /// <para>
        /// Matched on the stack rather than on the type alone, so a genuine null bug in our own
        /// code is still reported as one instead of being excused as a busy service.
        /// </para>
        /// </summary>
        public static bool IsSdkErrorPathFailure(Exception e) =>
            e is NullReferenceException && IsLobbyPackageStack(e.StackTrace);

        /// <summary>
        /// Whether these frames come from inside the Lobby package.
        ///
        /// Split out because <see cref="Exception.StackTrace"/> is filled in by the runtime as an
        /// exception is thrown and cannot be set, so this is the half a test can reach — and
        /// because the check is the load-bearing part: matching on the type alone would excuse
        /// every null in our own code as a busy service.
        /// </summary>
        public static bool IsLobbyPackageStack(string stackTrace) =>
            stackTrace != null && stackTrace.Contains("Unity.Services.Lobbies");

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

        // ─────────────────────────────────────────────
        //  VS: mode, team rules, roster
        // ─────────────────────────────────────────────

        /// <summary>
        /// Whether this lobby is a VS match rather than the story campaign.
        ///
        /// A lobby with no mode key at all reads as story. That default matters, not just for
        /// symmetry with <see cref="ModeStory"/>: a lobby created by a build that shipped before VS
        /// existed carries no <see cref="KeyMode"/> key at all, and it must keep reading as the
        /// story lobby it always was rather than suddenly being offered as a match.
        /// </summary>
        public static bool IsVersus(Lobby lobby) =>
            lobby?.Data != null
            && lobby.Data.TryGetValue(KeyMode, out DataObject mode)
            && mode.Value == ModeVersus;

        /// <summary>
        /// How many teams this lobby is split into. Falls back to
        /// <see cref="VersusRules.DefaultTeams"/> when the key is absent or unparseable — a story
        /// lobby, or one from a build that predates VS — and is always clamped, so a value written
        /// by a peer with looser limits still lands somewhere this build's rules recognise.
        /// </summary>
        public static int TeamCountOf(Lobby lobby)
        {
            ReadTeamRules(lobby, out int teamCount, out _);
            return teamCount;
        }

        /// <summary>How big each team is. See <see cref="TeamCountOf"/> — the same fallback and clamp.</summary>
        public static int TeamSizeOf(Lobby lobby)
        {
            ReadTeamRules(lobby, out _, out int teamSize);
            return teamSize;
        }

        /// <summary>
        /// Reads and clamps both team-rule keys together, in the order <see cref="VersusRules"/>'s
        /// docs pin as its pairing contract: <see cref="VersusRules.ClampTeams"/> first, against the
        /// raw size, then <see cref="VersusRules.ClampTeamSize"/> fed that already-clamped count.
        /// Clamping either axis alone, against an unclamped partner, is how a host ends up with
        /// numbers whose product is nowhere near <see cref="VersusRules.MaxSeats"/>.
        /// </summary>
        private static void ReadTeamRules(Lobby lobby, out int teamCount, out int teamSize)
        {
            int rawTeams = ReadInt(lobby?.Data, KeyTeamCount, VersusRules.DefaultTeams);
            int rawSize = ReadInt(lobby?.Data, KeyTeamSize, VersusRules.DefaultTeamSize);

            teamCount = VersusRules.ClampTeams(rawTeams, rawSize);
            teamSize = VersusRules.ClampTeamSize(rawSize, teamCount);
        }

        /// <summary>
        /// Which team each player stands on, in lobby order and index-aligned with
        /// <see cref="PlayerNames"/>. A player with no team key is on team 0.
        ///
        /// A team index this lobby's own rules do not recognise — a peer from a build that allows
        /// more teams than this one's <see cref="TeamCountOf"/> — is folded back into range with
        /// <see cref="FoldTeam"/> rather than dropped or clamped to 0. See that method for why.
        /// </summary>
        public static int[] Teams(Lobby lobby) => Teams(lobby, TeamCountOf(lobby));

        /// <summary>
        /// <see cref="Teams(Lobby)"/>, taking a team count already computed by the caller instead
        /// of re-reading and re-clamping the rule keys — see <see cref="Snapshot"/>, which is the
        /// caller that actually needs this.
        /// </summary>
        private static int[] Teams(Lobby lobby, int teamCount)
        {
            if (lobby?.Players == null) return Array.Empty<int>();

            var teams = new int[lobby.Players.Count];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                Player p = lobby.Players[i];

                int raw = p?.Data != null
                          && p.Data.TryGetValue(KeyTeam, out PlayerDataObject value)
                          && int.TryParse(value.Value, NumberStyles.Integer,
                                          CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : 0;

                teams[i] = FoldTeam(raw, teamCount);
            }

            return teams;
        }

        /// <summary>
        /// Wraps a team index that does not belong to this lobby's rules back into
        /// <c>[0, teamCount)</c>, by modulus rather than by clamping to 0.
        ///
        /// <para>
        /// Clamping every out-of-range index to 0 would mean every peer running a build with a
        /// bigger palette of teams — team 7 and team 70 alike — lands on the SAME team, piling
        /// every stray player onto team one specifically and nowhere else. Wrapping instead spreads
        /// them out (team 7 folds to team 1 of 2, team 8 folds back to team 0), which is no less
        /// arbitrary but does not single out one team as the dumping ground.
        /// </para>
        ///
        /// <para>
        /// Either way the result is always inside <c>[0, teamCount)</c>, which is what
        /// <see cref="Occupancy"/> and <see cref="TeamColorsOf"/> both depend on to size their
        /// per-team arrays and index into them without a bounds check of their own — several raw
        /// indices folding onto the same team is perfectly safe downstream: occupancy simply counts
        /// them together, and the colour rule still just looks for the highest stamp among whoever
        /// landed there.
        /// </para>
        /// </summary>
        private static int FoldTeam(int team, int teamCount)
        {
            if (teamCount <= 0) return 0;

            int folded = team % teamCount;
            return folded < 0 ? folded + teamCount : folded;
        }

        /// <summary>Heads standing on each team, index-aligned with team number.</summary>
        public static int[] Occupancy(Lobby lobby) => Occupancy(Teams(lobby), TeamCountOf(lobby));

        /// <summary>
        /// <see cref="Occupancy(Lobby)"/>, taking a team assignment and team count already computed
        /// by the caller instead of recomputing them from the lobby — see <see cref="Snapshot"/>,
        /// which is the caller that actually needs this.
        /// </summary>
        private static int[] Occupancy(int[] teams, int teamCount)
        {
            var occupancy = new int[teamCount];

            foreach (int team in teams)
                occupancy[team]++;

            return occupancy;
        }

        /// <summary>
        /// One swatch per team: the highest-stamped opinion among that team's members, else
        /// <see cref="TeamColorRules.DefaultColors"/>. Always exactly <see cref="TeamCountOf"/>
        /// entries long, even for a lobby with nobody in it at all.
        ///
        /// Ties — two players on the same team publishing the same stamp — go to whichever comes
        /// first in lobby order. That has to be resolved the same way on every peer or two machines
        /// paint the same team's rank in different colours; it is why the comparison below is
        /// strict (<c>&gt;</c>, never <c>&gt;=</c>) — a later, equally-stamped opinion never
        /// displaces the earlier one that already claimed the team.
        /// </summary>
        public static int[] TeamColorsOf(Lobby lobby, int swatchCount) =>
            TeamColorsOf(lobby, Teams(lobby), TeamCountOf(lobby), swatchCount);

        /// <summary>
        /// <see cref="TeamColorsOf(Lobby, int)"/>, taking a team assignment and team count already
        /// computed by the caller instead of recomputing them from the lobby — see
        /// <see cref="Snapshot"/>, which is the caller that actually needs this.
        /// </summary>
        private static int[] TeamColorsOf(Lobby lobby, int[] teams, int teamCount, int swatchCount)
        {
            int[] colors = TeamColorRules.DefaultColors(teamCount, swatchCount);

            if (lobby?.Players == null) return colors;

            var bestStamp = new long[teamCount];
            var hasOpinion = new bool[teamCount];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                Player p = lobby.Players[i];

                if (p?.Data == null
                    || !p.Data.TryGetValue(KeyTeamColor, out PlayerDataObject value)
                    || !TryDecodeTeamColor(value.Value, out int swatch, out long stampMs))
                    continue;

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

        private static int ClampSwatch(int swatch, int swatchCount)
        {
            if (swatchCount <= 0) return 0;
            if (swatch < 0) return 0;
            return swatch >= swatchCount ? swatchCount - 1 : swatch;
        }

        /// <summary>
        /// Everything a roster view needs, taken off this lobby. A null lobby produces a safe empty
        /// snapshot rather than throwing — see <see cref="RosterSnapshot"/>.
        ///
        /// <para>
        /// The team rules are read once, via <see cref="ReadTeamRules"/> rather than the public
        /// <see cref="TeamCountOf"/> / <see cref="TeamSizeOf"/> wrappers, and the roster is walked
        /// once, via the private <see cref="Teams(Lobby, int)"/> overload — then both results are
        /// threaded into <see cref="Occupancy(int[], int)"/> and
        /// <see cref="TeamColorsOf(Lobby, int[], int, int)"/> instead of letting each of those
        /// recompute the same team assignment and rule counts from <paramref name="lobby"/> a
        /// second and third time. A roster of any size this game actually seats would never notice
        /// the difference, but the doc for this method previously claimed "one pass" while doing
        /// seven redundant rereads of the rule keys and three redundant walks of the roster — so
        /// this paragraph describes what the method below actually does, not what would have been
        /// convenient to claim.
        /// </para>
        ///
        /// <para>
        /// Takes <paramref name="localSlot"/> as a parameter but computes the host slot itself via
        /// <see cref="HostSlot"/>, rather than also taking it as one. The two are not symmetric:
        /// <c>LocalSlot</c> genuinely cannot be computed from a <c>Lobby</c> alone — it needs the
        /// authentication service's player id, which is exactly what this pure half of the class is
        /// kept away from — so the instance half has to hand it in. <c>HostSlot</c> has no such
        /// dependency; it is pure already. Accepting it as a parameter anyway would only invite a
        /// caller to pass one computed from a different, stale <c>Lobby</c> than the one being
        /// snapshotted, which a plain method call cannot go stale by construction.
        /// </para>
        /// </summary>
        public static RosterSnapshot Snapshot(Lobby lobby, int localSlot, int swatchCount)
        {
            ReadTeamRules(lobby, out int teamCount, out int teamSize);
            int[] teams = Teams(lobby, teamCount);

            return new RosterSnapshot(
                PlayerNames(lobby),
                SuitColors(lobby),
                teams,
                TeamColorsOf(lobby, teams, teamCount, swatchCount),
                Occupancy(teams, teamCount),
                teamCount,
                teamSize,
                localSlot,
                HostSlot(lobby),
                IsVersus(lobby));
        }

        private static int ReadInt(Dictionary<string, DataObject> data, string key, int fallback) =>
            data != null
            && data.TryGetValue(key, out DataObject value)
            && int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
    }
}
