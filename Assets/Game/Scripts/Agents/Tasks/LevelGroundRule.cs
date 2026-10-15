// How level a task's destination must be, for a group that stops as a whole.
//
// A person or an animal stops on any ground. A walking city does not: at a stop its houses hold
// their marching slots across a couple of hundred metres and put their crew ashore at the gangways,
// so a stop on a dune face leaves houses standing at different heights and crew stepping off onto a
// slope. The rule is off by default (footprintRadius 0), so every task authored before it behaves
// exactly as it did.
using System;
using UnityEngine;

namespace SpaceGame.Agents
{
    [Serializable]
    public struct LevelGroundRule
    {
        [Tooltip("Half-width of the square of ground that must be level round the destination: the " +
                 "whole group's extent at a stop. 0 turns the rule off and any ground will do.")]
        public float footprintRadius;

        [Tooltip("Steepest average grade, in degrees, allowed across the footprint. Measured as the " +
                 "height between its highest and lowest of nine samples, over its width.")]
        public float maxSlopeDegrees;

        [Tooltip("How far round a candidate to look for level ground before rejecting it, in metres. " +
                 "Nudging a stop onto the shelf beside a dune is cheaper than a new candidate.")]
        public float searchRadius;

        [Tooltip("Spacing of the rings that search walks, in metres.")]
        public float searchStep;

        [Tooltip("Candidates tried before the task gives up for now and retries later.")]
        public int attempts;

        [Tooltip("How far above or below the candidate's own height a footprint sample may find the " +
                 "NavMesh, in metres. Anything farther counts as unmeasurable, which rejects the spot.")]
        public float sampleReach;

        [Tooltip("How far sideways from the asked point the NavMesh may answer and still count as the " +
                 "ground there, in metres. The NavMesh has holes where rocks and buildings stand, and " +
                 "the nearest edge of a hole is not the ground inside it.")]
        public float sampleTolerance;

        /// <summary>True when the rule asks for anything at all.</summary>
        public bool Enabled => footprintRadius > 0f;

        /// <summary>The footprint as <see cref="SpaceGame.Gameplay.HullFootprint"/> wants it: a square, so any heading fits.</summary>
        public Vector2 Extents => new Vector2(footprintRadius, footprintRadius);

        /// <summary>
        /// The largest height difference across the footprint that still counts as level: the rise of
        /// <see cref="maxSlopeDegrees"/> over the footprint's width. A slope that runs corner to corner
        /// rises up to √2 times that and is refused a little early, which errs the safe way.
        /// </summary>
        public float MaxSpread => Mathf.Tan(Mathf.Clamp(maxSlopeDegrees, 0f, 89f) * Mathf.Deg2Rad) * 2f * footprintRadius;
    }
}
