// The plane a rigid sole actually comes to rest on.
//
// A foot is not a point. It is a disc of some size, and where it ends up is decided by everything
// under it rather than by whatever a single ray beneath its centre happened to hit. The code this
// replaces acknowledged that much -- it fired five rays -- but then combined them by pairing the
// centre ray's x/z with the HIGHEST neighbour's y, which is a point lying on no surface at all.
// That is why feet were seen hovering on one side of a slope and buried on the other.
//
// What a rigid disc rests on is a supporting plane: the plane through the samples, pushed up until
// nothing pokes through it. That is what this computes, and the property is worth stating exactly
// because it is what the tests pin down -- for every sample s, dot(s - Point, Normal) <= 0.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// One ray's worth of ground under a sole.
    public struct WalkerFootprintSample
    {
        public Vector3 Point;
        public Vector3 Normal;
    }

    public struct WalkerSurface
    {
        /// Where the sole's contact point comes to rest. Directly under the centre it was sampled
        /// about, at the height the supporting plane puts it.
        public Vector3 Point;

        /// Mean of the sampled normals. Averaged rather than taken from one ray, because a single
        /// ray on the lip of a rock reports a wall.
        public Vector3 Normal;

        /// Below this the surface is treated as a wall, where there is no "height at an (x, z)"
        /// to speak of and the vertical solve would divide by nearly zero.
        private const float MinVerticalComponent = 1e-3f;

        /// Fits the supporting plane to `count` samples taken around `centre`. Returns false when
        /// there is nothing to fit, rather than inventing a surface the caller would then stand on.
        public static bool TryFit(Vector3 centre, WalkerFootprintSample[] samples, int count,
                                  out WalkerSurface surface)
        {
            surface = default;
            if (samples == null || count <= 0 || count > samples.Length) return false;

            Vector3 normalSum = Vector3.zero;
            Vector3 pointSum = Vector3.zero;
            float highest = float.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                normalSum += samples[i].Normal;
                pointSum += samples[i].Point;
                highest = Mathf.Max(highest, samples[i].Point.y);
            }

            Vector3 normal = normalSum.sqrMagnitude > 1e-8f ? normalSum.normalized : Vector3.up;
            Vector3 mean = pointSum / count;

            surface.Normal = normal;

            // A wall has no height to stand at. Report the topmost hit so the caller has a finite
            // point to reason about, and let it judge the foothold on the normal.
            if (Mathf.Abs(normal.y) < MinVerticalComponent)
            {
                surface.Point = new Vector3(centre.x, highest, centre.z);
                return true;
            }

            // Push the plane up until the topmost sample lies on it rather than through it. The
            // offset is measured vertically, which is the direction the sole is pressed down in.
            float lift = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = samples[i].Point;
                lift = Mathf.Max(lift, p.y - HeightAt(mean, normal, p.x, p.z));
            }

            surface.Point = new Vector3(
                centre.x, HeightAt(mean, normal, centre.x, centre.z) + lift, centre.z);
            return true;
        }

        /// Height of the plane through `on` with `normal`, above a given (x, z).
        private static float HeightAt(Vector3 on, Vector3 normal, float x, float z)
            => on.y - (normal.x * (x - on.x) + normal.z * (z - on.z)) / normal.y;
    }
}
