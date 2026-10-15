// A building's ground footprint as a rotated rectangle in the XZ plane. Spacing buildings by the
// real distance between their walls -- not by circles around their longest side, which kept a
// long building's short sides metres clear of anything -- is what lets a settlement pack them
// shoulder to shoulder.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public readonly struct SettlementFootprint
    {
        public readonly Vector2 center;
        public readonly Vector2 halfExtents;
        private readonly Vector2 axisX;
        private readonly Vector2 axisZ;

        /// <param name="center">Rectangle centre, XZ.</param>
        /// <param name="size">Rectangle size along the rotated X and Z axes.</param>
        /// <param name="rotation">Only the yaw matters.</param>
        public SettlementFootprint(Vector2 center, Vector2 size, Quaternion rotation)
        {
            this.center = center;
            halfExtents = size * 0.5f;
            Vector3 x = rotation * Vector3.right;
            Vector3 z = rotation * Vector3.forward;
            axisX = new Vector2(x.x, x.z).normalized;
            axisZ = new Vector2(z.x, z.z).normalized;
        }

        /// <summary>Distance from the centre to the farthest corner.</summary>
        public float Circumradius => halfExtents.magnitude;

        public SettlementFootprint MovedTo(Vector2 newCenter) => new SettlementFootprint(newCenter, halfExtents, axisX, axisZ);

        private SettlementFootprint(Vector2 center, Vector2 halfExtents, Vector2 axisX, Vector2 axisZ)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.axisX = axisX;
            this.axisZ = axisZ;
        }

        /// <summary>Distance from a point to the rectangle's edge: positive outside, negative inside.</summary>
        public float SignedDistance(Vector2 point)
        {
            Vector2 d = point - center;
            Vector2 q = new Vector2(Mathf.Abs(Vector2.Dot(d, axisX)), Mathf.Abs(Vector2.Dot(d, axisZ))) - halfExtents;
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return outside + inside;
        }

        /// <summary>
        /// Wall-to-wall distance between two footprints in metres. Negative when they overlap (the
        /// depth of the overlap), so "at least this far apart" is one comparison for any gap,
        /// including a gap of 0 that lets two buildings touch.
        /// </summary>
        public static float Gap(in SettlementFootprint a, in SettlementFootprint b)
        {
            float leastOverlap = float.PositiveInfinity;
            if (!AxisOverlap(a, b, a.axisX, ref leastOverlap) || !AxisOverlap(a, b, a.axisZ, ref leastOverlap) ||
                !AxisOverlap(a, b, b.axisX, ref leastOverlap) || !AxisOverlap(a, b, b.axisZ, ref leastOverlap))
            {
                // Separated: for two convex shapes the closest pair always includes a corner.
                float gap = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    gap = Mathf.Min(gap, b.SignedDistance(a.Corner(i)));
                    gap = Mathf.Min(gap, a.SignedDistance(b.Corner(i)));
                }
                return gap;
            }
            return -leastOverlap;
        }

        /// <summary>Points covering the rectangle, edges and corners included, no more than <paramref name="spacing"/> apart along either axis.</summary>
        public IEnumerable<Vector2> GridPoints(float spacing)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt(2f * halfExtents.x / spacing));
            int nz = Mathf.Max(1, Mathf.CeilToInt(2f * halfExtents.y / spacing));
            for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
                yield return center + axisX * (halfExtents.x * (2f * i / nx - 1f)) + axisZ * (halfExtents.y * (2f * j / nz - 1f));
        }

        private Vector2 Corner(int i)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            return center + axisX * (halfExtents.x * sx) + axisZ * (halfExtents.y * sz);
        }

        /// <summary>Half the rectangle's width measured along <paramref name="axis"/> (a unit vector).</summary>
        public float ExtentAlong(Vector2 axis) => ProjectedRadius(axis);

        private float ProjectedRadius(Vector2 axis) =>
            halfExtents.x * Mathf.Abs(Vector2.Dot(axisX, axis)) + halfExtents.y * Mathf.Abs(Vector2.Dot(axisZ, axis));

        /// <summary>Separating-axis test on one axis; false when this axis separates the two.</summary>
        private static bool AxisOverlap(in SettlementFootprint a, in SettlementFootprint b, Vector2 axis, ref float leastOverlap)
        {
            float overlap = a.ProjectedRadius(axis) + b.ProjectedRadius(axis) - Mathf.Abs(Vector2.Dot(b.center - a.center, axis));
            if (overlap <= 0f) return false;
            leastOverlap = Mathf.Min(leastOverlap, overlap);
            return true;
        }
    }
}
