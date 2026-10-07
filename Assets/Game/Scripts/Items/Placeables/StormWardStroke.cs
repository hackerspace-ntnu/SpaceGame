// The storm ward's stroke: the emitter ring rides up its mast, slams down past where it rests, and
// settles. The slam's bottom is the IMPACT, the moment the shockwave leaves, so the pulse reads as
// the ring striking it out of the ground rather than as a timer going off (GDC-L1-FEEL-0004).
//
// A pure function of time either side of the impact, so it can be pinned by a test and every machine
// draws the same stroke from its own pulse timer with nothing sent.
using System;
using UnityEngine;

namespace SpaceGame.Items
{
    [Serializable]
    public class StormWardStroke
    {
        [Tooltip("Metres the ring rises above its rest before the slam. The ward's ring rests at 0.80 m and the " +
                 "vane at the top of its mast starts at 0.90 m, so past 0.08 the ring cuts through the vane.")]
        [SerializeField, Min(0f)] private float raiseHeight = 0.08f;

        [Tooltip("Metres the ring drives below its rest at the impact, before settling back.")]
        [SerializeField, Min(0f)] private float dipDepth = 0.05f;

        [Tooltip("Seconds the rise takes. Eased, so it reads as winding up.")]
        [SerializeField, Min(0.01f)] private float raiseDuration = 0.5f;

        [Tooltip("Seconds from the top of the rise to the impact. Short, and accelerating, so it snaps.")]
        [SerializeField, Min(0.01f)] private float slamDuration = 0.06f;

        [Tooltip("Seconds from the impact back to rest.")]
        [SerializeField, Min(0.01f)] private float settleDuration = 0.25f;

        /// <summary>Seconds before an impact the stroke starts moving.</summary>
        public float Lead => raiseDuration + slamDuration;

        /// <summary>The shortest pulse interval the whole stroke fits in.</summary>
        public float Length => Lead + settleDuration;

        /// <summary>
        /// Metres above (positive) or below the ring's rest, given the seconds until the next impact
        /// and the seconds since the last one.
        /// </summary>
        public float Offset(float untilImpact, float sinceImpact)
        {
            if (untilImpact <= slamDuration)
            {
                float t = 1f - Mathf.Clamp01(untilImpact / slamDuration);
                return Mathf.Lerp(raiseHeight, -dipDepth, t * t);
            }

            if (untilImpact <= Lead)
                return raiseHeight * Mathf.SmoothStep(0f, 1f, (Lead - untilImpact) / raiseDuration);

            if (sinceImpact < settleDuration)
            {
                float t = sinceImpact / settleDuration;
                return -dipDepth * (1f - t) * (1f - t);
            }

            return 0f;
        }
    }
}
