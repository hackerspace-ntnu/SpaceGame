using System;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Everything the humanoid controller is generated from except the actions: the locomotion
    /// set and its variants, the held-item poses, the gauntlet raise, the glide, and the blend
    /// times between them.
    ///
    /// <para>
    /// One asset, at <see cref="HumanoidControllerBuilder.ProfilePath"/>. Edit it, then run
    /// Tools/SpaceGame/Animation/Rebuild Humanoid Controller; the controller itself is never
    /// edited by hand, because the next rebuild writes over it.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Humanoid Animation Profile", fileName = "HumanoidAnimationProfile")]
    public sealed class HumanoidAnimationProfile : ScriptableObject
    {
        /// <summary>The pose the Upper Body layer holds for one <see cref="ItemGrip.HoldStyle"/>.</summary>
        [Serializable]
        public struct HoldPose
        {
            public ItemGrip.HoldStyle style;
            public AnimationClip clip;
        }

        /// <summary>Three clips blended over the look pitch (AimPitch): down, level, up.</summary>
        [Serializable]
        public struct PitchBlend
        {
            public AnimationClip down;
            public AnimationClip level;
            public AnimationClip up;
        }

        /// <summary>Blend times and thresholds of the generated transitions, in seconds.</summary>
        [Serializable]
        public sealed class Timings
        {
            public float intoAir = 0.05f;
            public float airToGround = 0.25f;
            public float intoCrouch = 0.15f;
            public float outOfCrouch = 0.25f;
            public float intoLanding = 0.15f;
            public float outOfLanding = 0.25f;

            [Tooltip("Normalized time of the landing clip at which it hands back to walking.")]
            [Range(0f, 1f)] public float landingExitTime = 0.82f;

            [Tooltip("The hard landing plays when the body lands immobilised with FallSpeed above this.")]
            public float landingFallSpeed = -6f;

            public float sit = 0.25f;
            public float hold = 0.15f;
            public float raise = 0.12f;
            public float glide = 0.25f;
        }

        [SerializeField] private LocomotionSet locomotion;

        [Tooltip("Archetype variants of the locomotion set. Each becomes an override controller " +
                 "named Humanoid_<set name>, which a prefab uses in place of the base controller.")]
        [SerializeField] private LocomotionSet[] locomotionVariants = Array.Empty<LocomotionSet>();

        [Tooltip("The Upper Body pose per held-item style. Each gets a mirrored twin for the left " +
                 "arm, and a Worn Left state.")]
        [SerializeField] private HoldPose[] holdPoses = Array.Empty<HoldPose>();

        [Tooltip("One arm raised at the look pitch — a gauntlet firing. The left arm plays it mirrored.")]
        [SerializeField] private PitchBlend raiseOneArm;

        [SerializeField] private PitchBlend raiseBothArms;

        [SerializeField] private AnimationClip glide;

        [Tooltip("Look pitch, in degrees, at which a pitch blend reaches its Up and Down clips.")]
        [SerializeField, Min(1f)] private float aimPitchRange = 45f;

        [SerializeField] private Timings timings = new Timings();

        public LocomotionSet Locomotion => locomotion;
        public LocomotionSet[] LocomotionVariants => locomotionVariants;
        public HoldPose[] HoldPoses => holdPoses;
        public PitchBlend RaiseOneArm => raiseOneArm;
        public PitchBlend RaiseBothArms => raiseBothArms;
        public AnimationClip Glide => glide;
        public float AimPitchRange => aimPitchRange;
        public Timings Timing => timings;
    }
}
