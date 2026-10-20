using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Presentation;

namespace SpaceGame.Core
{
    /// <summary>
    /// The one way into a world as its singleplayer host — the counterpart of <see cref="SessionExit"/>.
    ///
    /// Shared by the main menu and by the bootstrapper's Play-in-a-world-scene route, so pressing
    /// Play in a world scene is the same session as entering it from the menu, not a bare scene load
    /// with nobody hosting it.
    /// </summary>
    public static class SessionEntry
    {
        /// <summary>
        /// Does the three things every route into the world does, in the order they must happen.
        /// The caller stages <see cref="Persistence.WorldSession"/> first. False, and logged, when
        /// the session could not start.
        /// </summary>
        public static bool EnterWorld(string sceneName)
        {
            // Up before the load starts, and held until terrain streaming and the NavMesh bake have
            // finished — those run after the scene reports loaded and are what makes the first few
            // seconds stutter. A caller that already put it up (the bootstrapper) just restarts the
            // same wait.
            LoadingScreenUI.ShowUntilReady(sceneName);

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
                return false;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            return true;
        }
    }
}
