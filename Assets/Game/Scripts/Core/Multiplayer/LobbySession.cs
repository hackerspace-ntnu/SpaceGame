using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }

        private void Update()
        {
            Heartbeat();
            Poll();
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
        public async Task<bool> CreateAsync(string lobbyName, bool isPrivate, string password)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                SessionResult host = await SessionLauncher.HostRelayAsync(MaxPlayers);
                if (!host.Success) { Failed?.Invoke(host.Error); return false; }

                string name = string.IsNullOrWhiteSpace(lobbyName) ? $"{PlayerName}'s game" : lobbyName;

                Current = await LobbyService.Instance.CreateLobbyAsync(name, MaxPlayers,
                    BuildCreateOptions(isPrivate, password, host.JoinCode, PlayerName));

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

        /// <summary>Public lobbies with room, newest first. Private ones are reachable only by code.</summary>
        public async Task<List<Lobby>> QueryAsync()
        {
            try
            {
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
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not fetch the lobby list."));
                return new List<Lobby>();
            }
        }

        public Task<bool> JoinByIdAsync(string lobbyId) => JoinAsync(
            () => LobbyService.Instance.JoinLobbyByIdAsync(lobbyId,
                new JoinLobbyByIdOptions { Player = BuildPlayer(PlayerName) }),
            "Could not join that lobby.");

        public Task<bool> JoinByCodeAsync(string lobbyCode, string password = null)
        {
            string code = SessionLauncher.NormalizeJoinCode(lobbyCode);

            if (string.IsNullOrEmpty(code))
            {
                Failed?.Invoke("Enter a lobby code first.");
                return Task.FromResult(false);
            }

            return JoinAsync(
                () => LobbyService.Instance.JoinLobbyByCodeAsync(code, new JoinLobbyByCodeOptions
                {
                    Player = BuildPlayer(PlayerName),
                    Password = NullIfBlank(password)
                }),
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

        /// <summary>
        /// Joins, then connects to the Relay server the lobby advertises.
        ///
        /// Lobby membership is rolled back if the Relay connection fails. Otherwise a failed
        /// connection leaves a ghost occupying a slot in a lobby it is not in, which is how a
        /// four-player lobby ends up refusing a third player.
        /// </summary>
        private async Task<bool> JoinAsync(Func<Task<Lobby>> join, string failureHeadline)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                Lobby lobby = await join();
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

            // Any disconnect strands us, not just a clean host shutdown. Matching on the exact
            // reason string meant a host crash, a dropped connection or a Relay timeout left the
            // player sitting in a session that no longer existed.
            Failed?.Invoke(string.IsNullOrEmpty(reason) ? "Lost connection to the host." : reason);
            Forget();
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

        private static string Describe(Exception e, string headline) =>
            e is LobbyServiceException lobbyException
                ? $"{headline}\n({lobbyException.Reason}: {lobbyException.Message})"
                : $"{headline}\n({e.GetType().Name}: {e.Message})";
    }
}
