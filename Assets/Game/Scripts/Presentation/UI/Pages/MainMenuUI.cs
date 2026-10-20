using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.Presentation.Lobbies;
using SpaceGame.World;
public class MainMenuUI : MonoBehaviour
{
    [Header("Worlds")]
    [Tooltip("The Story world: the streamed main world. Also what VS and Disposable play in.")]
    [SerializeField] private GameWorld storyWorld;
    [Tooltip("The Spillexo world: one authored terrain, no chunk streaming.")]
    [SerializeField] private GameWorld spillexoWorld;

    [Header("World selection")]
    [Tooltip("The prefab every menu entry is cloned from, so the screens this menu opens carry its " +
             "hover animation and sounds. Assigned by Tools ▸ SpaceGame ▸ Menus ▸ Setup World Select.")]
    [SerializeField] private GameObject menuButtonPrefab;

    // Which world the open route leads into. Set by the front-menu entry that started the route,
    // and read by every screen after it, so the world list, the lobby and the scene load all agree.
    private bool spillexoChosen;

    private GameWorld ChosenWorld => spillexoChosen ? spillexoWorld : storyWorld;

    /// <summary>The world the screens this menu opens stage saves against.</summary>
    public WorldStreamingConfig WorldConfig => ChosenWorld != null ? ChosenWorld.Identity : null;

    /// <summary>
    /// The world scene every route into the game loads. Read by <see cref="LobbyUI"/> so the
    /// lobby and the singleplayer route cannot end up loading different ones.
    /// </summary>
    public string GameSceneName => ChosenWorld != null ? ChosenWorld.SceneName : null;

    /// <summary>
    /// The menu's own button, lent to the screens it opens. They are built at runtime and have no
    /// Inspector of their own, so this is where the reference lives.
    /// </summary>
    public GameObject MenuButtonPrefab => menuButtonPrefab;

    /// <summary>Opens the world list; entering a world is WorldSelectUI's job.</summary>
    public void StartSinglePlayer() => WorldSelectUI.Open(this, WorldSelectUI.Destination.Singleplayer);

    /// <summary>
    /// Front-menu entry: a throwaway singleplayer session for trying something out — no world
    /// naming, no crash-landing cutscene (the ship starts already down), and nothing is ever
    /// written to disk. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartDisposable()
    {
        spillexoChosen = false;
        WorldSession.StageNew("Disposable", WorldConfig, disposable: true);
        EnterWorld();
    }

    /// <summary>
    /// Front-menu entry: singleplayer or multiplayer, before anything else — the Story route's own
    /// version of the host/join fork below. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartStory() => OpenWorldRoute("STORY", spillexo: false);

    /// <summary>
    /// Front-menu entry: the Story route into the Spillexo world instead — same choices, same
    /// world list and lobby, a different scene and save identity. Bound by name from
    /// MainMenu.unity; do not rename.
    /// </summary>
    public void StartSpillexo() => OpenWorldRoute("SPILLEXO", spillexo: true);

    private void OpenWorldRoute(string title, bool spillexo)
    {
        spillexoChosen = spillexo;
        MenuChoiceUI.Open(this, title,
            new MenuChoiceUI.Choice("Singleplayer", StartSinglePlayer),
            new MenuChoiceUI.Choice("Multiplayer", StartMultiPlayer));
    }

    /// <summary>
    /// Front-menu entry: VS is multiplayer-only, so it asks host-or-join directly rather than
    /// singleplayer-or-multiplayer first. Bound by name from MainMenu.unity; do not rename.
    /// </summary>
    public void StartVersus()
    {
        spillexoChosen = false;
        MenuChoiceUI.Open(this, "VERSUS",
            new MenuChoiceUI.Choice("Host a game", HostVersus),
            new MenuChoiceUI.Choice("Join a game", JoinVersus));
    }

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
    /// Enters the chosen world as its singleplayer host, with the world already staged.
    /// Public so WorldSelectUI can finish the job it started.
    /// </summary>
    public void EnterWorld() => SessionEntry.EnterWorld(GameSceneName);

    /// <summary>
    /// Opens the lobby, with the host's world already chosen.
    ///
    /// No longer a scene load. The lobby used to be LobbyMenu.unity, which meant leaving the menu
    /// scene — and its 3D set — for a screen with a flat background, then loading it back again on
    /// the way out. It is now a page over this menu like every other screen the menu opens, so
    /// Back is a destroyed GameObject rather than a second scene load.
    /// </summary>
    public void EnterLobby() => LobbyUI.Open(this, LobbyRoute.StoryHost);

    public void QuitGame()
    {
        Application.Quit();
    }
}
