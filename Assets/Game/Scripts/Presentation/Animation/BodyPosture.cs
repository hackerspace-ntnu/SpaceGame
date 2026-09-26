using System;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// What a body is doing with its legs, as far as an action cares: whether a full-body clip may
    /// take it over, or only the arms may move. A flags set, so an action can fit several.
    /// Append only — action assets store the numbers.
    /// </summary>
    [Flags]
    public enum BodyPosture
    {
        Standing = 1,
        Moving = 2,
        Seated = 4,
        Airborne = 8
    }

    /// <summary>Reads a humanoid body's posture off the parameters its controller already carries.</summary>
    public static class BodyPostures
    {
        /// <summary>Every posture — what an arm or upper-body action fits unless it says otherwise.</summary>
        public const BodyPosture Any = BodyPosture.Standing | BodyPosture.Moving | BodyPosture.Seated | BodyPosture.Airborne;

        private static readonly int SpeedXHash = Animator.StringToHash(HumanoidParams.SpeedX);
        private static readonly int SpeedYHash = Animator.StringToHash(HumanoidParams.SpeedY);
        private static readonly int SeatedHash = Animator.StringToHash(HumanoidParams.Seated);
        private static readonly int GroundedHash = Animator.StringToHash(HumanoidParams.IsGrounded);
        private static readonly int GlidingHash = Animator.StringToHash(HumanoidParams.IsGliding);

        /// <summary>
        /// The posture <paramref name="animator"/> shows. Parameters rather than physics, because
        /// they are what every machine has for every body: the player's travel through its
        /// NetworkAnimator, an NPC's from its driver.
        /// </summary>
        /// <param name="movingAbove">Planar animator speed above which the body counts as moving.</param>
        public static BodyPosture Read(Animator animator, float movingAbove)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return BodyPosture.Standing;
            if (animator.GetBool(SeatedHash)) return BodyPosture.Seated;
            if (!animator.GetBool(GroundedHash) || animator.GetBool(GlidingHash)) return BodyPosture.Airborne;

            var speed = new Vector2(animator.GetFloat(SpeedXHash), animator.GetFloat(SpeedYHash));
            return speed.sqrMagnitude > movingAbove * movingAbove ? BodyPosture.Moving : BodyPosture.Standing;
        }
    }
}
