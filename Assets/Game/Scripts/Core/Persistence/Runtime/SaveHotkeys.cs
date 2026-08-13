using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using SpaceGame.Persistence;

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

        public void QuickSave()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning("[Save] Quicksave pressed with no SaveManager in the scene.");
                return;
            }

            if (manager.Save(SaveSlots.QuickSaveSlotId, "Quicksave"))
                Debug.Log("[Save] Quicksaved.");
        }

        public void QuickLoad()
        {
            if (!SaveManager.StageLoad(SaveSlots.QuickSaveSlotId, out string error))
            {
                Debug.LogWarning($"[Save] Quickload failed: {error}");
                return;
            }

            string target = string.IsNullOrEmpty(worldSceneName)
                ? SceneManager.GetActiveScene().name
                : worldSceneName;

            Debug.Log($"[Save] Quickloading into '{target}'.");

            // Through Netcode's scene manager when hosted, so clients follow. A plain
            // SceneManager.LoadScene here would leave every client in the old world.
            if (Network.Server)
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(target, LoadSceneMode.Single);
            else
                SceneManager.LoadScene(target, LoadSceneMode.Single);
        }
    }
}
