// Whether the player has lost their controls to nothing.
//
// Every "why can I not move" report has the same shape: PlayerController.Input is disabled and the
// thing that disabled it is gone. What makes this decidable is that the four legitimate holders are
// all askable — a menu is up, a cutscene is playing, the player is dead, the player is riding — so
// "none of those, for several seconds" is not a guess.
//
// cutsceneRunning is deliberately NOT PlayerController.InCutsceneMode. That flag is the SYMPTOM: it
// is how a menu, a cutscene and a leaked scope all express themselves, so reading it here would make
// the guard refuse to fire in exactly the case it exists for. Ask the cutscene director instead.
namespace SpaceGame.Core.Safety
{
    public static class InputRestoreRule
    {
        /// <param name="held">
        /// Is the player's own body being held down — netted, tied, frozen, foamed, swallowed, or
        /// simply flat on its back after a blast?
        ///
        /// <para>
        /// The fifth legitimate holder, and the one that was missing. Every one of those goes
        /// through <c>BodyHold</c> to <c>PlayerRagdoll.HoldDown</c>, whose <c>Suspend</c> switches
        /// the controller's input off — so any of them that outlasts the timeout was being handed
        /// the controls back mid-effect, and the player then walked around invisible inside a
        /// singularity or upright inside a block of ice. <c>Frozen</c> and <c>Foamed</c> are ten
        /// seconds each and have always tripped it; the bottled singularity is what finally made
        /// somebody read the log.
        /// </para>
        /// <para>
        /// It is asked of the RAGDOLL rather than of the status system, because the ragdoll is what
        /// actually took the input. A future condition that suppresses a body some other way is
        /// covered the day it goes through the same door, and honestly not covered before then.
        /// </para>
        /// </param>
        public static bool ShouldRestore(bool inputEnabled, bool menuActive, bool cutsceneRunning,
                                         bool isDead, bool mounted, bool held,
                                         float stuckSeconds, float timeoutSeconds)
        {
            if (inputEnabled) return false;
            if (menuActive || cutsceneRunning || isDead || mounted || held) return false;

            // Bounded rather than immediate: input is legitimately off for a frame or two during
            // every handover — mounting, a scene transition, a respawn — and a guard that fired on
            // that would fight the game instead of repairing it.
            return stuckSeconds >= timeoutSeconds;
        }
    }
}
