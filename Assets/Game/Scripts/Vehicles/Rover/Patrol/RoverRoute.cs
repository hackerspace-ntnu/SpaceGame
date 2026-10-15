using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// A closed loop a rover drives, as a function of distance: smooth between its control points, with no scene in it.
    /// Control points are on the ground plane (x, z); the ground's height is the rover's own business. The loop is a uniform
    /// Catmull-Rom spline, sampled once into an arc-length table, so <see cref="PointAt"/> is the same on every machine that
    /// builds it from the same control points.
    /// </summary>
    public sealed class RoverRoute
    {
        private const int SamplesPerSegment = 24;
        public const int MinControlPoints = 4;

        private readonly Vector2[] samples;
        private readonly float[] distanceAt;

        public RoverRoute(IReadOnlyList<Vector2> controlPoints)
        {
            if (controlPoints == null || controlPoints.Count < MinControlPoints)
                throw new ArgumentException($"A rover route needs at least {MinControlPoints} control points.", nameof(controlPoints));

            int count = controlPoints.Count;
            samples = new Vector2[count * SamplesPerSegment];
            distanceAt = new float[samples.Length + 1];
            for (int segment = 0; segment < count; segment++)
            {
                Vector2 before = controlPoints[(segment + count - 1) % count], from = controlPoints[segment];
                Vector2 to = controlPoints[(segment + 1) % count], after = controlPoints[(segment + 2) % count];
                for (int step = 0; step < SamplesPerSegment; step++)
                    samples[segment * SamplesPerSegment + step] = CatmullRom(before, from, to, after, step / (float)SamplesPerSegment);
            }

            for (int i = 0; i < samples.Length; i++)
                distanceAt[i + 1] = distanceAt[i] + Vector2.Distance(samples[i], samples[(i + 1) % samples.Length]);
            Length = distanceAt[samples.Length];
        }

        /// <summary>Metres once round.</summary>
        public float Length { get; }

        /// <summary>The loop's sampled points, in driving order, for a caller that has to check the ground under all of it.</summary>
        public IReadOnlyList<Vector2> Samples => samples;

        /// <summary>Where the rover is after driving <paramref name="distance"/> metres from the start; wraps, and negative drives the other way round.</summary>
        public Vector2 PointAt(float distance)
        {
            float along = Wrap(distance);
            int index = Search(along);
            float span = distanceAt[index + 1] - distanceAt[index];
            float t = span > Mathf.Epsilon ? (along - distanceAt[index]) / span : 0f;
            return Vector2.Lerp(samples[index], samples[(index + 1) % samples.Length], t);
        }

        /// <summary>The direction of travel at <paramref name="distance"/>, a unit vector in driving order.</summary>
        public Vector2 HeadingAt(float distance)
        {
            int index = Search(Wrap(distance));
            Vector2 heading = samples[(index + 1) % samples.Length] - samples[index];
            return heading.sqrMagnitude > Mathf.Epsilon ? heading.normalized : Vector2.up;
        }

        /// <summary>
        /// The control point candidate for slot <paramref name="slot"/> of <paramref name="slots"/>, on a ring round <paramref name="centre"/>
        /// between <paramref name="inner"/> and <paramref name="outer"/> metres out. <paramref name="alternative"/> is the next try for a
        /// slot whose first choice was no good. Seeded, so every machine proposes the same points in the same order.
        /// </summary>
        public static Vector2 Candidate(int seed, int slot, int slots, int alternative, Vector2 centre, float inner, float outer)
        {
            var rng = new System.Random(unchecked((seed * 73856093) ^ (slot * 19349663) ^ (alternative * 83492791)));
            float angle = (slot + (float)rng.NextDouble() * 0.6f - 0.3f) / slots * Mathf.PI * 2f;
            float radius = Mathf.Lerp(inner, outer, (float)rng.NextDouble());
            return centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private float Wrap(float distance)
        {
            float along = distance % Length;
            return along < 0f ? along + Length : along;
        }

        // The last sample that starts at or before `along`.
        private int Search(float along)
        {
            int low = 0, high = samples.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (distanceAt[middle] <= along) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
