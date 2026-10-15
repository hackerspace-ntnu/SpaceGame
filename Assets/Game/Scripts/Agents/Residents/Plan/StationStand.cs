// Where a resident works a spot from, so the tool lands on what the spot is for.
//
// A work clip moves the tool to a measured place relative to the body (CharacterAction.Reach, taken at the clip's Contact
// mark), and a spot names its target (a farm bed, an ore node, a stove). The authored stand point is a walkable place
// beside the prop, found by eye; this moves it toward the target until the measured reach lands the tool on it. Pure, so
// the rule is tested as a table and the exit check can run it over every authored spot.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Agents.Residents
{
    public static class StationStand
    {
        /// <summary>
        /// How far in front of the body the tool lands, for the loops <paramref name="tagged"/> (the actions a spot's hold cue asks
        /// for): the mean over those that loop, fit a standing body and have a measured reach. False when none is measured.
        /// <paramref name="spread"/> is the distance between the shortest and the longest of them: what a station cannot get right
        /// for every clip a resident may draw.
        /// </summary>
        public static bool TryForwardReach(IReadOnlyList<CharacterAction> tagged, out float forward, out float spread)
        {
            float sum = 0f, low = float.MaxValue, high = float.MinValue;
            int count = 0;
            foreach (CharacterAction action in tagged)
            {
                if (action == null || !action.Loops || !action.HasReach || !action.Fits(BodyPosture.Standing)) continue;

                float z = action.Reach.z;
                sum += z;
                low = Mathf.Min(low, z);
                high = Mathf.Max(high, z);
                count++;
            }

            forward = count > 0 ? sum / count : 0f;
            spread = count > 0 ? high - low : 0f;
            return count > 0;
        }

        /// <summary>
        /// The stand point for a body facing <paramref name="target"/> from <paramref name="authored"/>: moved toward the target until
        /// the tool, <paramref name="forwardReach"/> ahead of the body, lands on it — by at most <paramref name="maxShift"/>, never
        /// closer than <paramref name="minDistance"/>, and never farther than where it was authored. The authored point's height is kept.
        /// </summary>
        public static Vector3 Derive(Vector3 authored, Vector3 target, float forwardReach, float maxShift, float minDistance)
        {
            Vector3 toTarget = Flat(target - authored);
            float distance = toTarget.magnitude;
            if (distance <= minDistance) return authored;

            float wanted = Mathf.Min(distance, Mathf.Max(minDistance, Mathf.Max(forwardReach, distance - maxShift)));
            return authored + toTarget / distance * (distance - wanted);
        }

        /// <summary>How far short of (negative: past) the target the tool lands for a body standing at <paramref name="stand"/>.</summary>
        public static float Shortfall(Vector3 stand, Vector3 target, float forwardReach) => Flat(target - stand).magnitude - forwardReach;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
