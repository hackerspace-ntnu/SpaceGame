// Turns a camera back on when this machine has stopped drawing anything.
//
// Cameras here are handed between owners constantly — the player's own, a mount's orbit camera, the
// spectator on death, the focus camera at a terminal — and every handover is a pair of calls. A
// throw between them leaves every camera off, which the player reads as the game having crashed.
//
// The measurement is Camera.allCamerasCount and nothing cleverer. "Is the right camera active"
// would need to know about mounts, spectators, focus cameras and the terminal, and would be wrong
// the first time somebody adds a fifth. Zero enabled cameras is unambiguous and needs no such list.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Safety
{
    public sealed class ViewGuard : ISessionGuard
    {
        /// <summary>How long a black screen is tolerated. A Single scene load legitimately has none.</summary>
        public const float TimeoutSeconds = 3f;

        private float blindSeconds;

        public string Name => "View";

        public void Check(float interval)
        {
            bool anyCamera = Camera.allCamerasCount > 0;

            blindSeconds = anyCamera ? 0f : blindSeconds + interval;

            if (!ViewRecoveryRule.ShouldRestoreView(anyCamera, blindSeconds, TimeoutSeconds))
                return;

            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null || player.PlayerCamera == null)
            {
                // Nothing to restore. Say so once per interval rather than going quiet, because a
                // black screen with no local player is a different bug and needs its own report.
                Debug.LogError($"[ViewGuard] No enabled camera anywhere for {blindSeconds:F1}s and no " +
                               "local player to restore one from.");
                blindSeconds = 0f;
                return;
            }

            Debug.LogError($"[ViewGuard] No enabled camera anywhere for {blindSeconds:F1}s. " +
                           "Re-enabling the player camera — a camera handover did not complete, and " +
                           "that is the real bug.", player);

            player.PlayerCamera.gameObject.SetActive(true);
            blindSeconds = 0f;
        }
    }
}
