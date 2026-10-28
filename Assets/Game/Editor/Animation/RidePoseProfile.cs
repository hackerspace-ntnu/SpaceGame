using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// What the riding clip is generated from: a real seated mocap loop, and how far each humanoid
    /// muscle is moved from it to straddle an animal instead of sitting on a chair.
    ///
    /// <para>
    /// One asset, at <see cref="RideClipGenerator.ProfilePath"/>. Edit it, then run
    /// Tools/SpaceGame/Animation/Generate Ride Clip; the clip and its action are output and the next
    /// run writes over any hand edit to them.
    /// </para>
    /// <para>
    /// No riding clip exists in any library this project has, and a pose built by rotating bones
    /// over the STANDING idle read as a mannequin folded onto the saddle. The seated loop keeps a
    /// person's own spine, shoulders and breathing; only the hips and knees change. Offsets are in
    /// muscle units (a fraction of the joint's range). Measured on the astronaut 2026-10-21:
    /// Upper Leg In-Out positive opens the legs, Upper Leg Front-Back positive lowers the knee,
    /// Lower Leg Stretch negative bends it.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Ride Pose Profile", fileName = "RidePoseProfile")]
    public sealed class RidePoseProfile : ScriptableObject
    {
        /// <summary>One muscle moved from the seated source for the whole loop.</summary>
        [Serializable]
        public struct MuscleOffset
        {
            [Tooltip("A humanoid muscle, exactly as HumanTrait.MuscleName spells it. A Left muscle " +
                     "is applied to its Right twin too when mirrorLeftToRight is on.")]
            public string muscle;

            [Tooltip("Added to every key of that muscle's curve, in muscle units, signed.")]
            public float offset;
        }

        [Tooltip("The seated loop the ride is made from. Must be a looping humanoid clip.")]
        [SerializeField] private AnimationClip source;

        [Tooltip("The action asset's name, and the clip's. MountedRiderPose plays it by this name.")]
        [SerializeField] private string actionName = "Ride";

        [Tooltip("Apply every Left muscle's offset to the Right one as well, so the rider sits square.")]
        [SerializeField] private bool mirrorLeftToRight = true;

        [Tooltip("Blend into the ride on mounting and out of it on dismounting, in seconds.")]
        [SerializeField, Min(0f)] private float fadeIn = 0.25f;
        [SerializeField, Min(0f)] private float fadeOut = 0.25f;

        [SerializeField] private MuscleOffset[] offsets = Array.Empty<MuscleOffset>();

        public AnimationClip Source => source;
        public string ActionName => actionName;
        public bool MirrorLeftToRight => mirrorLeftToRight;
        public float FadeIn => fadeIn;
        public float FadeOut => fadeOut;
        public IReadOnlyList<MuscleOffset> Offsets => offsets;
    }
}
