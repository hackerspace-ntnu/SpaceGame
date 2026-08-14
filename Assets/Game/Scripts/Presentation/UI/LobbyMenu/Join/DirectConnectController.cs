using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Host or join by IP, with no Unity Gaming Services involved.
    ///
    /// This exists because every other way into a game depends on Relay and Lobby, and those can be
    /// unavailable for reasons no amount of code quality prevents: the project's services not
    /// enabled on the dashboard, an expired org seat, a campus network blocking UDP to Relay, an
    /// outage. When that happens the Relay path cannot even report accurately — a Relay
    /// misconfiguration surfaces as a connection that hangs, not as an error — so without this
    /// there is no way to tell "Relay is broken" from "my code is broken".
    ///
    /// It replaced an IMGUI panel that installed itself into any scene holding a LobbySystem. That
    /// panel needed no wiring, which is why it appeared as an unexplained second popup beside the
    /// real menu. This one is a tab like any other.
    /// </summary>
    public class DirectConnectController : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI localAddressLabel;
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private LobbyWarningSystem warningSystem;

        [Tooltip("Read for the game scene, so the direct and Relay paths cannot load different ones.")]
        [SerializeField] private LobbySystem lobby;

        [SerializeField] private ushort port = SessionLauncher.DefaultDirectPort;

        private void OnEnable()
        {
            if (localAddressLabel != null)
                localAddressLabel.text = $"{SessionLauncher.GetLocalIPv4()}:{port}";
        }

        public void CopyLocalAddress() =>
            GUIUtility.systemCopyBuffer = $"{SessionLauncher.GetLocalIPv4()}:{port}";

        public void HostDirect()
        {
            SessionResult result = SessionLauncher.HostDirect(port);
            if (!result.Success) { Warn(result.Error); return; }

            string scene = ResolveGameScene();
            if (string.IsNullOrEmpty(scene)) { Warn("Hosting, but no game scene is configured to load."); return; }

            // Straight into the game rather than waiting in a menu. Clients that connect later are
            // synchronised into whatever scene the host is already in, and NetworkGameManager
            // spawns a player for every client as it connects — so there is nothing to coordinate
            // and no window in which joining is "too early" or "too late".
            LoadingScreenUI.ShowUntilReady(scene);
            NetworkManager.Singleton.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        public async void JoinDirect()
        {
            string address = addressInput != null ? addressInput.text : null;

            SessionResult result = await SessionLauncher.JoinDirectAsync(address, port);
            if (!result.Success) { Warn(result.Error); return; }

            string scene = ResolveGameScene();
            if (!string.IsNullOrEmpty(scene))
                LoadingScreenUI.ShowUntilReady(scene, "Joining");
        }

        private string ResolveGameScene() => lobby != null ? lobby.GameSceneName : null;

        private void Warn(string message)
        {
            Debug.LogWarning($"[DirectConnect] {message}");
            if (warningSystem != null) warningSystem.warn(message);
        }
    }
}
