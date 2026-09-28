// Shared placement math for every settlement generator (RobotSettlementGenerator included) so
// footprint measurement, spacing checks and ground sampling exist exactly once.
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementPlacementUtil
    {
        /// <summary>
        /// Deterministic per-point pseudo-random sequence. Same seed always produces the same
        /// sequence of draws — everything here is a pure function of the seed, never
        /// <c>UnityEngine.Random</c>/<c>System.Random</c>, so a settlement regenerates identically
        /// on every machine.
        /// </summary>
        public struct SeededRng
        {
            private readonly int seed;
            private int salt;

            public SeededRng(int seed)
            {
                this.seed = seed;
                salt = 0;
            }

            public float NextFloat01() => TerrainNoiseHelper.Hash01(seed, salt++);

            /// <summary>Uniformly distributed point inside a disc of the given radius, centered on the origin.</summary>
            public Vector2 NextPointInDisk(float radius)
            {
                float r = Mathf.Sqrt(NextFloat01()) * radius;
                float angle = NextFloat01() * Mathf.PI * 2f;
                return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            }
        }

        /// <summary>Derives a stable seed from a world position so the same spot always generates the same layout.</summary>
        public static int SeedFromPosition(Vector3 worldPosition)
        {
            int qx = Mathf.RoundToInt(worldPosition.x * 100f);
            int qy = Mathf.RoundToInt(worldPosition.y * 100f);
            int qz = Mathf.RoundToInt(worldPosition.z * 100f);
            unchecked
            {
                int h = qx * 73856093;
                h = (h * 397) ^ (qy * 19349663);
                h = (h * 397) ^ (qz * 83492791);
                return h;
            }
        }

        /// <summary>
        /// A prefab's true rendered XZ footprint, measured from its mesh bounds by walking every
        /// <see cref="MeshFilter"/> in its hierarchy. Falls back to <paramref name="fallback"/> when
        /// the prefab has no mesh (e.g. it's an empty spawn marker).
        /// </summary>
        public static Vector2 ComputeFootprint(GameObject prefab, Vector2 fallback)
        {
            if (prefab == null) return fallback;

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return fallback;

            Bounds combined = default;
            bool init = false;
            Transform root = prefab.transform;

            // Axis-align against the root's own position/rotation only, deliberately excluding its
            // scale -- root.InverseTransformPoint would divide that back out, silently reporting the
            // footprint the prefab would have at scale 1 rather than the one it's actually placed
            // at. Most prefab roots ARE scale 1, so this went unnoticed until a corrected NomadSail_*
            // tent (root scale ~0.33-0.65, see ArtPipeline.md) measured 1.5-3x its real footprint,
            // reserving that much placement space and starving everything spawned after it.
            Matrix4x4 rootNoScale = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
            Matrix4x4 worldToRootNoScale = rootNoScale.inverse;

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Bounds local = mf.sharedMesh.bounds;
                Vector3 c = local.center;
                Vector3 e = local.extents;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    Vector3 inRoot = worldToRootNoScale.MultiplyPoint3x4(mf.transform.TransformPoint(corner));
                    if (!init) { combined = new Bounds(inRoot, Vector3.zero); init = true; }
                    else combined.Encapsulate(inRoot);
                }
            }

            return init ? new Vector2(combined.size.x, combined.size.z) : fallback;
        }

        /// <summary>Half the clearance a prefab needs from its neighbours: its own longest XZ extent plus the configured spacing.</summary>
        public static float ClearanceRadius(GameObject prefab, float minSpacing, Vector2 fallbackFootprint)
        {
            Vector2 footprint = ComputeFootprint(prefab, fallbackFootprint);
            float longest = Mathf.Max(footprint.x, footprint.y);
            return (longest + minSpacing) * 0.5f;
        }

        /// <summary>Raycasts straight down onto whatever terrain/ground collider is loaded at this XZ. Mask defaults to everything, matching the existing settlement generator.</summary>
        public static bool SampleGround(Vector3 worldXZ, out float groundY, LayerMask mask = default)
        {
            if (mask == default) mask = ~0;
            Vector3 origin = new Vector3(worldXZ.x, worldXZ.y + 500f, worldXZ.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2000f, mask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = worldXZ.y;
            return false;
        }
    }
}
