// The patrol route: a ring of points a fixed distance outside the outermost buildings. Pure geometry — the
// society turns the points into NavMesh places. For each bearing from the centre the ring sits where a ray
// leaves the farthest building box (grown by the offset), so it hugs the settlement's outline instead of
// being a circle that cuts through the market or floats a hundred metres off a compact village.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public static class PerimeterRing
    {
        /// <summary>
        /// <paramref name="count"/> points around <paramref name="center"/>, in bearing order, each
        /// <paramref name="offset"/> metres outside the farthest of <paramref name="footprints"/> along its ray.
        /// A bearing no footprint lies along is skipped. Heights are the centre's: the caller snaps them to ground.
        /// </summary>
        public static List<Vector3> Compute(Vector3 center, IReadOnlyList<Bounds> footprints, float offset, int count)
        {
            var ring = new List<Vector3>(count);
            for (int k = 0; k < count; k++)
            {
                float angle = k * Mathf.PI * 2f / count;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float reach = -1f;
                foreach (Bounds box in footprints)
                    reach = Mathf.Max(reach, ExitDistance(new Vector2(center.x, center.z), direction, box, offset));
                if (reach > 0f) ring.Add(new Vector3(center.x + direction.x * reach, center.y, center.z + direction.y * reach));
            }
            return ring;
        }

        /// <summary>Where a ray from <paramref name="origin"/> leaves the XZ rectangle of <paramref name="box"/> grown by <paramref name="grow"/>; -1 when it misses.</summary>
        public static float ExitDistance(Vector2 origin, Vector2 direction, Bounds box, float grow)
        {
            Vector2 min = new Vector2(box.min.x - grow, box.min.z - grow), max = new Vector2(box.max.x + grow, box.max.z + grow);
            float near = float.NegativeInfinity, far = float.PositiveInfinity;
            if (!Slab(origin.x, direction.x, min.x, max.x, ref near, ref far)) return -1f;
            if (!Slab(origin.y, direction.y, min.y, max.y, ref near, ref far)) return -1f;
            return far >= Mathf.Max(near, 0f) ? far : -1f;
        }

        private static bool Slab(float origin, float direction, float min, float max, ref float near, ref float far)
        {
            if (Mathf.Abs(direction) < 1e-6f) return origin >= min && origin <= max;

            float a = (min - origin) / direction, b = (max - origin) / direction;
            near = Mathf.Max(near, Mathf.Min(a, b));
            far = Mathf.Min(far, Mathf.Max(a, b));
            return near <= far;
        }
    }
}
