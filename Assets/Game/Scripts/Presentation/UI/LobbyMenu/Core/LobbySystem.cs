using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;
using SpaceGame.Presentation;

/// <summary>
/// The lobby menu's controller: create, list, join, leave, and hand off into the game.
///
/// Deliberately in the global namespace and named exactly as before — the LobbyMenu scene binds six
/// button events to these method names by string, and UnityEvent resolves them by string at runtime
/// with no compile-time link. Renaming or namespacing any of them turns its button into a silent
/// no-op with nothing logged.
///
/// This is a MonoBehaviour, not a NetworkBehaviour. Deriving from NetworkBehaviour required a
/// NetworkObject on this GameObject, which sat in a scene loaded by plain SceneManager rather than
/// Netcode's. Starting a host from that scene made Netcode try to spawn an in-scene object the
/// joining client had no matching copy of, which is a synchronization failure, not a warning.
/// Nothing here ever needed to replicate: it drives a menu and then hands off.
///
/// Connection work lives in <see cref="SessionLauncher"/>. This class only decides WHEN to connect.
/// </summary>
public class LobbySystem : MonoBehaviour
{
    [SerializeField] SceneReference gameScene;

    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";
    private const string KEY_PLAYER_NAME = "PlayerName";

    /// <summary>Lobby requires a heartbeat inside 30s or it delists. 15s leaves room for a hiccup.</summary>
    private const float HeartbeatInterval = 15f;

    /// <summary>Lobby's GET rate limit is one call per second per lobby; 2s stays clear of it.</summary>
    private const float PollInterval = 2f;

    /// <summary>
    /// How long Start waits for every lobby member to also finish the Netcode handshake.
    ///
    /// Joining is two independent handshakes — the Lobby service roster and the Relay/Netcode
    /// connection — and they settle at different times. The old code compared the two counts once
    /// and refused to start on any mismatch, which rejected perfectly good sessions because the
    /// roster is only re-read every couple of seconds. Waiting for them to converge is the fix.
    /// </summary>
    private const float StartConvergenceTimeout = 10f;

    public static int maxPlayers = 4;

    /// <summary>
    /// The scene the lobby hands off to. Exposed so the Relay-free direct path loads the same one
    /// rather than keeping a second copy of the name that can drift.
    /// </summary>
    public string GameSceneName => gameScene != null ? gameScene.SceneName : null;

    private Lobby hostLobby;
    private Lobby joinedLobby;

    private float heartBeatTimer;
    private float lobbyUpdateTimer;

    private LobbyListSystem lobbyList;
    private LobbyWarningSystem warningSystem;
    private string playerName;

    // In-flight guards. Update() fires these on a timer, and a slow request would otherwise be
    // reissued every frame until it returned, tripping the service's rate limiter and burying the
    // real response under a pile of 429s.
    private bool heartbeatInFlight;
    private bool pollInFlight;
    private bool busy;

    /// <summary>
    /// The code the player last tried, so a password prompt can retry the same lobby.
    ///
    /// A private lobby is excluded from query results, so it can only ever be reached by code —
    /// there is no listed entry to click and no id to look up. The previous implementation passed
    /// the literal string "lobbyId" to JoinLobbyByIdAsync, so joining a password-protected lobby
    /// could not succeed under any circumstances.
    /// </summary>
    private string lastAttemptedJoinCode;

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        // Before any await. These were resolved after `await UnityServices.InitializeAsync()`, so a
        // failed initialisation left warningSystem null — and every error path in this class
        // reports through it, so the one situation that most needed a message on screen instead
        // threw a NullReferenceException inside an async void and vanished.
        lobbyList = GetComponent<LobbyListSystem>();
        warningSystem = GetComponent<LobbyWarningSystem>();

