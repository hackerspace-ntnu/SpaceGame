// Hands the player their controls back when nothing is holding them.
//
// The last line of defence behind StuckScopeGuard, and it catches a different set of causes: a
// cutscene action that threw before its ExitCutsceneMode, a mount teardown that never ran, a death
// handler that disabled input and then failed to respawn. All of them look the same to the player —
// the world is there, the camera works, and nothing responds.
//
// It measures the outcome and asks the five legitimate holders directly. It never reads
// PlayerController.InCutsceneMode as the question, because that flag is how every one of those
// holders — including the broken one — expresses itself; reading it would make the guard refuse to
// fire in exactly the case it exists for. See InputRestoreRule.
//
// THE FIFTH HOLDER IS THE BODY ITSELF. A player who is netted, tied, frozen, foamed, swallowed or
// simply flat on their back has had their input taken by PlayerRagdoll, legitimately, and several
// of those last longer than this timeout. PlayerRagdoll.IsHeldOrDown is a claim held by a named
// holder rather than a symptom, which is why it is safe to ask where InCutsceneMode is not.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Safety
{
    public sealed class InputRestoreGuard : ISessionGuard
    {
        /// <summary>
        /// How long the controls may be held by nothing before they are handed back.
        ///
        /// Generous on purpose. Input is legitimately off for a moment during every handover, and
        /// this is a repair for a session that is otherwise over — a few seconds of certainty costs
        /// far less than fighting a mount for its own rider.
        /// </summary>
        public const float TimeoutSeconds = 5f;

        private float stuckSeconds;

        public string Name => "InputRestore";

        public void Check(float interval)
        {
            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null || player.Input == null)
            {
                stuckSeconds = 0f;
                return;
            }

            bool cutsceneRunning = CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying;
            bool mounted = MountModule.LocalRiderMount != null;

            // From the parent, because the controller and the ragdoll need not be the same object,
            // and resolved every check rather than cached: this guard outlives any one player.
            PlayerRagdoll ragdoll = player.GetComponentInParent<PlayerRagdoll>();
            bool held = ragdoll != null && ragdoll.IsHeldOrDown;

            stuckSeconds = player.Input.enabled ? 0f : stuckSeconds + interval;

            if (!InputRestoreRule.ShouldRestore(player.Input.enabled, GameplayMenuScope.IsActive,
                                                cutsceneRunning, player.IsDead, mounted, held,
                                                stuckSeconds, TimeoutSeconds))
                return;

            Debug.LogError($"[InputRestoreGuard] The local player has had no input for " +
                           $"{stuckSeconds:F1}s with no menu, cutscene, death, mount or hold on " +
                           "their body to explain it. Handing the controls back — something took " +
                           "them and did not give them back, and that is the real bug.", player);

            // Through ExitCutsceneMode rather than by writing Input.enabled: that method is the
            // project's existing primitive and it also restores look, movement and the cursor, which
            // a bare flag would leave in whatever state the failure left them.
            if (player.InCutsceneMode) player.ExitCutsceneMode();
            else player.Input.enabled = true;

            stuckSeconds = 0f;
        }
    }
}
