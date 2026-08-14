using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

/// <summary>
/// The lobby menu's controller: it decides WHEN to act and what the screen shows. What actually
/// happens lives in <see cref="LobbySession"/>, which outlives this object and this scene.
///
/// Deliberately in the global namespace with these exact method names — LobbyMenu.unity binds its
/// controls to them by string through UnityEvent, which resolves at runtime with no compile-time
/// link. Renaming any of them turns its button into a silent no-op with nothing logged.
/// LobbyMenuWiringTests pins them.
///
/// A MonoBehaviour, not a NetworkBehaviour. NetworkBehaviour requires a NetworkObject, and this
/// sits in a scene loaded by plain SceneManager rather than Netcode's — so starting a host from
/// here made Netcode try to spawn an in-scene object joining clients had no matching copy of,
/// which is a synchronisation failure rather than a warning.
/// </summary>
public class LobbySystem : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;

    /// <summary>Kept for existing inspector references. The session owns the real limit.</summary>
    public static int maxPlayers = LobbySession.MaxPlayers;

    /// <summary>
    /// The scene the lobby hands off to. Exposed so the Relay-free direct path loads the same one
    /// rather than keeping a second copy of the name that can drift.
    /// </summary>
    public string GameSceneName => gameScene != null ? gameScene.SceneName : null;

    private LobbyListSystem lobbyList;
    private LobbyWarningSystem warningSystem;
    private LobbySession session;

    /// <summary>
    /// The code last tried, so a password prompt can retry the same lobby. A private lobby is
    /// excluded from query results, so it is reachable only by code — there is no listed entry to
    /// click and no id to look up.
    /// </summary>
    private string lastAttemptedJoinCode;

    private void Awake()
    {
        // Before any await. Resolved after `await` these were null whenever initialisation failed
        // — and every error path here reports through warningSystem, so the one situation that
        // most needed a message on screen instead threw inside an async void and vanished.
        lobbyList = GetComponent<LobbyListSystem>();
        warningSystem = GetComponent<LobbyWarningSystem>();
    }

    private async void Start()
    {
        session = LobbySession.Instance;
        session.Changed += Render;
        session.Failed += Warn;

        Render();

        if (!await session.EnsureReadyAsync()) return;

        NetworkBootstrap.LogRegisteredPrefabCount();
        listLobbies();
    }

    private void OnDestroy()
    {
        if (session == null) return;

        session.Changed -= Render;
        session.Failed -= Warn;
    }

    // ───────────────────────────────────────────────
    //  Bound by NAME from LobbyMenu.unity. Do not rename.
    // ───────────────────────────────────────────────

    public async void createLobbyWithGivenOptions() =>
        await session.CreateAsync(
            lobbyList.getLobbyNameInputText(),
            lobbyList.getLobbyPrivate(),
            lobbyList.getLobbyPasswordInputText());

    public async void listLobbies()
    {
        List<Lobby> lobbies = await session.QueryAsync();

        if (lobbyList == null) return;

        lobbyList.clearPrevList();
        foreach (Lobby lobby in lobbies)
            lobbyList.listNewLobby(lobby);
    }

    public async void JoinLobbyById(string id)
    {
        if (await session.JoinByIdAsync(id)) EnterIfPlaying();
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        lastAttemptedJoinCode = SessionLauncher.NormalizeJoinCode(lobbyCode);
        if (await session.JoinByCodeAsync(lastAttemptedJoinCode)) EnterIfPlaying();
    }

    /// <summary>Retries the last code with a password, for a lobby that turned out to be protected.</summary>
    public async void JoinLobbyByPassword(string lobbyPassword)
    {
        if (string.IsNullOrWhiteSpace(lobbyPassword)) { Warn("Enter the lobby password first."); return; }
        if (string.IsNullOrEmpty(lastAttemptedJoinCode)) { Warn("Enter the lobby code first, then the password."); return; }

        if (await session.JoinByCodeAsync(lastAttemptedJoinCode, lobbyPassword)) EnterIfPlaying();
    }

    public async void LeaveLobby()
    {
        await session.LeaveAsync();
        listLobbies();
    }

    public async void StartLobbyGame()
    {
        string scene = GameSceneName;
        if (string.IsNullOrEmpty(scene)) { Warn("No game scene is configured on this lobby."); return; }

        // Up before the load starts and held until terrain streaming and the NavMesh bake finish —
        // those run after the scene reports loaded and are what makes the first seconds stutter.
        LoadingScreenUI.ShowUntilReady(scene);

        if (!await session.BeginGameAsync(scene)) LoadingScreenUI.Dismiss();
    }

    // ───────────────────────────────────────────────
    //  View
    // ───────────────────────────────────────────────

    /// <summary>
    /// Redraws the screen from the session. Called on every session change rather than written to
    /// piecemeal at each call site, so there is one description of what the screen shows.
    /// </summary>
    private void Render()
    {
        if (lobbyList == null || session == null) return;

        if (session.Current == null)
        {
            lobbyList.hideLobbyScreen();
            lobbyList.setStartGameButtonState(false);
            return;
        }

        lobbyList.openLobbyScreen(session.Current.Name, session.Current.LobbyCode);
        lobbyList.setStartGameButtonState(session.IsHost);
        lobbyList.showPlayerElements(LobbySession.PlayerNames(session.Current));
    }

    /// <summary>
    /// A joiner whose host is already playing skips the lobby screen entirely. Netcode's scene
    /// synchronisation pulls them into the running world; this only puts something on screen while
    /// that happens.
    /// </summary>
    private void EnterIfPlaying()
    {
        if (session.State != LobbyState.InGame) return;

        string scene = GameSceneName;
        if (!string.IsNullOrEmpty(scene))
            LoadingScreenUI.ShowUntilReady(scene, "Joining");
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"[Lobby] {message}");

        // Still useful without the strip wired up: this class is reachable from scenes that have
        // no warning panel, and losing the message entirely is how the original failed silently.
        if (warningSystem != null) warningSystem.warn(message);
    }
}
