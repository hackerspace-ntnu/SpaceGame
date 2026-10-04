using System;
using SpaceGame.Items;
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
            if (animator.GetBool(HumanoidParams.SeatedHash)) return BodyPosture.Seated;
            if (!animator.GetBool(GroundedHash) || animator.GetBool(GlidingHash)) return BodyPosture.Airborne;

            var speed = new Vector2(animator.GetFloat(SpeedXHash), animator.GetFloat(SpeedYHash));
            return speed.sqrMagnitude > movingAbove * movingAbove ? BodyPosture.Moving : BodyPosture.Standing;
        }
    }

    /// <summary>Which arms are in use. A flags set; <see cref="Right"/> and <see cref="Left"/> are the body's own sides.</summary>
    [Flags]
    public enum BodyArms
    {
        None = 0,
        Right = 1,
        Left = 2,
        Both = Right | Left
    }

    /// <summary>Reads what a humanoid body's hands are doing off the parameters its controller already carries.</summary>
    public static class BodyHands
    {
        private static readonly int HoldStyleHash = Animator.StringToHash(HumanoidParams.HoldStyle);
        private static readonly int HoldMirrorHash = Animator.StringToHash(HumanoidParams.HoldMirror);

        /// <summary>
        /// The arms <paramref name="animator"/>'s held item occupies. Only the one-armed hold styles
        /// leave the other arm alone; every older style is a gun clip that poses both.
        /// </summary>
        public static BodyArms Busy(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return BodyArms.None;

            switch ((ItemGrip.HoldStyle)animator.GetInteger(HoldStyleHash))
            {
                case ItemGrip.HoldStyle.None: return BodyArms.None;
                case ItemGrip.HoldStyle.Carry:
                case ItemGrip.HoldStyle.Ready:
                    return animator.GetBool(HoldMirrorHash) ? BodyArms.Left : BodyArms.Right;
                default: return BodyArms.Both;
            }
        }
    }
}
