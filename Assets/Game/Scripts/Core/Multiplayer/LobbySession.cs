using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Characters;

namespace SpaceGame.Core
{
    /// <summary>
    /// The one owner of lobby state, for as long as the application runs.
    ///
    /// It is separate from the menu because the menu is destroyed by the load into the world, and
    /// the session must not be. A lobby that stops being heartbeated is delisted after 30 seconds,
    /// so a session tied to the menu scene could never be joined once the host started playing —
    /// which is exactly the thing "start now, let friends in later" requires.
    ///
    /// Everything above this is disposable view. Nothing here touches UnityEngine.UI, and no method
    /// throws across the boundary: failures arrive as <see cref="Failed"/> with a message already
    /// fit to show a player.
    ///
    /// The option builders live in the other half of this partial class, where they can be tested
    /// without a live service.
    /// </summary>
    public partial class LobbySession : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        /// <summary>Lobby delists a lobby not heartbeated inside 30s. 15s leaves room for a hiccup.</summary>
        private const float HeartbeatInterval = 15f;

        /// <summary>Lobby's GET rate limit is one call per second per lobby; 2s stays clear of it.</summary>
        private const float PollInterval = 2f;

        /// <summary>
        /// The floor between two QueryLobbies calls, whoever asked for them.
        ///
        /// Lobby allows one query per second. The browser's automatic refresh already sits on that
        /// ceiling, so the Refresh button — which is a second query issued at a moment of the
        /// player's choosing, usually right in the middle of the automatic one's interval — is what
        /// pushes it over. 1.1s buys back the timer jitter that made it a coin toss.
        /// </summary>
        private const float QuerySpacing = 1.1f;

        /// <summary>When the last query was ISSUED. Rate limiters count arrivals, not completions.</summary>
        private float lastQueryAt = float.NegativeInfinity;

        private static LobbySession instance;

        /// <summary>
        /// The session, created on first use.
        ///
        /// Not placed in a scene: it has to outlive every scene, including the one that would have
        /// held it. Created lazily rather than from Bootstrap so entering LobbyMenu directly in the
        /// editor still works — the same reason NetworkBootstrap backfills its manager.
        /// </summary>
        public static LobbySession Instance
        {
            get
            {
                if (instance != null) return instance;

                var host = new GameObject(nameof(LobbySession));
                instance = host.AddComponent<LobbySession>();
                DontDestroyOnLoad(host);
                return instance;
            }
        }

        public LobbyState State { get; private set; } = LobbyState.Idle;

        /// <summary>The lobby this peer is in, or null. Refreshed by the poll.</summary>
        public Lobby Current { get; private set; }

        public bool IsHost =>
            Current != null
            && AuthenticationService.Instance.IsSignedIn
            && Current.HostId == AuthenticationService.Instance.PlayerId;

        /// <summary>
        /// Which row of the current lobby is us, or -1.
        ///
        /// The instance half of <see cref="SlotOf"/>: it needs the authentication service, which is
        /// exactly what the views are kept away from so they stay testable without one.
        /// </summary>
        public int LocalSlot =>
            Current != null && AuthenticationService.Instance.IsSignedIn
                ? SlotOf(Current, AuthenticationService.Instance.PlayerId)
                : -1;

        /// <summary>Raised whenever the roster, the code or the state moved. The view redraws from this.</summary>
        public event Action Changed;

        /// <summary>A message fit to show a player. Never an exception across this boundary.</summary>
        public event Action<string> Failed;

        private float heartbeatTimer;
        private float pollTimer;

        // Update() fires these on a timer, and a slow request would otherwise be reissued every
        // frame until it returned, tripping the rate limiter and burying the real response under a
        // pile of 429s.
        private bool heartbeatInFlight;
        private bool pollInFlight;

        /// <summary>
        /// One operation at a time. Double-clicking Create used to allocate two Relay servers and
        /// two lobbies, then orphan the second pair when the first finished last.
        /// </summary>
        private bool busy;

        /// <summary>Long enough to swallow a burst of arrow presses, short enough to feel immediate.</summary>
        private const float SuitColorDebounce = 0.75f;

