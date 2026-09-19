// Where a sky vessel sets a war party down near its quarry.
//
// Land first: a footprint that is flat ENOUGH — the user wants this generous, so slope and bumps
// are judged separately (a 20° hillside with small rocks lands; a boulder field does not) — clear
// overhead, with NavMesh where the ramp touches down. Otherwise hover a few metres over open ground
// and drop the party onto the NavMesh below. Null only when neither exists anywhere on the rings.
//
// Pure over IGroundProbe so the rules are tested against a height function, not a scene;
// PhysicsGroundProbe is the scene.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public enum DropMode { Land, Hover }

    /// <summary>
    /// A drop-off. <see cref="Point"/> is where the vessel's keel comes to rest: the ground at the
    /// footprint centre for <see cref="DropMode.Land"/>, the hover position above everything under
    /// the hull for <see cref="DropMode.Hover"/>. <see cref="UnloadPoint"/> is on the NavMesh: the
    /// foot of the ramp when landed, the spot below the hull when hovering.
    /// </summary>
    public readonly struct DropSite
    {
        public Vector3 Point { get; }
        public DropMode Mode { get; }
        public Vector3 UnloadPoint { get; }

        public DropSite(Vector3 point, DropMode mode, Vector3 unloadPoint)
        {
            Point = point;
            Mode = mode;
            UnloadPoint = unloadPoint;
        }
    }

    /// <summary>What the finder asks of the world. Every query ignores trigger colliders.</summary>
    public interface IGroundProbe
    {
        /// <summary>The ground straight below (or above) <paramref name="xzPoint"/>; its y is only a hint.</summary>
        bool TryGround(Vector3 xzPoint, out Vector3 point, out Vector3 normal);

        /// <summary>
        /// True when nothing solid is inside the upright square column of half-width
        /// <paramref name="radius"/> that stands on <paramref name="center"/> and is
        /// <paramref name="height"/> tall — the whole footprint, rim included, at every height.
        /// </summary>
        bool IsClear(Vector3 center, float radius, float height);

        bool TryNavMesh(Vector3 point, float maxDistance, out Vector3 onMesh);
    }

    [Serializable]
    public struct LandingSettings
    {
        [Tooltip("Innermost candidate ring around the quarry. Close enough to fight, far enough that " +
                 "the vessel is not landing on top of it.")]
        public float ringMin;

        [Tooltip("Outermost candidate ring. The middle of ringMin..ringMax is the preferred distance.")]
        public float ringMax;

        [Tooltip("Candidates on each ring, evenly spaced, the first on the side the vessel comes from.")]
        public int candidatesPerRing;

        [Tooltip("Rings between ringMin and ringMax inclusive.")]
        public int rings;

        [Tooltip("Steepest ground a vessel still lands on: the slope of the plane fitted through " +
                 "every footprint sample, so one tilted stone under the centre does not decide it.")]
        [Range(0f, 89f)]
        public float maxSlopeDegrees;

        [Tooltip("How far the footprint may deviate from its fitted slope plane, top bump to bottom " +
                 "dip. Measured against the slope, not the horizontal, so a smooth hillside is " +
                 "judged by its slope alone.")]
        public float maxHeightSpread;

        [Tooltip("Height of the empty cylinder the hull needs above the footprint.")]
        public float clearanceHeight;

        [Tooltip("Hover drop height above the highest ground under the hull.")]
        public float hoverHeight;

        [Tooltip("How far from the ramp end (Land) or the ground below (Hover) NavMesh may be and " +
                 "still count as somewhere the party can walk from.")]
        public float navMeshReach;

        [Tooltip("Score penalty, in metres of ring error, for a candidate directly opposite the " +
                 "approach; scaled down to zero on the approach side. Small, so a better ring " +
                 "distance still beats a nearer side.")]
        public float farSidePenalty;

        public static LandingSettings Default => new LandingSettings
        {
            ringMin = 40f,
            ringMax = 90f,
            candidatesPerRing = 16,
            rings = 3,
            maxSlopeDegrees = 22f,
            maxHeightSpread = 3.5f,
            clearanceHeight = 25f,
            hoverHeight = 4f,
            navMeshReach = 4f,
            farSidePenalty = 10f,
        };
    }

    public static class LandingSiteFinder
    {
        // Centre + this many on the rim is the footprint the brief specifies; not a tunable.
        private const int RimSamples = 8;

        private readonly struct Candidate
        {
            public readonly Vector3 Position;
            public readonly float Score;
            public readonly int Order;

            public Candidate(Vector3 position, float score, int order)
            {
                Position = position;
                Score = score;
                Order = order;
            }
        }

        public static DropSite? Find(Vector3 quarry, Vector3 approachFrom, float footprintRadius,
                                     in LandingSettings s, IGroundProbe probe)
        {
            DropSite? hover = null;
            var footprint = new Footprint();
            foreach (Candidate candidate in RankedCandidates(quarry, approachFrom, s))
            {
                if (!probe.TryGround(candidate.Position, out Vector3 ground, out _)) continue;

                footprint.Sample(ground, quarry, footprintRadius, probe);
                if (TryLand(footprint, footprintRadius, s, probe, out DropSite landed)) return landed;
                if (!hover.HasValue && TryHover(footprint, footprintRadius, s, probe, out DropSite hovering))
                    hover = hovering;
            }
            return hover;
        }

        /// <summary>
        /// Whether <paramref name="site"/> still has the headroom it was chosen with — re-asked while
        /// the vessel flies to it, because something may have moved in since. Judged exactly as
        /// <see cref="Find"/> judged it: a Land site's column stands on its highest footprint sample,
        /// a Hover site's on its hover point.
        /// </summary>
        public static bool IsStillClear(in DropSite site, Vector3 quarry, float footprintRadius,
                                        in LandingSettings s, IGroundProbe probe)
        {
            if (site.Mode == DropMode.Hover) return probe.IsClear(site.Point, footprintRadius, s.clearanceHeight);

            var footprint = new Footprint();
            footprint.Sample(site.Point, quarry, footprintRadius, probe);
            Vector3 centre = site.Point;
            return probe.IsClear(new Vector3(centre.x, footprint.Top, centre.z), footprintRadius, s.clearanceHeight);
        }

        /// <summary>
        /// The up direction of the plane fitted through the footprint at <paramref name="centre"/> —
        /// the same fit that judged its slope, so a hull set down along it rests on the ground the
        /// finder accepted instead of floating on the downhill side. Up when a sample misses ground.
        /// </summary>
        public static Vector3 GroundNormal(Vector3 centre, float footprintRadius, IGroundProbe probe)
        {
            var footprint = new Footprint();
            footprint.Sample(centre, centre + Vector3.forward, footprintRadius, probe);
            if (!footprint.AllGrounded) return Vector3.up;

            footprint.FitPlane(out _, out _, out Vector3 normal);
            return normal;
        }

        private static List<Candidate> RankedCandidates(Vector3 quarry, Vector3 approachFrom, in LandingSettings s)
        {
            Vector3 approach = Flat(approachFrom - quarry);
            approach = approach.sqrMagnitude > Vector3.kEpsilon ? approach.normalized : Vector3.forward;

            int rings = Mathf.Max(1, s.rings);
            int perRing = Mathf.Max(1, s.candidatesPerRing);
            float preferred = (s.ringMin + s.ringMax) * 0.5f;
            var candidates = new List<Candidate>(rings * perRing);

            for (int ring = 0; ring < rings; ring++)
            {
                float radius = rings == 1 ? preferred : Mathf.Lerp(s.ringMin, s.ringMax, ring / (float)(rings - 1));
                for (int i = 0; i < perRing; i++)
                {
                    float angle = 360f * i / perRing;
                    Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * approach;
                    float farSide = (1f - Mathf.Cos(angle * Mathf.Deg2Rad)) * 0.5f;
                    float score = Mathf.Abs(radius - preferred) + s.farSidePenalty * farSide;
                    candidates.Add(new Candidate(quarry + direction * radius, score, candidates.Count));
                }
            }

            candidates.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.Order.CompareTo(b.Order));
            return candidates;
        }

        /// <summary>
        /// The ground under one candidate footprint: the centre plus <see cref="RimSamples"/> points on
        /// the rim, rim sample 0 facing the quarry (that is where the ramp comes down). One instance
        /// per <see cref="Find"/> call, refilled for each candidate.
        /// </summary>
        private sealed class Footprint
        {
            public Vector3 Centre;
            public readonly Vector3[] Rim = new Vector3[RimSamples];
            public readonly bool[] Grounded = new bool[RimSamples];

            public Vector3 RampFoot => Rim[0];

            public void Sample(Vector3 centre, Vector3 quarry, float radius, IGroundProbe probe)
            {
                Centre = centre;
                Vector3 toQuarry = Flat(quarry - centre);
                toQuarry = toQuarry.sqrMagnitude > Vector3.kEpsilon ? toQuarry.normalized : Vector3.forward;

                for (int i = 0; i < RimSamples; i++)
                {
                    Vector3 direction = Quaternion.AngleAxis(360f * i / RimSamples, Vector3.up) * toQuarry;
                    Grounded[i] = probe.TryGround(centre + direction * radius, out Rim[i], out _);
                }
            }

            public bool AllGrounded => Array.TrueForAll(Grounded, grounded => grounded);

            /// <summary>Highest grounded sample, centre included.</summary>
            public float Top
            {
                get
                {
                    float top = Centre.y;
                    for (int i = 0; i < RimSamples; i++)
                        if (Grounded[i]) top = Mathf.Max(top, Rim[i].y);
                    return top;
                }
            }

            /// <summary>
            /// Least-squares plane y = mean + gradX·dx + gradZ·dz through the centre and every rim
            /// sample (all must be grounded). Fitting all nine means no single sample — a stone face
            /// tilted under the centre, one rock on the rim — sets the slope. Returns the slope in
            /// degrees, the spread (highest minus lowest sample relative to that plane) and the
            /// plane's up normal.
            /// </summary>
            public void FitPlane(out float slopeDegrees, out float spread, out Vector3 normal)
            {
                float mean = Centre.y;
                for (int i = 0; i < RimSamples; i++) mean += Rim[i].y;
                mean /= RimSamples + 1;

                // Evenly spaced rim offsets sum to zero in x, z and x·z, so the normal equations
                // decouple into one ratio per axis. The centre's offset is zero and drops out.
                float xy = 0f, xx = 0f, zy = 0f, zz = 0f;
                for (int i = 0; i < RimSamples; i++)
                {
                    Vector3 offset = Rim[i] - Centre;
                    float height = Rim[i].y - mean;
                    xy += offset.x * height;
                    xx += offset.x * offset.x;
                    zy += offset.z * height;
                    zz += offset.z * offset.z;
                }
                float gradX = xx > Vector3.kEpsilon ? xy / xx : 0f;
                float gradZ = zz > Vector3.kEpsilon ? zy / zz : 0f;
                slopeDegrees = Mathf.Atan(Mathf.Sqrt(gradX * gradX + gradZ * gradZ)) * Mathf.Rad2Deg;
                normal = new Vector3(-gradX, 1f, -gradZ).normalized;

                float lowest = Centre.y - mean, highest = lowest;
                for (int i = 0; i < RimSamples; i++)
                {
                    Vector3 offset = Rim[i] - Centre;
                    float deviation = Rim[i].y - (mean + gradX * offset.x + gradZ * offset.z);
                    lowest = Mathf.Min(lowest, deviation);
                    highest = Mathf.Max(highest, deviation);
                }
                spread = highest - lowest;
            }
        }

        private static bool TryLand(Footprint footprint, float footprintRadius, in LandingSettings s,
                                    IGroundProbe probe, out DropSite site)
        {
            site = default;
            if (!footprint.AllGrounded) return false;

            footprint.FitPlane(out float slope, out float spread, out _);
            if (slope > s.maxSlopeDegrees || spread > s.maxHeightSpread) return false;

            Vector3 centre = footprint.Centre;
            if (!probe.IsClear(new Vector3(centre.x, footprint.Top, centre.z), footprintRadius, s.clearanceHeight))
                return false;
            if (!probe.TryNavMesh(footprint.RampFoot, s.navMeshReach, out Vector3 rampFoot)) return false;

            site = new DropSite(centre, DropMode.Land, rampFoot);
            return true;
        }

        private static bool TryHover(Footprint footprint, float footprintRadius, in LandingSettings s,
                                     IGroundProbe probe, out DropSite site)
        {
            site = default;
            Vector3 centre = footprint.Centre;
            var hoverPoint = new Vector3(centre.x, footprint.Top + s.hoverHeight, centre.z);
            if (!probe.IsClear(hoverPoint, footprintRadius, s.clearanceHeight)) return false;
            if (!probe.TryNavMesh(centre, s.navMeshReach, out Vector3 below)) return false;

            site = new DropSite(hoverPoint, DropMode.Hover, below);
            return true;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
