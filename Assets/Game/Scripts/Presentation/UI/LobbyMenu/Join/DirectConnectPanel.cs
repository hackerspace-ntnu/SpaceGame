using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A host/join panel that does not involve Unity Gaming Services at all.
    ///
    /// This exists because every other way into a game depends on Relay and Lobby, and those can be
    /// unavailable for reasons no amount of code quality prevents: the project's services not
    /// enabled on the dashboard, an expired org seat, a campus network blocking UDP to Relay, an
    /// outage. When that happens the Relay path cannot even report accurately — a Relay
    /// misconfiguration surfaces as a connection that hangs, not as an error. Two people on the
    /// same network (or with one port forwarded) can always use this instead.
    ///
    /// Drawn with IMGUI on purpose. A uGUI panel needs a Canvas, an EventSystem, a font asset and
    /// prefab wiring — four things that can be missing or misconfigured in the scene, in a class
    /// whose entire job is to work when other things are broken. OnGUI needs none of them.
    /// </summary>
    public class DirectConnectPanel : MonoBehaviour
    {
        [Tooltip("Shows and hides the panel at runtime.")]
        [SerializeField] private Key toggleKey = Key.F9;

        [Tooltip("Show automatically on the multiplayer/lobby screen. Turn off to require the toggle key everywhere.")]
        [SerializeField] private bool visibleOnStart = true;

        [SerializeField] private ushort port = SessionLauncher.DefaultDirectPort;

        /// <summary>
        /// Scene to load once hosting. Left empty it is taken from the LobbySystem in the scene, so
        /// the two paths cannot drift apart.
        /// </summary>
        [SerializeField] private string gameSceneOverride;

        private bool visible;
        private string address = "127.0.0.1";
        private string status = string.Empty;
        private bool workInProgress;
        private string cachedLocalIp;

        /// <summary>
        /// Installs itself rather than waiting to be dragged into a scene.
        ///
        /// A fallback nobody remembered to add to the scene is not a fallback. This one has to be
        /// present on both machines for two people to connect, and requiring that step by hand is
        /// exactly the kind of thing that is missing on the one machine that needed it. Creating it
        /// here means it is always there, on every build, with nothing to wire.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<DirectConnectPanel>() != null) return;

            var host = new GameObject(nameof(DirectConnectPanel));
            host.AddComponent<DirectConnectPanel>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            cachedLocalIp = SessionLauncher.GetLocalIPv4();

            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            ApplyVisibility();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyVisibility();

        private void HandleSceneUnloaded(Scene scene) => ApplyVisibility();

        /// <summary>
        /// Shows itself only on the multiplayer screen — the one place a host/join control belongs.
        ///
        /// Presence of a <see cref="LobbySystem"/> is the test, not the scene's name. Matching names
        /// containing "Menu" put this panel on the main menu too, beside Singleplayer/Multiplayer,
        /// which is a second unexplained way to start a game in the one screen that should present
        /// exactly one. Names were always the wrong question: "MainMenu" and "LobbyMenu" are not
        /// meaningfully distinguishable as strings, and renaming a scene should not move a control.
        /// Asking whether the multiplayer UI is present answers the actual question directly.
        ///
        /// The panel is still reachable anywhere on <see cref="toggleKey"/> — the moment you need a
        /// Relay-free fallback is rarely the moment you are standing in the right scene for it.
        /// </summary>
        private void ApplyVisibility()
        {
            visible = visibleOnStart && FindAnyObjectByType<LobbySystem>() != null;
        }

        private void Update()
        {
            if (toggleKey == Key.None) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible) return;

            const float width = 330f;
            const float height = 210f;

            GUILayout.BeginArea(new Rect(12f, Screen.height - height - 12f, width, height), GUI.skin.box);

            GUILayout.Label("<b>Direct Connect</b> (no Unity services)");
            GUILayout.Label($"Your IP: {cachedLocalIp}:{port}");

            if (SessionLauncher.IsRunning)
            {
                DrawRunningState();
            }
            else
            {
                DrawIdleState();
            }

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(4f);
                GUILayout.Label(status);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"<size=10>{toggleKey} to hide</size>");
            GUILayout.EndArea();
        }

        private void DrawIdleState()
        {
            GUILayout.Space(6f);

            GUI.enabled = !workInProgress;

            if (GUILayout.Button("Host on this machine"))
                Host();

            GUILayout.Space(4f);
            GUILayout.Label("Host's IP address:");
            address = GUILayout.TextField(address ?? string.Empty);

            if (GUILayout.Button("Join"))
                Join();

            GUI.enabled = true;
        }

        private void DrawRunningState()
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            string role = networkManager.IsHost ? "Hosting"
                : networkManager.IsServer ? "Server"
                : "Connected";

            GUILayout.Label($"{role} — {networkManager.ConnectedClientsIds.Count} player(s)");

            if (GUILayout.Button("Disconnect"))
            {
                SessionLauncher.Shutdown();
                status = "Disconnected.";
            }
        }

        private void Host()
        {
            workInProgress = true;
            status = "Starting host...";

            SessionResult result = SessionLauncher.HostDirect(port);

            if (!result.Success)
            {
                status = result.Error;
                workInProgress = false;
                return;
            }

            string scene = ResolveGameScene();
            if (string.IsNullOrEmpty(scene))
            {
                status = "Hosting, but no game scene is configured to load.";
                workInProgress = false;
                return;
            }

            status = $"Hosting. Friends connect to {cachedLocalIp}:{port}";
            workInProgress = false;

            // Straight into the game rather than waiting in a menu. Clients that connect later are
            // synchronised into whatever scene the host is already in, and NetworkGameManager
            // spawns a player for every client as it connects — so there is nothing to coordinate
            // and no window in which joining is "too early" or "too late".
            NetworkManager.Singleton.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        private async void Join()
        {
            workInProgress = true;
            status = $"Connecting to {address}:{port}...";

            SessionResult result = await SessionLauncher.JoinDirectAsync(address, port);

            status = result.Success
                ? "Connected. Waiting for the host's world to load..."
                : result.Error;

            workInProgress = false;
        }

        private string ResolveGameScene()
        {
            if (!string.IsNullOrWhiteSpace(gameSceneOverride)) return gameSceneOverride;

            var lobby = FindAnyObjectByType<LobbySystem>();
            return lobby != null ? lobby.GameSceneName : null;
        }
    }
}