        playerName = "Player" + UnityEngine.Random.Range(10, 99);
    }

    private async void Start()
    {
        try
        {
            SessionResult services = await SessionLauncher.EnsureServicesAsync();
            if (!services.Success)
            {
                Warn(services.Error);
                return;
            }

            Debug.Log($"[Lobby] Signed in as {AuthenticationService.Instance.PlayerId}");

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
                NetworkBootstrap.LogRegisteredPrefabCount();
            }

            listLobbies();
        }
        catch (Exception e)
        {
            // Start is `async void` because Unity's message loop requires it. That makes this catch
            // mandatory rather than defensive: an exception escaping here is swallowed by the
            // runtime with no stack trace attached to anything the player or developer can see.
            Debug.LogException(e);
            Warn($"Lobby failed to start.\n({e.GetType().Name}: {e.Message})");
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
    }

    private void Update()
    {
        HandleLobbyHeartBeat();
        HandleLobbyPollForUpdates();
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null) return;
        if (networkManager.IsHost) return;
        if (clientId != networkManager.LocalClientId) return;

        string reason = networkManager.DisconnectReason;
        Debug.Log($"[Lobby] Disconnected. Reason: '{reason}'");

        // Any disconnect strands us, not just a clean host shutdown. Matching on the exact reason
        // string meant a host crash, a dropped connection or a Relay timeout left the player
        // sitting in a lobby screen for a session that no longer existed.
        Warn(string.IsNullOrEmpty(reason) ? "Lost connection to the host." : reason);
        ForgetLobbyLocally();
    }

    // ─────────────────────────────────────────────
    //  Create
    // ─────────────────────────────────────────────

    public async void createLobbyWithGivenOptions()
    {
        if (!TryBeginOperation()) return;

        try
        {
            SessionResult services = await SessionLauncher.EnsureServicesAsync();
            if (!services.Success) { Warn(services.Error); return; }

            // Relay first. If it fails there is no lobby to clean up — the old order created the
            // lobby, then allocated Relay, and on an allocation failure left an orphan lobby
            // advertised to everyone with a join code that led nowhere.
            SessionResult host = await SessionLauncher.HostRelayAsync(maxPlayers);
            if (!host.Success) { Warn(host.Error); return; }

            string lobbyName = string.IsNullOrWhiteSpace(lobbyList.getLobbyNameInputText())
                ? $"{playerName}'s game"
                : lobbyList.getLobbyNameInputText();

            bool isPrivate = lobbyList.getLobbyPrivate();

            var options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = GetPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    // Written at creation rather than by a follow-up UpdateLobbyAsync. A client that
                    // polled in the gap between the two saw a lobby with no join code and read
                    // straight past the missing key.
                    { KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, host.JoinCode) }
                }
            };

            if (isPrivate)
                options.Password = NullIfBlank(lobbyList.getLobbyPasswordInputText());

            hostLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            joinedLobby = hostLobby;

            lobbyList.openLobbyScreen(joinedLobby.Name, joinedLobby.LobbyCode);
            lobbyList.setStartGameButtonState(true);
            UpdatePlayerListInLobby();

            Debug.Log($"[Lobby] Hosting '{joinedLobby.Name}' code={joinedLobby.LobbyCode} relay={host.JoinCode}");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not create the lobby."));
            SessionLauncher.Shutdown();
        }
        finally
        {
            EndOperation();
        }
    }

    // ─────────────────────────────────────────────
    //  List / Join
    // ─────────────────────────────────────────────

    public async void listLobbies()
    {
        try
        {
            var query = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                },
                Order = new List<QueryOrder>
                {
                    new(false, QueryOrder.FieldOptions.Created)
                }
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(query);

            if (lobbyList == null) return;

            lobbyList.clearPrevList();
            foreach (Lobby lobby in response.Results)
                lobbyList.listNewLobby(lobby);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not fetch the lobby list."));
        }
    }

    public async void JoinLobbyById(string id)
    {
        if (!TryBeginOperation()) return;

        try
        {
            Lobby lobby = await LobbyService.Instance.JoinLobbyByIdAsync(id, new JoinLobbyByIdOptions
            {
                Player = GetPlayer()
            });

            await ConnectToJoinedLobby(lobby);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not join that lobby."));
        }
        finally
        {
            EndOperation();
        }
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        if (!TryBeginOperation()) return;

        try
        {
            lastAttemptedJoinCode = SessionLauncher.NormalizeJoinCode(lobbyCode);

            if (string.IsNullOrEmpty(lastAttemptedJoinCode))
            {
                Warn("Enter a lobby code first.");
                return;
            }

            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lastAttemptedJoinCode,
                new JoinLobbyByCodeOptions { Player = GetPlayer() });

            await ConnectToJoinedLobby(lobby);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not join with that code."));
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>
    /// Retries the last code with a password, for a lobby that turned out to be protected.
    /// </summary>
    public async void JoinLobbyByPassword(string lobbyPassword)
    {
        if (!TryBeginOperation()) return;

        try
        {
            if (string.IsNullOrWhiteSpace(lobbyPassword))
            {
                Warn("Enter the lobby password first.");
                return;
            }

            if (string.IsNullOrEmpty(lastAttemptedJoinCode))
            {
                Warn("Enter the lobby code first, then the password.");
                return;
            }

            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lastAttemptedJoinCode,
                new JoinLobbyByCodeOptions
                {
                    Player = GetPlayer(),
                    Password = lobbyPassword
                });

            await ConnectToJoinedLobby(lobby);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not join with that password."));
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>
    /// Reads the Relay code off a lobby we are already a member of and connects to it.
    ///
    /// The lobby membership is rolled back if the Relay connection fails. Otherwise a failed
    /// connection leaves a ghost occupying a slot in a lobby it is not in, which is how a
    /// four-player lobby ends up refusing a third player.
    /// </summary>
    private async Task ConnectToJoinedLobby(Lobby lobby)
    {
        if (lobby == null)
        {
            Warn("The lobby service returned nothing.");
            return;
        }

        if (!lobby.Data.TryGetValue(KEY_RELAY_JOIN_CODE, out DataObject relayCode)
            || string.IsNullOrEmpty(relayCode.Value))
        {
            await LeaveLobbyQuietly(lobby.Id);
            Warn("That lobby has no Relay server attached. The host may still be setting it up.");
            return;
        }

        SessionResult join = await SessionLauncher.JoinRelayAsync(relayCode.Value);
        if (!join.Success)
        {
            await LeaveLobbyQuietly(lobby.Id);
            Warn(join.Error);
            return;
        }

        joinedLobby = lobby;
        hostLobby = null;

        lobbyList.openLobbyScreen(joinedLobby.Name, joinedLobby.LobbyCode);
        lobbyList.setStartGameButtonState(false);
        UpdatePlayerListInLobby();

        Debug.Log($"[Lobby] Joined '{joinedLobby.Name}' and connected to its Relay server.");
    }

    // ─────────────────────────────────────────────
    //  Leave / Start
    // ─────────────────────────────────────────────

    public async void LeaveLobby()
    {
        try
        {
            string lobbyId = joinedLobby?.Id;

            // Local state first, so the UI responds even if the service call fails or hangs.
            ForgetLobbyLocally();
            SessionLauncher.Shutdown();

            if (!string.IsNullOrEmpty(lobbyId))
                await LeaveLobbyQuietly(lobbyId);

            listLobbies();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not leave cleanly."));
        }
    }

    public async void StartLobbyGame()
    {
        if (!TryBeginOperation()) return;

        try
        {
            if (joinedLobby == null)
            {
                Warn("You are not in a lobby.");
                return;
            }

            if (!IsPlayerLobbyHost())
            {
                Warn("Only the host can start the game!");
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                Warn("The host is not running a server. Try recreating the lobby.");
                return;
            }

            if (!await WaitForEveryoneToConnect())
            {
                Warn($"Only {networkManager.ConnectedClientsIds.Count} of {joinedLobby.Players.Count} " +
                     "players finished connecting. Wait a moment and try again.");
                return;
            }

            // Lock before loading so nobody joins the lobby during the handoff and lands in a menu
            // scene the rest of the session has already left.
            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions { IsLocked = true });

            joinedLobby = null;
            hostLobby = null;

            Debug.Log($"[Lobby] Starting game — loading '{gameScene.SceneName}' for " +
                      $"{networkManager.ConnectedClientsIds.Count} client(s).");

            networkManager.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Warn(Describe(e, "Could not start the game."));
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>
    /// True once as many Netcode clients are connected as the lobby has members, or on timeout.
    /// </summary>
    private async Task<bool> WaitForEveryoneToConnect()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        float deadline = Time.realtimeSinceStartup + StartConvergenceTimeout;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (joinedLobby == null || networkManager == null) return false;

            if (networkManager.ConnectedClientsIds.Count >= joinedLobby.Players.Count)
                return true;

            await Task.Yield();
        }

        return networkManager != null
               && joinedLobby != null
               && networkManager.ConnectedClientsIds.Count >= joinedLobby.Players.Count;
    }

    // ─────────────────────────────────────────────
    //  Polling
    // ─────────────────────────────────────────────

    private async void HandleLobbyHeartBeat()
    {
        if (hostLobby == null || heartbeatInFlight) return;

        heartBeatTimer -= Time.deltaTime;
        if (heartBeatTimer > 0f) return;

        heartBeatTimer = HeartbeatInterval;
        heartbeatInFlight = true;

        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
        }
        catch (Exception e)
        {
            // Not surfaced to the player: a missed heartbeat only delists the lobby from search,
            // and the session itself keeps working. A warning popup here would fire repeatedly
            // over a flaky connection and bury messages that actually need acting on.
            Debug.LogWarning($"[Lobby] Heartbeat failed: {e.Message}");
        }
        finally
        {
            heartbeatInFlight = false;
        }
    }

    private async void HandleLobbyPollForUpdates()
    {
        if (joinedLobby == null || pollInFlight) return;

        lobbyUpdateTimer -= Time.deltaTime;
        if (lobbyUpdateTimer > 0f) return;

        lobbyUpdateTimer = PollInterval;
        pollInFlight = true;

        try
        {
            Lobby lobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);

            // The handoff to the game scene nulls joinedLobby while this request is in flight;
            // writing the stale result back would resurrect a lobby we have already left.
            if (joinedLobby == null) return;

            joinedLobby = lobby;
            if (hostLobby != null) hostLobby = lobby;

            UpdatePlayerListInLobby();
            lobbyList.setStartGameButtonState(IsPlayerLobbyHost());
        }
        catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
        {
            Warn("The host closed the lobby.");
            ForgetLobbyLocally();
            SessionLauncher.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Poll failed: {e.Message}");
        }
        finally
        {
            pollInFlight = false;
        }
    }

    private void UpdatePlayerListInLobby()
    {
        if (joinedLobby?.Players == null || lobbyList == null) return;

        var names = new List<string>(joinedLobby.Players.Count);

        foreach (Player p in joinedLobby.Players)
        {
            // Defensive on both the dictionary and the entry: a player object written by an older
            // build, or one still mid-join, may not carry the name key at all, and an unguarded
            // indexer here threw KeyNotFoundException every poll and killed the whole roster.
            names.Add(p?.Data != null && p.Data.TryGetValue(KEY_PLAYER_NAME, out PlayerDataObject value)
                ? value.Value
                : "Player");
        }

        lobbyList.showPlayerElements(names.ToArray());
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    private Player GetPlayer() => new()
    {
        Data = new Dictionary<string, PlayerDataObject>
        {
            { KEY_PLAYER_NAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
        }
    };

    private bool IsPlayerLobbyHost() =>
        joinedLobby != null
        && AuthenticationService.Instance.IsSignedIn
        && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;

    private void ForgetLobbyLocally()
    {
        joinedLobby = null;
        hostLobby = null;

        if (lobbyList == null) return;

        lobbyList.hideLobbyScreen();
        lobbyList.setStartGameButtonState(false);
    }

    /// <summary>Best-effort removal from a lobby. Never reports — callers are already handling a failure.</summary>
    private async Task LeaveLobbyQuietly(string lobbyId)
    {
        try
        {
            if (!string.IsNullOrEmpty(lobbyId) && AuthenticationService.Instance.IsSignedIn)
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Could not remove self from {lobbyId}: {e.Message}");
        }
    }

    /// <summary>
    /// One button-press at a time. Double-clicking Create used to allocate two Relay servers and
    /// two lobbies, then leave the second pair orphaned when the first finished last.
    /// </summary>
    private bool TryBeginOperation()
    {
        if (busy)
        {
            Debug.Log("[Lobby] Ignoring input — an operation is already running.");
            return false;
        }

        busy = true;
        return true;
    }

    private void EndOperation() => busy = false;

    private void Warn(string message)
    {
        Debug.LogWarning($"[Lobby] {message}");

        // Still useful without the popup wired up: this class is reachable from scenes that have no
        // warning panel, and losing the message entirely is how the original failed silently.
        if (warningSystem != null) warningSystem.warn(message);
    }

    private static string Describe(Exception e, string headline) =>
        e is LobbyServiceException lobbyException
            ? $"{headline}\n({lobbyException.Reason}: {lobbyException.Message})"
            : $"{headline}\n({e.GetType().Name}: {e.Message})";

    private static string NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
