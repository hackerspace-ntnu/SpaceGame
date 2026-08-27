using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.World;
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;
    [SerializeField] private SceneReference minigameScene;

    [Header("World selection")]
    [Tooltip("The world these saves belong to. Recorded in every save's header so a save cannot " +
             "be loaded into a different world.")]
    [SerializeField] private WorldStreamingConfig worldConfig;
    [Tooltip("The prefab every menu entry is cloned from, so the screens this menu opens carry its " +
             "hover animation and sounds. Assigned by Tools ▸ SpaceGame ▸ Menus ▸ Setup World Select.")]
    [SerializeField] private GameObject menuButtonPrefab;

    /// <summary>The world the screens this menu opens stage saves against.</summary>
    public WorldStreamingConfig WorldConfig => worldConfig;

    /// <summary>
    /// The world scene every route into the game loads. Read by <see cref="LobbyUI"/> so the
    /// lobby and the singleplayer route cannot end up loading different ones.
    /// </summary>
    public string GameSceneName => gameScene != null ? gameScene.SceneName : null;

    /// <summary>
    /// The menu's own button, lent to the screens it opens. They are built at runtime and have no
    /// Inspector of their own, so this is where the reference lives.
    /// </summary>
    public GameObject MenuButtonPrefab => menuButtonPrefab;

    /// <summary>Opens the world list; entering a world is WorldSelectUI's job.</summary>
    public void StartSinglePlayer() => WorldSelectUI.Open(this, WorldSelectUI.Destination.Singleplayer);

    /// <summary>
    /// Front-menu entry: singleplayer or multiplayer, before anything else — the Story route's own
    /// version of the host/join fork below. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartStory() =>
        MenuChoiceUI.Open(this, "STORY",
            new MenuChoiceUI.Choice("Singleplayer", StartSinglePlayer),
            new MenuChoiceUI.Choice("Multiplayer", StartMultiPlayer));

    /// <summary>
    /// Front-menu entry: VS is multiplayer-only, so it asks host-or-join directly rather than
    /// singleplayer-or-multiplayer first. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartVersus() =>
        MenuChoiceUI.Open(this, "VERSUS",
            new MenuChoiceUI.Choice("Host a game", HostVersus),
            new MenuChoiceUI.Choice("Join a game", JoinVersus));

    /// <summary>
    /// Asks host or join before anything else.
    ///
    /// This used to open the world list directly, which made picking a world a toll on every route
    /// into multiplayer — including joining, where the world is the host's and the one the joiner
    /// picked is at best ignored. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartMultiPlayer() =>
        MenuChoiceUI.Open(this, "MULTIPLAYER",
            new MenuChoiceUI.Choice("Host a game", HostMultiplayer),
            new MenuChoiceUI.Choice("Join a game", JoinMultiplayer));

    /// <summary>Host: pick a world, then the lobby. Called back from MenuChoiceUI.</summary>
    public void HostMultiplayer() => WorldSelectUI.Open(this, WorldSelectUI.Destination.Lobby);

    /// <summary>
    /// Join: straight to the lobby, with no world of our own.
    ///
    /// Clearing is not tidiness. SaveManager.Awake consumes whatever WorldSession has staged and
    /// restores it — on every peer, client included — so a joiner who had staged a world would load
    /// their own save over the host's world as they arrived.
    /// </summary>
    public void JoinMultiplayer()
    {
        WorldSession.Clear();
        LobbyUI.Open(this, LobbyRoute.StoryJoin);
    }

    /// <summary>Host: rules first, so the host picks the team shape before anyone can join it.</summary>
    public void HostVersus() => VersusRulesUI.Open(this);

    /// <summary>
    /// Join: straight to the lobby, with no world and no leftover match state of our own.
    ///
    /// WorldSession is cleared for the reason <see cref="JoinMultiplayer"/> already documents:
    /// SaveManager.Awake restores whatever is staged there on every peer, host or client, so a
    /// joiner carrying a staged world would load their own save over the host's. VersusSession is
    /// cleared too, for a different reason: it is not staged by VersusRulesUI, which only ever
    /// writes its own StagedTeams/StagedTeamSize — it is populated by VersusSession.Begin when a
    /// match actually starts. A peer who played a VS match, returned to the menu, and is now
    /// joining someone else's match would otherwise arrive still carrying the LAST match's team and
    /// colour, from whichever session never got cleared on the way back.
    /// </summary>
    public void JoinVersus()
    {
        WorldSession.Clear();
        VersusSession.Clear();
        LobbyUI.Open(this, LobbyRoute.VersusJoin);
    }

    /// <summary>
    /// Finishes the host route VersusRulesUI started.
    ///
    /// WorldSession is cleared even though VersusRulesUI never stages a world of its own: this is
    /// insurance against a leftover from an earlier, abandoned trip through WorldSelectUI (Story
    /// and back, say) that would otherwise sit staged and get consumed by SaveManager.Awake once
    /// the VS match's world scene loads.
    /// </summary>
    public void EnterVersusLobby()
    {
        WorldSession.Clear();
        LobbyUI.Open(this, LobbyRoute.VersusHost);
    }

    /// <summary>
    /// Does the three things every route into the world does, in the order they must happen.
    /// Public so WorldSelectUI can finish the job it started.
    /// </summary>
    public void EnterWorld()
    {
        // Up before the load starts, and held until terrain streaming and the NavMesh bake have
        // finished — those run after the scene reports loaded and are what makes the first few
        // seconds stutter.
        LoadingScreenUI.ShowUntilReady(gameScene.SceneName);

        // HostLocal, not HostDirect: singleplayer is a host of one and must keep the NetworkManager
        // prefab's own port. HostDirect would override it with its LAN defaults, re-breaking the
        // port the project already had to move once around Unity's leaked UDP socket. What this
        // still buys over a bare StartHost() is the Relay reset — the transport keeps its last
        // configuration, so entering singleplayer after a Relay attempt would host on a dead
        // allocation.
        SessionResult result = SessionLauncher.HostLocal();

        if (!result.Success)
        {
            Debug.LogError($"[Net] Could not start the session: {result.Error}");
            LoadingScreenUI.Dismiss();
            return;
        }

        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }

    /// <summary>
    /// Opens the lobby, with the host's world already chosen.
    ///
    /// No longer a scene load. The lobby used to be LobbyMenu.unity, which meant leaving the menu
    /// scene — and its 3D set — for a screen with a flat background, then loading it back again on
    /// the way out. It is now a page over this menu like every other screen the menu opens, so
    /// Back is a destroyed GameObject rather than a second scene load.
    /// </summary>
    public void EnterLobby() => LobbyUI.Open(this, LobbyRoute.StoryHost);

    // Wired to the menu's Minigame button. The match is configured first — the
    // config screen calls LaunchMinigame() once the host has picked a gamemode.
    public void StartMinigame()
    {
        MinigameConfigUI.Open(this);
    }

    public void LaunchMinigame()
    {
        // Tell NetworkGameManager's auto-spawn coroutine to hold off until minigameScene has
        // loaded and gone active, otherwise it spawns the player at persistentScene's own
        // SpawnPoint the instant persistentScene finishes loading. Must be set before StartHost()
        // so it's in place before OnNetworkSpawn fires.
        NetworkGameManager.PendingSceneNameToWaitFor = minigameScene.SceneName;

        // Waits on the arena, not gameScene: the arena is loaded additively on top and is the
        // scene the player actually ends up in.
        LoadingScreenUI.ShowUntilReady(minigameScene.SceneName);

        NetworkManager.Singleton.StartHost();

        void OnLoaded(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            if (sceneName != gameScene.SceneName) return;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoaded;
            NetworkManager.Singleton.SceneManager.LoadScene(minigameScene.SceneName, LoadSceneMode.Additive);
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoaded;
        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }
    
    public void QuitGame()
    {
        Application.Quit();
    }
}
