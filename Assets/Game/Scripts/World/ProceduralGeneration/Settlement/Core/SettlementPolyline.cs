// Polyline arithmetic in the XZ plane shared by everything that lays pieces along a line: a street's
// centre line, a terrace wall's contour. Arc length s is metres along the line from its first point.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementPolyline
    {
        public static float Length(IReadOnlyList<Vector2> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
            return length;
        }

        public static Vector2 PointAt(IReadOnlyList<Vector2> points, float s)
        {
            for (int i = 1; i < points.Count; i++)
            {
                float segment = Vector2.Distance(points[i - 1], points[i]);
                if (s <= segment || i == points.Count - 1)
                    return Vector2.Lerp(points[i - 1], points[i], segment > 0f ? Mathf.Clamp01(s / segment) : 0f);
                s -= segment;
            }
            return points[0];
        }

        /// <summary>Unit direction of the line at <paramref name="s"/>, averaged over <paramref name="window"/> metres around it.</summary>
        public static Vector2 TangentAt(IReadOnlyList<Vector2> points, float s, float window)
        {
            float length = Length(points);
            Vector2 d = PointAt(points, Mathf.Min(s + window * 0.5f, length)) - PointAt(points, Mathf.Max(s - window * 0.5f, 0f));
            return d.sqrMagnitude > 1e-10f ? d.normalized : Vector2.up;
        }

        /// <summary>Arc length to the point of the line nearest <paramref name="p"/>.</summary>
        public static float Project(IReadOnlyList<Vector2> points, Vector2 p)
        {
            float best = float.PositiveInfinity, arc = 0f, bestArc = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1], ab = points[i] - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best)
                {
                    best = d;
                    bestArc = arc + t * ab.magnitude;
                }
                arc += ab.magnitude;
            }
            return bestArc;
        }

        /// <summary>
        /// Arc length of the first point after <paramref name="from"/> lying exactly <paramref name="chord"/>
        /// metres in a straight line from the point at <paramref name="from"/>; false past the end. Pieces laid
        /// chord to chord meet corner to corner on a curve, where pieces laid arc to arc would open a gap.
        /// </summary>
        public static bool NextChord(IReadOnlyList<Vector2> points, float from, float chord, out float to)
        {
            Vector2 start = PointAt(points, from);
            float arc = 0f;
            to = from;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1], b = points[i];
                float segment = Vector2.Distance(a, b);
                float segmentEnd = arc + segment;
                if (segmentEnd > from && Vector2.Distance(start, b) >= chord && segment > 0f)
                {
                    // Solve |a + t(b - a) - start| = chord for the larger root on this segment.
                    Vector2 d = (b - a) / segment, m = a - start;
                    float bq = Vector2.Dot(m, d), c = m.sqrMagnitude - chord * chord;
                    float t = -bq + Mathf.Sqrt(Mathf.Max(0f, bq * bq - c));
                    to = arc + Mathf.Clamp(t, 0f, segment);
                    return to > from;
                }
                arc = segmentEnd;
            }
            return false;
        }

        /// <summary>Ramer-Douglas-Peucker: drops points closer than <paramref name="tolerance"/> to the line through their neighbours.</summary>
        public static List<Vector2> Simplify(List<Vector2> points, float tolerance)
        {
            if (points.Count < 3 || tolerance <= 0f) return points;
            var keep = new bool[points.Count];
            keep[0] = keep[points.Count - 1] = true;
            var spans = new Stack<(int from, int to)>();
            spans.Push((0, points.Count - 1));
            while (spans.Count > 0)
            {
                var (from, to) = spans.Pop();
                int farthest = -1;
                float farthestDistance = tolerance;
                for (int i = from + 1; i < to; i++)
                {
                    float d = DistanceToSegment(points[i], points[from], points[to]);
                    if (d <= farthestDistance) continue;
                    farthestDistance = d;
                    farthest = i;
                }
                if (farthest < 0) continue;
                keep[farthest] = true;
                spans.Push((from, farthest));
                spans.Push((farthest, to));
            }

            var result = new List<Vector2>();
            for (int i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
            return result;
        }

        /// <summary>One corner-cutting pass that keeps both ends where they are.</summary>
        public static List<Vector2> Chaikin(List<Vector2> points)
        {
            if (points.Count < 3) return points;
            var result = new List<Vector2>(points.Count * 2) { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                if (i > 0) result.Add(Vector2.Lerp(a, b, 0.25f));
                if (i < points.Count - 2) result.Add(Vector2.Lerp(a, b, 0.75f));
            }
            result.Add(points[points.Count - 1]);
            return result;
        }

        /// <summary><paramref name="passes"/> rounds of <see cref="Chaikin"/>.</summary>
        public static List<Vector2> Smooth(List<Vector2> points, int passes)
        {
            for (int pass = 0; pass < passes; pass++) points = Chaikin(points);
            return points;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
