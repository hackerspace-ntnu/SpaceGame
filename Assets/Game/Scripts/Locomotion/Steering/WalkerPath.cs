// Following a polyline, for a machine that can only be asked to go forwards and turn.
//
// A NavMesh path arrives as a list of corners. The walker cannot be teleported along it, so the
// only thing this has to answer each frame is "which corner am I steering at right now" -- and
// then get out of the way and let the driver turn the heading error into a twist.
//
// Two things make this less trivial than it sounds, and they are the whole reason it is a type
// rather than two lines in the driver:
//
//   * Arrival is measured FLAT. The hull rides metres above the NavMesh the corners were sampled
//     on, so a 3D distance never falls below the ride height and the walker would grind against a
//     corner it is standing directly on top of, forever.
//   * The index only ever moves forward. A legged machine overshoots -- it is metres long and
//     pivots slowly -- so a nearest-corner search would hand it the corner behind it the moment
//     it swung wide, and it would turn around and re-walk the leg it just finished.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// A NavMesh path being walked, and the cursor into it. Knows nothing about NavMesh itself:
    /// it is handed corners, so it can be driven from a test with no baked surface in the scene.
    public class WalkerPath
    {
        private Vector3[] corners = System.Array.Empty<Vector3>();
        private int count;
        private int index;

        /// Corners still ahead of the cursor, the one being steered at included.
        public int RemainingCorners => Mathf.Max(0, count - index);

        /// True while there is still a corner to steer at.
        public bool HasPath => index < count;

        /// The corner currently being steered at, without advancing anything. For gizmos.
        public Vector3 CurrentCorner => HasPath ? corners[index] : Vector3.zero;

        /// Adopt a freshly computed path. `cornerCount` is passed separately because
        /// NavMeshPath.GetCornersNonAlloc fills a reusable buffer that is longer than the path.
        ///
        /// The first corner is dropped: NavMesh paths start at the agent's own position, and
        /// steering at where you already are yields a zero-length heading vector and no turn.
        public void Set(Vector3[] pathCorners, int cornerCount)
        {
            corners = pathCorners ?? System.Array.Empty<Vector3>();
            int available = Mathf.Clamp(cornerCount, 0, corners.Length);

            // Fewer than two corners is nothing but the agent's own position, which is not
            // somewhere it can be steered towards. Treat it as no path at all.
            count = available < 2 ? 0 : available;
            index = count > 0 ? 1 : 0;
        }

        public void Clear()
        {
            count = 0;
            index = 0;
        }

        /// Where to steer from `position`, having first walked the cursor past every corner
        /// already reached. Returns false once the path is spent -- which is the driver's cue to
        /// aim at the destination itself rather than to stop, since the last corner sits on the
        /// NavMesh and the destination may not.
        public bool TryGetSteerTarget(Vector3 position, float arriveRadius, out Vector3 target)
        {
            float radiusSqr = arriveRadius * arriveRadius;
            while (index < count && FlatDistanceSqr(position, corners[index]) <= radiusSqr)
                index++;

            if (index >= count)
            {
                target = default;
                return false;
            }

            target = corners[index];
            return true;
        }

        /// Distance still to walk along the path from `position`, summed leg by leg. The straight
        /// line to the destination is shorter than this whenever the path bends around anything,
        /// which is exactly when the difference matters.
        public float RemainingDistance(Vector3 position)
        {
            if (!HasPath)
                return 0f;

            float total = Mathf.Sqrt(FlatDistanceSqr(position, corners[index]));
            for (int i = index; i < count - 1; i++)
                total += Mathf.Sqrt(FlatDistanceSqr(corners[i], corners[i + 1]));
            return total;
        }

        private static float FlatDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
