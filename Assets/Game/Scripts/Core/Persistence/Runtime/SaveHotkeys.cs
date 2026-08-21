using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Quicksave and quickload on two keys.
    ///
    /// Deliberately not routed through the game's InputControls asset. Those bindings are player
    /// actions that belong to a possessed character; these are session commands that have to work
    /// with no player spawned, on a dead player, and mid-load — which is precisely when a save
    /// system needs testing.
    ///
    /// Quickload restarts the world scene rather than restoring in place. Un-spawning every player,
    /// rewinding every streamed chunk and rebuilding the NavMesh live is a far larger and more
    /// fragile operation than the scene load the game already does reliably on every launch.
    /// </summary>
    public class SaveHotkeys : MonoBehaviour
    {
        [Tooltip("Scene to reload when quickloading. Leave empty to reload whichever scene is active.")]
        [SerializeField] private string worldSceneName;

        [SerializeField] private Key quickSaveKey = Key.F5;
        [SerializeField] private Key quickLoadKey = Key.F9;

        [Tooltip("Off for a release build unless quicksave is meant to be a player-facing feature.")]
        [SerializeField] private bool enabledInBuilds = true;

        private void Update()
        {
            if (!enabledInBuilds && !Application.isEditor) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[quickSaveKey].wasPressedThisFrame) QuickSave();
            if (keyboard[quickLoadKey].wasPressedThisFrame) QuickLoad();
        }

        /// <summary>
        /// Writes the active world, or says why it did not.
        ///
        /// <para>
        /// Every refusal is announced. <c>SaveManager.Save</c> logs its own through a
        /// <c>verbose</c>-gated <c>Log</c>, which is right for the autosave timer and wrong for a
        /// keypress: F5 on a client did nothing at all, silently, and the player's only evidence was
        /// that their world did not come back. A hotkey is a question the player asked, and it gets
        /// an answer.
        /// </para>
        /// </summary>
        public void QuickSave()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning("[Save] Quicksave pressed with no SaveManager in the scene.");
                return;
            }

            if (!WorldSession.IsActive)
            {
                Debug.LogWarning("[Save] Nothing to quicksave: no world is active. " +
                                 "Enter a world through the main menu.");
                return;
            }

            // Checked here as well as inside SaveManager, so the message names the player's actual
            // situation rather than the system's rule. A guest in someone else's game cannot save it.
            if (Network.IsNetworked && !Network.Server)
            {
                Debug.LogWarning("[Save] Quicksave ignored: the world belongs to the host, so only " +
                                 "they can save it. Ask them to press the quicksave key.");
                return;
            }

            // Writes to whichever world is active — see SaveManager.QuickSave.
            if (manager.QuickSave())
            {
                Debug.Log($"[Save] Quicksaved '{WorldSession.DisplayName ?? "world"}'.");
                return;
            }

            Debug.LogWarning("[Save] Quicksave did not run. The most likely reason is that the " +
                             "previous save is still being written; try again in a moment. Turn on " +
                             "SaveManager's 'verbose' for the exact refusal.");
        }

        public void QuickLoad()
        {
            if (!WorldSession.IsActive)
            {
                Debug.LogWarning("[Save] Quickload pressed with no active world.");
                return;
            }

            // A client quickloading used to fall through to SceneManager.LoadScene(Single) below,
            // which tears this machine's whole scene out from under a live session: its
            // NetworkManager, its spawned player and every object Netcode had synchronised go with
            // it, while the server carries on believing the peer is in the world. The state the
            // client would be reloading is not theirs to reload either — the world lives on the
            // server and only the server has the file.
            if (Network.IsNetworked && !Network.Server)
            {
                Debug.LogWarning("[Save] Quickload ignored: the world belongs to the host. Reloading " +
                                 "it here would drop you out of the session without reloading anything. " +
                                 "Ask the host to quickload, and everyone is taken back together.");
                return;
            }

            // Restages THIS world rather than a global quicksave slot, so quickloading cannot pull
            // a different world's session into the one being played.
            if (!WorldSession.StageExisting(WorldSession.WorldId, ActiveConfig(), out string error))
            {
                Debug.LogWarning($"[Save] Quickload failed: {error}");
                return;
            }

            string target = string.IsNullOrEmpty(worldSceneName)
                ? SceneManager.GetActiveScene().name
                : worldSceneName;

            Debug.Log($"[Save] Quickloading into '{target}'.");

            // Through Netcode's scene manager when hosted, so clients follow. A plain
            // SceneManager.LoadScene here would leave every client in the old world. The plain call
            // is now only reachable with no session at all — an editor scene, an offline test —
            // which is the one case where this machine IS the world.
            if (Network.Server)
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(target, LoadSceneMode.Single);
            else
                SceneManager.LoadScene(target, LoadSceneMode.Single);
        }

        /// <summary>
        /// The config of the world being played, so a quickload validates against the same world it
        /// is reloading. Found from the live streamer rather than a serialized reference, which
        /// would be one more thing to wire per world scene and to get wrong.
        /// </summary>
        private static WorldStreamingConfig ActiveConfig()
        {
            var streamer = Object.FindFirstObjectByType<WorldStreamer>();
            return streamer != null ? streamer.Config : null;
        }
    }
}
