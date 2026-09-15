// Whether this machine has stopped drawing anything at all.
//
// A blunt measurement on purpose: not "is the right camera active" — which needs to know about
// mounts, spectators, focus cameras and the terminal — but "is there any enabled camera". Zero is
// unambiguous, and every legitimate state in the game has one: the menu has its own, the loading
// screen has its own, a mounted player has the mount's, a dead player has the spectator's.
//
// It is bounded because a Single scene load legitimately has none for a moment.
namespace SpaceGame.Core.Safety
{
    public static class ViewRecoveryRule
    {
        public static bool ShouldRestoreView(bool anyEnabledCamera, float blindSeconds, float timeoutSeconds)
        {
            if (anyEnabledCamera) return false;
            return blindSeconds >= timeoutSeconds;
        }
    }
}
