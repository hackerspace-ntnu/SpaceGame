// Raises the loading overlay on every machine the server pulls into a world, not only on the one
// that pressed the button.
//
// LoadingScreenUI was only ever shown by the screen that STARTED a load: the host pressing Start in
// the lobby, the menu entering a world, a joiner arriving mid-game. Everyone else in the lobby was
// moved into the world by Netcode's own scene synchronisation, which no menu code is watching — so
// a client's lobby simply vanished and it stared at a half-built world assembling around it while
// the host, who had raised its own overlay, saw a clean load. There is nothing to send: the load is
// already announced to every peer as a scene event, and this listens for it.
//
// Bootstrapped from a static rather than placed in a scene, for the same reason SessionWatchdog is:
// it has to be listening in the menu, through the load, and in the world, and a listener that must
// exist in all three cannot be authored into one of them.
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    public class NetworkLoadingScreen : MonoBehaviour
    {
        private static NetworkLoadingScreen instance;

        private SceneEventHook sceneEvents;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject(nameof(NetworkLoadingScreen));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetworkLoadingScreen>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Through a hook rather than a plain subscription: there is no NetworkSceneManager at
            // all until a session starts, and a new one on every session after that. See
            // SceneEventHook.
            sceneEvents = new SceneEventHook(HandleSceneEvent);
            sceneEvents.Poll();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            sceneEvents?.Detach();
        }

        private void Update() => sceneEvents?.Poll();

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            // Single only. Netcode reports the streamed chunk scenes and the interiors through this
            // same event as additive loads, and covering the screen every time a chunk arrives would
            // blank the world the player is standing in.
            if (sceneEvent.SceneEventType != SceneEventType.Load) return;
            if (sceneEvent.LoadSceneMode != LoadSceneMode.Single) return;

            // Already up, which is the normal case for whoever started the load: they raised it
            // before calling LoadScene, and Netcode reports the event locally on the server as well
            // as on every client. Re-entering would restart the wait and its stall timer.
            if (LoadingScreenUI.IsShowing) return;

            LoadingScreenUI.ShowUntilReady(sceneEvent.SceneName);
        }
    }
}
