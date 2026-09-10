// Gets a rider off a mount that no longer exists.
//
// Mounting switches off the rider's movement, camera and interactor, and dismounting switches them
// back on. When the mount goes away without that second half running — despawned by the streamer,
// destroyed on the server, torn down during a Shutdown that NGO does not deparent through — the
// rider is left with everything switched off and nothing to ride.
//
// The recovery is deliberately the game's own Dismount and not a hand-written unwind. Dismount knows
// about the camera, the collision ignores, the pose and the drop point; a second copy of that
// knowledge here would rot the moment either changes.
//
// When the mount is gone entirely there is nothing to call Dismount on. That case falls through to
// InputRestoreGuard, which is why LocalRiderMount is cleared here: the mount reference is what stops
// that guard from firing.
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Core.Safety
{
    public sealed class MountGuard : ISessionGuard
    {
        /// <summary>
        /// How long a broken attachment is tolerated. A mount despawning and a rider being released
        /// are two events on two machines; they do not arrive in the same frame.
        /// </summary>
        public const float TimeoutSeconds = 3f;

        private float brokenSeconds;

        public string Name => "Mount";

        public void Check(float interval)
        {
            MountModule mount = MountModule.LocalRiderMount;

            // ReferenceEquals for "we hold a reference at all" and Unity's == for "the object behind
            // it still exists". The two disagree exactly when a mount was destroyed under its rider,
            // which is the failure this guard is for.
            bool haveMount = !ReferenceEquals(mount, null);
            bool mountAlive = mount != null;
            bool claimsRider = mountAlive && mount.IsMounted;

            if (!haveMount || claimsRider)
            {
                brokenSeconds = 0f;
                return;
            }

            brokenSeconds += interval;

            if (!MountRecoveryRule.ShouldDismount(haveMount, mountAlive, claimsRider,
                                                  brokenSeconds, TimeoutSeconds))
                return;

            Debug.LogError($"[MountGuard] The local player has been attached to a mount that is " +
                           $"{(mountAlive ? "no longer claiming them" : "destroyed")} for " +
                           $"{brokenSeconds:F1}s. Forcing a dismount — a mount teardown did not run, " +
                           "and that is the real bug.");

            // The real Dismount when there is still an object to run it, so the camera, the
            // collision ignores and the pose are all unwound by the code that set them.
            if (mountAlive) mount.Dismount();

            // Cleared either way. With the mount destroyed there is nothing to unwind, and clearing
            // this is what lets InputRestoreGuard see an unmounted player and hand the controls back.
            MountModule.ClearLocalRiderMount();

            brokenSeconds = 0f;
        }
    }
}