        /// <summary>The colour waiting to be published, or -1 when there is nothing pending.</summary>
        private int pendingSuitColor = -1;

        private float suitColorTimer;
        private bool suitColorInFlight;

        /// <summary>
        /// Keeps <see cref="HandleClientDisconnect"/> attached to whichever NetworkManager is live.
        ///
        /// This used to be a single subscription in <see cref="Awake"/> guarded by a null check,
        /// which silently did nothing whenever the manager did not exist yet — and it often does
        /// not, because this object creates itself on first use while NetworkBootstrap only
        /// backfills a manager AfterSceneLoad. Losing that race cost the whole disconnect path with
        /// no error anywhere. See <see cref="DisconnectHook"/>.
        /// </summary>
        private DisconnectHook disconnects;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;

            disconnects = new DisconnectHook(HandleClientDisconnect);
            disconnects.Poll();
        }

        private void OnDestroy()
        {
            disconnects?.Detach();
        }

        private void Update()
        {
            // Re-checked every frame rather than assumed once: two null tests, and it is what makes
            // a manager that appears late — or is replaced between Play sessions — still reach us.
            disconnects?.Poll();

            Heartbeat();
            Poll();
            FlushSuitColor();
        }

        // ─────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────

        /// <summary>Signed in and ready for Relay/Lobby calls. Safe to await repeatedly.</summary>
        public async Task<bool> EnsureReadyAsync()
        {
            SessionResult services = await SessionLauncher.EnsureServicesAsync();
            if (!services.Success) Failed?.Invoke(services.Error);
            return services.Success;
        }

        /// <summary>
        /// Allocates a Relay server, then advertises it as a lobby.
        ///
        /// Relay first. If it fails there is no lobby to clean up — the reverse order created the
        /// lobby, then allocated Relay, and on an allocation failure left an orphan lobby
        /// advertised to everyone with a join code that led nowhere.
        /// </summary>
        public async Task<bool> CreateAsync(string lobbyName, bool isPrivate)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                SessionResult host = await SessionLauncher.HostRelayAsync(MaxPlayers);
                if (!host.Success) { Failed?.Invoke(host.Error); return false; }

                string name = string.IsNullOrWhiteSpace(lobbyName) ? $"{PlayerName}'s game" : lobbyName;

                Current = await LobbyService.Instance.CreateLobbyAsync(name, MaxPlayers,
                    BuildCreateOptions(isPrivate, host.JoinCode, PlayerName, SuitColor));

                State = LobbyState.InLobby;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Hosting '{Current.Name}' code={Current.LobbyCode} relay={host.JoinCode}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not create the lobby."));
                SessionLauncher.Shutdown();
                return false;
            }
            finally { busy = false; }
        }

        /// <summary>
        /// Public lobbies with room, newest first. Private ones are reachable only by code.
        ///
        /// <para>
        /// Returns <b>null</b> when the query failed and an empty list when it succeeded and found
        /// nothing. The two used to be the same answer, which was harmless while the list was only
        /// fetched when the player asked for it — but the browser now refreshes every second, and a
        /// failure indistinguishable from "no sessions" empties the screen on every hiccup and puts
        /// it back on the next one. The caller keeps what it has when this returns null.
        /// </para>
        /// </summary>
        public async Task<List<Lobby>> QueryAsync()
        {
            try
            {
                await SpaceQuery();

                QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 25,
                    Filters = new List<QueryFilter>
                    {
                        new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                    },
                    Order = new List<QueryOrder> { new(false, QueryOrder.FieldOptions.Created) }
                });

                return response.Results;
            }
            catch (Exception e) when (IsSdkErrorPathFailure(e))
            {
                // Not our null. The service refused the request and the SDK threw this dereferencing
                // an error body it never managed to parse — see IsSdkErrorPathFailure. Logged as a
                // warning rather than an exception because the stack points into the package and
                // says nothing about the actual refusal, and reported to the player as what it
                // almost certainly is: one query too many, seconds away from working again.
                Debug.LogWarning("[LobbySession] The lobby service refused the query and the SDK " +
                                 "threw on its own error path. Treating it as rate limiting.");

                Failed?.Invoke("Could not fetch the lobby list.\n(Too many requests — trying again shortly.)");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not fetch the lobby list."));
                return null;
            }
        }

        /// <summary>
        /// Holds a query back until <see cref="QuerySpacing"/> has passed since the last one.
        ///
        /// Enforced here rather than in the browser because it is the browser having two callers —
        /// its own timer and its Refresh button — that trips the limiter, and neither of them can
        /// see the other's request. Every query goes through this method, so the budget is spent in
        /// one place. Held rather than dropped: the player pressed a button, and a refusal to
        /// refresh is worse than a refresh that takes a moment.
        ///
        /// <para>
        /// Waits by yielding frames rather than with <c>Task.Delay</c>. The continuation has to come
        /// back on the main thread — the request under it is a UnityWebRequest and throws anywhere
        /// else — and yielding is the form that cannot be resumed on a pool thread. It also reads
        /// <see cref="Time.unscaledTime"/>, which is main-thread only.
        /// </para>
        /// </summary>
        private async Task SpaceQuery()
        {
            // Claimed before the first await, not after: two queries arriving in the same frame must
            // not both read the old stamp, both decide they are clear, and leave together.
            float sendAt = Mathf.Max(Time.unscaledTime, lastQueryAt + QuerySpacing);
            lastQueryAt = sendAt;

            while (Time.unscaledTime < sendAt) await Task.Yield();
        }

        public Task<bool> JoinByIdAsync(string lobbyId) => JoinAsync(
            () => LobbyService.Instance.JoinLobbyByIdAsync(lobbyId,
                new JoinLobbyByIdOptions { Player = BuildPlayer(PlayerName, SuitColor) }),
            "Could not join that lobby.");

        public Task<bool> JoinByCodeAsync(string lobbyCode)
        {
            string code = SessionLauncher.NormalizeJoinCode(lobbyCode);

            if (string.IsNullOrEmpty(code))
            {
                Failed?.Invoke("Enter a lobby code first.");
                return Task.FromResult(false);
            }

            return JoinAsync(
                () => LobbyService.Instance.JoinLobbyByCodeAsync(code,
                    new JoinLobbyByCodeOptions { Player = BuildPlayer(PlayerName, SuitColor) }),
                "Could not join with that code.");
        }

        public async Task LeaveAsync()
        {
            string lobbyId = Current?.Id;

            // Local state first, so the UI responds even if the service call fails or hangs.
            Forget();
            SessionLauncher.Shutdown();

            await RemoveSelfQuietly(lobbyId);
        }

        /// <summary>
        /// Hands the membership back on the way out of a session, with nobody waiting for it.
        ///
        /// <para>
        /// Leaving to the menu is synchronous and ends in a scene load, so it cannot await this: a
        /// player who pressed "Main Menu" would sit staring at a frozen world for as long as the
        /// Lobby service took to answer. Nor can it be skipped, which is what it used to be — the
        /// host's lobby then stayed listed until its 30-second heartbeat lapsed, and the membership
        /// survived as exactly the ghost <see cref="JoinWithConflictRecoveryAsync"/> exists to clean
        /// up. Anonymous auth hands back the same PlayerId every launch, so hosting again races your
        /// own stale membership and is refused with 409 <i>player is already a member of the
        /// lobby</i>.
        /// </para>
        ///
        /// <para>
        /// Firing a task and walking away is only safe because of what it is fired at. This object
        /// is <see cref="DontDestroyOnLoad"/> and outlives the scene load, and the request itself
        /// runs inside <see cref="RemoveSelfQuietly"/>, which is static and swallows its own
        /// failures — so nothing lands on a destroyed object and the awaited half cannot leave an
        /// unobserved exception on a task nobody holds. The try only has to cover the part that
        /// runs before the first await, which is <see cref="Forget"/> raising
        /// <see cref="Changed"/> at whatever is still subscribed.
        /// </para>
        ///
        /// <para>
        /// Reads the backing field rather than <see cref="Instance"/> on purpose: Instance is a lazy
        /// factory, and asking it here would create a DontDestroyOnLoad LobbySession purely to be
        /// told there is no lobby to leave — on every singleplayer exit.
        /// </para>
        /// </summary>
        public static void LeaveInBackground()
        {
            LobbySession session = instance;
            if (session == null) return;

            try
            {
                _ = session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Could not start leaving the lobby: {e.Message}");
            }
        }

        /// <summary>
        /// Marks the lobby as playing and moves everyone into the world.
        ///
        /// The lobby is NOT locked and the heartbeat keeps running, so it stays listed and anyone
        /// joining later is synchronised into the running world by Netcode. There is deliberately
        /// no "wait for everyone to connect" gate: the host may start alone, and a client still
        /// completing its handshake is pulled into whatever scene the host is in when it lands.
        /// </summary>
        public async Task<bool> BeginGameAsync(string sceneName)
        {
            if (!TryBegin()) return false;

            try
            {
                if (Current == null) { Failed?.Invoke("You are not in a lobby."); return false; }
                if (!IsHost) { Failed?.Invoke("Only the host can start the game!"); return false; }

                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsServer)
                {
                    Failed?.Invoke("The host is not running a server. Try recreating the lobby.");
                    return false;
                }

                Current = await LobbyService.Instance.UpdateLobbyAsync(Current.Id, BuildBeginGameOptions());
                State = LobbyState.InGame;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Starting '{sceneName}' for {manager.ConnectedClientsIds.Count} " +
                          "client(s). Lobby stays open for late joiners.");

                manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not start the game."));
                return false;
            }
            finally { busy = false; }
        }

        /// <summary>
        /// Turns the lobby private or public.
        ///
        /// Private delists it: it stops appearing in the browser and is reachable only by its code.
        /// That is the whole of it — the code still works, which is what makes this the control a
        /// host actually wants when they only meant to keep strangers out.
        ///
        /// Not routed through <see cref="TryBegin"/>. That guard exists to stop a double-click
        /// allocating two Relay servers; this allocates nothing, and blocking it would mean a host
        /// who toggles privacy while the roster is mid-poll gets silently ignored.
        /// </summary>
        public async Task<bool> SetPrivacyAsync(bool isPrivate)
        {
            try
            {
                if (Current == null) { Failed?.Invoke("You are not in a lobby."); return false; }
                if (!IsHost) { Failed?.Invoke("Only the host can change this."); return false; }

                Current = await LobbyService.Instance.UpdateLobbyAsync(Current.Id,
                    BuildPrivacyOptions(isPrivate));

                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not change the session's privacy."));

                // The screen renders from Current, so a failed update has to be announced or the
                // toggle keeps showing the state the host asked for rather than the one in force.
                Changed?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// Publishes the local player's suit colour to the lobby, coalescing bursts.
        ///
        /// <para>
        /// Nothing here paints anything — the local figure is repainted by the screen the instant the
        /// arrow is pressed, and this only tells everyone else. That split is what lets the cycler
        /// feel immediate while the service call is allowed to be slow.
        /// </para>
        ///
        /// <para>
        /// Debounced because Lobby rate-limits UpdatePlayer to five calls per five seconds per
        /// player, and stepping through fourteen swatches to see them all is fourteen presses in
        /// about two seconds. Without this, a player browsing the list trips the limiter and the
        /// colour they settle on is the one request that gets refused — leaving everyone else looking
        /// at whatever they happened to be on when the budget ran out.
        /// </para>
        ///
        /// <para>
        /// Not routed through <see cref="TryBegin"/>, for the same reason
        /// <see cref="SetPrivacyAsync"/> is not: that guard exists to stop a double-click allocating
        /// two Relay servers, and blocking here would silently drop the press.
        /// </para>
        /// </summary>
        public void PublishSuitColor(int suitColor)
        {
            pendingSuitColor = SuitPalette.Clamp(suitColor);
            suitColorTimer = SuitColorDebounce;
        }

        /// <summary>
        /// Sends the pending colour once the player has stopped pressing.
        ///
        /// Failures are logged, not raised: the local astronaut and the stored preference are
        /// already correct, so the only casualty is that other people see the previous colour until
        /// the next press — and a warning pinned over the roster for that would be worse than the
        /// problem.
        /// </summary>
        private async void FlushSuitColor()
        {
            if (pendingSuitColor < 0 || suitColorInFlight) return;

            suitColorTimer -= Time.deltaTime;
            if (suitColorTimer > 0f) return;

            if (Current == null || !AuthenticationService.Instance.IsSignedIn)
            {
                // Nothing to publish to. Dropped rather than held, or it would fire at whatever
                // lobby this peer joins next.
                pendingSuitColor = -1;
                return;
            }

            int sending = pendingSuitColor;
            pendingSuitColor = -1;
            suitColorInFlight = true;

            try
            {
                Current = await LobbyService.Instance.UpdatePlayerAsync(
                    Current.Id, AuthenticationService.Instance.PlayerId,
                    BuildSuitColorOptions(sending));

                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Could not publish suit colour {sending}: {e.Message}");
            }
            finally { suitColorInFlight = false; }
        }

        /// <summary>The names to show in the roster, in lobby order.</summary>
        public static string[] PlayerNames(Lobby lobby)
        {
            if (lobby?.Players == null) return Array.Empty<string>();

            var names = new string[lobby.Players.Count];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                Player p = lobby.Players[i];

                // Defensive on both the dictionary and the entry: a player object written by an
                // older build, or one still mid-join, may not carry the name key at all, and an
                // unguarded indexer threw KeyNotFoundException every poll and killed the roster.
                names[i] = p?.Data != null && p.Data.TryGetValue(KeyPlayerName, out PlayerDataObject value)
                    ? value.Value
                    : "Player";
            }

            return names;
        }

        /// <summary>True when this lobby's host is already playing, so a joiner skips the lobby screen.</summary>
        public static bool IsPlaying(Lobby lobby) =>
            lobby?.Data != null
            && lobby.Data.TryGetValue(KeyGameState, out DataObject state)
            && state.Value == StateInGame;

        // ─────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────

        /// <summary>The name other players see. One identity, shared with PlayerIdentity in-game.</summary>
        private static string PlayerName => GameSettings.PlayerName;

        /// <summary>The suit colour other players see. Same store PlayerIdentity publishes from.</summary>
        private static int SuitColor => GameSettings.SuitColorIndex;

        /// <summary>
        /// Joins, then connects to the Relay server the lobby advertises.
        ///
        /// Lobby membership is rolled back if the Relay connection fails. Otherwise a failed
        /// connection leaves a ghost occupying a slot in a lobby it is not in, which is how a
        /// four-player lobby ends up refusing a third player.
        ///
        /// The join itself goes through <see cref="JoinWithConflictRecoveryAsync"/>, which clears
        /// out any membership an earlier session failed to give back. See that method for why the
        /// SDK's own handling of that case is not enough.
        /// </summary>
        private async Task<bool> JoinAsync(Func<Task<Lobby>> join, string failureHeadline)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                Lobby lobby = await JoinWithConflictRecoveryAsync(join,
                    () => LobbyService.Instance.GetJoinedLobbiesAsync(),
                    lobbyId => LobbyService.Instance.RemovePlayerAsync(
                        lobbyId, AuthenticationService.Instance.PlayerId));
                if (lobby == null) { Failed?.Invoke("The lobby service returned nothing."); return false; }

                if (!lobby.Data.TryGetValue(KeyRelayJoinCode, out DataObject relayCode)
                    || string.IsNullOrEmpty(relayCode.Value))
                {
                    await RemoveSelfQuietly(lobby.Id);
                    Failed?.Invoke("That lobby has no Relay server attached. The host may still be setting it up.");
                    return false;
                }

                SessionResult connected = await SessionLauncher.JoinRelayAsync(relayCode.Value);
                if (!connected.Success)
                {
                    await RemoveSelfQuietly(lobby.Id);
                    Failed?.Invoke(connected.Error);
                    return false;
                }

                Current = lobby;
                State = IsPlaying(lobby) ? LobbyState.InGame : LobbyState.InLobby;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Joined '{lobby.Name}' ({State}).");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, failureHeadline));
                return false;
            }
            finally { busy = false; }
        }

        private async void Heartbeat()
        {
            if (Current == null || !IsHost || heartbeatInFlight) return;

            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer > 0f) return;

            heartbeatTimer = HeartbeatInterval;
            heartbeatInFlight = true;

            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(Current.Id);
            }
            catch (Exception e)
            {
                // Not surfaced to the player: a missed heartbeat only delists the lobby from
                // search, and the session itself keeps working. A warning here would fire
                // repeatedly over a flaky connection and bury messages that need acting on.
                Debug.LogWarning($"[LobbySession] Heartbeat failed: {e.Message}");
            }
            finally { heartbeatInFlight = false; }
        }

        private async void Poll()
        {
            if (Current == null || pollInFlight) return;

            pollTimer -= Time.deltaTime;
            if (pollTimer > 0f) return;

            pollTimer = PollInterval;
            pollInFlight = true;

            try
            {
                Lobby lobby = await LobbyService.Instance.GetLobbyAsync(Current.Id);

                // Leaving nulls Current while this request is in flight; writing the stale result
                // back would resurrect a lobby we have already left.
                if (Current == null) return;

                Current = lobby;
                Changed?.Invoke();
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                Failed?.Invoke("The host closed the lobby.");
                Forget();
                SessionLauncher.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Poll failed: {e.Message}");
            }
            finally { pollInFlight = false; }
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsHost) return;
            if (clientId != manager.LocalClientId) return;

            string reason = manager.DisconnectReason;
            Debug.Log($"[LobbySession] Disconnected. Reason: '{reason}'");

            // Read before Forget nulls it: the membership has to be handed back, not just dropped
            // locally. Forgetting alone left this player listed in a lobby they were no longer in,
            // and because anonymous auth reuses the same player id, the next attempt to join that
            // lobby was refused with "player is already a member of the lobby".
            string lobbyId = Current?.Id;

            // Any disconnect strands us, not just a clean host shutdown. Matching on the exact
            // reason string meant a host crash, a dropped connection or a Relay timeout left the
            // player sitting in a session that no longer existed.
            Failed?.Invoke(string.IsNullOrEmpty(reason) ? "Lost connection to the host." : reason);
            Forget();

            // Not awaited: this is a Netcode callback, and the UI has already been told. The
            // removal is best-effort by design — RemoveSelfQuietly never throws.
            _ = RemoveSelfQuietly(lobbyId);
        }

        /// <summary>
        /// Hands the lobby membership back on the way out.
        ///
        /// Best-effort and often too late — quitting tears down the request loop, and stopping play
        /// mode in the editor gives it no chance at all. It is sent anyway because when it does land
        /// it frees the slot immediately, and <see cref="JoinWithConflictRecoveryAsync"/> clears up
        /// whatever survives on the next join.
        /// </summary>
        private void OnApplicationQuit()
        {
            _ = RemoveSelfQuietly(Current?.Id);
        }

        private void Forget()
        {
            Current = null;
            State = LobbyState.Idle;
            Changed?.Invoke();
        }

        /// <summary>Best-effort removal. Never reports — callers are already handling a failure.</summary>
        private static async Task RemoveSelfQuietly(string lobbyId)
        {
            try
            {
                if (!string.IsNullOrEmpty(lobbyId) && AuthenticationService.Instance.IsSignedIn)
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Could not remove self from {lobbyId}: {e.Message}");
            }
        }

        private bool TryBegin()
        {
            if (busy)
            {
                Debug.Log("[LobbySession] Ignoring input — an operation is already running.");
                return false;
            }

            busy = true;
            return true;
        }

        /// <summary>
        /// A line the player can read, from whatever the service threw.
        ///
        /// The SDK's own error path is spelled out rather than quoted: it arrives as a bare
        /// NullReferenceException with a message about an object reference, which describes the
        /// package's bug instead of the refusal that caused it. See
        /// <see cref="IsSdkErrorPathFailure"/>.
        /// </summary>
        private static string Describe(Exception e, string headline) =>
            IsSdkErrorPathFailure(e)
                ? $"{headline}\n(The lobby service refused the request. Try again in a moment.)"
                : e is LobbyServiceException lobbyException
                    ? $"{headline}\n({lobbyException.Reason}: {lobbyException.Message})"
                    : $"{headline}\n({e.GetType().Name}: {e.Message})";
    }
}
