// Whether a rider is still attached to something that still exists.
//
// Two ways this breaks, and they look identical to the player: the mount was destroyed while they
// were on it, or the mount let go without the rider being told. Either leaves a player seated on
// nothing, with their own movement, camera and interactor all switched off by the mount teardown
// that never ran.
namespace SpaceGame.Core.Safety
{
    public static class MountRecoveryRule
    {
        public static bool ShouldDismount(bool haveMount, bool mountAlive, bool mountClaimsRider,
                                          float brokenSeconds, float timeoutSeconds)
        {
            if (!haveMount) return false;
            if (mountAlive && mountClaimsRider) return false;

            // Bounded, because a mount despawning and a rider being released are two events on two
            // machines and they do not arrive in the same frame.
            return brokenSeconds >= timeoutSeconds;
        }
    }
}
