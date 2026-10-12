using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The nearest spot a rigid, level hull can actually sit down on.
    ///
    /// <para>
    /// An authored landing point says where a ship should be, not whether a ship fits there. A hull
    /// that cannot tilt rests on the highest ground it spans, so on any real slope it either hangs
    /// over the low side or buries its high side — and because the arrival persists the wreck
    /// exactly where the descent leaves it, that is permanent. Nudging the touchdown a few tens of
    /// metres onto ground the hull fits costs nothing anybody can see and is the difference between
    /// a landed ship and one parked in mid-air.
    /// </para>
    ///
    /// <para>
    /// Rings rather than a random scatter, and every ring walked in the same order: the search runs
    /// on the server and its answer is replicated, but it is also re-run when a formation is rebuilt,
    /// and a search that returned a different spot the second time would move a ship that peers had
    /// already been told about.
    /// </para>
    /// </summary>
    public static class LevelGroundSearch
    {
        /// <summary>Candidate spots per ring. Eight is a compass rose — enough to find a flat shelf beside a dune, few enough to be cheap.</summary>
        public const int PointsPerRing = 8;

        /// <summary>
        /// The spot nearest <paramref name="preferredXZ"/> where the ground under the hull is level
        /// enough to rest on, or the flattest spot found when nothing meets the tolerance.
        ///
        /// <para>
        /// Returns false only when nothing anywhere in the search could be measured at all, which in
        /// a streamed world means "not yet" — the chunks have not arrived — and never "there is
        /// nowhere". The distinction matters: a caller that treated an unstreamed world as a refusal
        /// would land a ship on a guess.
        /// </para>
        ///
        /// <para>
        /// Settling for the flattest spot rather than refusing is deliberate. A world whose terrain
        /// is all slope still has to open, and a ship on the least bad ground within
        /// <paramref name="searchRadius"/> is a better answer than a match that never starts.
        /// </para>
        /// </summary>
        public static bool TryFind(Vector2 preferredXZ, float yaw, Vector2 extents,
                                   float maxSpread, float searchRadius, float ringStep,
                                   HullFootprint.GroundSampler sample,
                                   out Vector2 groundXZ, out float groundY) =>
            TryFind(preferredXZ, yaw, extents, maxSpread, searchRadius, ringStep, sample,
                    out groundXZ, out groundY, out _);

        /// <summary>
        /// The same search, also saying whether the spot it answered with actually met
        /// <paramref name="maxSpread"/>. A caller for whom "the least bad ground" is not good enough
        /// — a walking city choosing where to stop and put its crew ashore, which can always choose
        /// somewhere else — reads <paramref name="level"/> and refuses the rest.
        /// </summary>
        public static bool TryFind(Vector2 preferredXZ, float yaw, Vector2 extents,
                                   float maxSpread, float searchRadius, float ringStep,
                                   HullFootprint.GroundSampler sample,
                                   out Vector2 groundXZ, out float groundY, out bool level)
        {
            groundXZ = preferredXZ;
            groundY = 0f;
            level = false;

            if (sample == null) return false;

            var scratch = new Vector2[HullFootprint.SampleCount];

            bool haveBest = false;
            float bestSpread = float.PositiveInfinity;

            int rings = ringStep > 0f ? Mathf.FloorToInt(Mathf.Max(0f, searchRadius) / ringStep) : 0;

            for (int ring = 0; ring <= rings; ring++)
            {
                float radius = ring * ringStep;
                int points = ring == 0 ? 1 : PointsPerRing;

                for (int i = 0; i < points; i++)
                {
                    float bearing = i * (360f / PointsPerRing) * Mathf.Deg2Rad;
                    Vector2 at = preferredXZ + new Vector2(Mathf.Sin(bearing), Mathf.Cos(bearing)) * radius;

                    HullFootprint.Ground ground =
                        HullFootprint.Measure(at, yaw, extents, sample, scratch);

                    // Anything less than the whole footprint means part of the hull is over ground
                    // nothing can vouch for, and that is precisely where the ground might be higher
                    // than everything measured. Skipped rather than scored.
                    if (!ground.Complete) continue;

                    if (ground.Spread < bestSpread)
                    {
                        bestSpread = ground.Spread;
                        groundXZ = at;
                        groundY = ground.Highest;
                        haveBest = true;
                    }

                    // First good enough wins, which is what keeps the ship near where the arena
                    // authored it: a later ring might be a metre flatter and a hundred metres away.
                    if (ground.Spread <= maxSpread)
                    {
                        level = true;
                        return true;
                    }
                }
            }

            return haveBest;
        }

        /// <summary>
        /// The flattest of <paramref name="candidates"/> whose whole footprint is measurable, or false
        /// when none of them is level enough (or none could be measured at all).
        ///
        /// <para>
        /// The other half of the ring search's policy. That one stops at the first spot good enough,
        /// because a ship should land where the arena authored it; this one looks at every candidate,
        /// for a choice made once, offline, among places that are all equally where the caller wants
        /// to be. Ties go to the earlier candidate, so the caller's order is its preference and a
        /// re-run picks the same one.
        /// </para>
        /// </summary>
        public static bool TryFindFlattest(IReadOnlyList<Vector2> candidates, float yaw, Vector2 extents,
                                           float maxSpread, HullFootprint.GroundSampler sample,
                                           out int index, out float spread)
        {
            index = -1;
            spread = float.PositiveInfinity;

            if (candidates == null || sample == null) return false;

            var scratch = new Vector2[HullFootprint.SampleCount];

            for (int i = 0; i < candidates.Count; i++)
            {
                HullFootprint.Ground ground = HullFootprint.Measure(candidates[i], yaw, extents, sample, scratch);

                // Incomplete is skipped for the ring search's reason: the unmeasured part is exactly
                // where the ground might be worst.
                if (!ground.Complete || ground.Spread > maxSpread || ground.Spread >= spread) continue;

                spread = ground.Spread;
                index = i;
            }

            return index >= 0;
        }
    }
}
