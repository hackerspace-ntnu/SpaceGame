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

            /// <summary>
            /// True with probability <paramref name="chance"/>. Draws nothing when the chance is
            /// certain, so giving an existing layout a chance of 1 never shifts its later draws.
            /// </summary>
            public bool NextChance(float chance) => chance >= 1f || NextFloat01() < chance;

            public float NextRange(float min, float max) => Mathf.Lerp(min, max, NextFloat01());

            /// <summary>Uniform index in [0, count).</summary>
            public int NextIndex(int count) => Mathf.Min(Mathf.FloorToInt(NextFloat01() * count), count - 1);

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
        /// A prefab's true rendered XZ footprint size, measured from its mesh bounds by walking every
        /// <see cref="MeshFilter"/> in its hierarchy. Falls back to <paramref name="fallback"/> when
        /// the prefab has no mesh (e.g. it's an empty spawn marker).
        /// </summary>
        public static Vector2 ComputeFootprint(GameObject prefab, Vector2 fallback) =>
            MeasureFootprint(prefab, fallback).size;

        /// <summary>
        /// Like <see cref="ComputeFootprint"/>, but keeps where the footprint sits relative to the
        /// prefab's pivot (x = X, y = Z, in the root's unscaled local frame) -- a building whose
        /// pivot is not at its middle would otherwise be spaced as if it were.
        /// </summary>
        public static Rect MeasureFootprint(GameObject prefab, Vector2 fallback)
        {
            Rect fallbackRect = new Rect(-fallback * 0.5f, fallback);
            if (prefab == null) return fallbackRect;

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return fallbackRect;

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

            return init
                ? Rect.MinMaxRect(combined.min.x, combined.min.z, combined.max.x, combined.max.z)
                : fallbackRect;
        }

        /// <summary>
        /// Raycasts straight down onto whatever terrain/ground collider is loaded at this XZ. Mask
        /// defaults to everything, matching the existing settlement generator. Colliders under
        /// <paramref name="ignoreUnder"/> are looked through -- terrain shares layer Default with
        /// buildings, so the mask alone cannot keep a building off its neighbour's roof.
        /// </summary>
        public static bool SampleGround(Vector3 worldXZ, out float groundY, LayerMask mask = default, Transform ignoreUnder = null) =>
            SampleGround(worldXZ, out groundY, out _, mask, ignoreUnder);

        /// <summary><see cref="SampleGround(Vector3, out float, LayerMask, Transform)"/>, plus the surface normal there.</summary>
        public static bool SampleGround(Vector3 worldXZ, out float groundY, out Vector3 normal, LayerMask mask = default, Transform ignoreUnder = null)
        {
            if (mask == default) mask = ~0;
            Vector3 origin = new Vector3(worldXZ.x, worldXZ.y + 500f, worldXZ.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 2000f, mask, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            groundY = worldXZ.y;
            normal = Vector3.up;
            foreach (var hit in hits)
            {
                if (ignoreUnder != null && hit.transform.IsChildOf(ignoreUnder)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                groundY = hit.point.y;
                normal = hit.normal;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        /// <summary>
        /// Height of the terrain tile under <paramref name="xz"/> among <paramref name="terrains"/>, or
        /// <paramref name="fallback"/> off every tile. Terrain only -- no raycast, so buildings,
        /// rocks and a settlement's own output never count; cheap enough to sample a whole grid.
        /// </summary>
        public static float TerrainHeightAt(Terrain[] terrains, Vector2 xz, float fallback)
        {
            foreach (var terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
                if (xz.x < p.x || xz.y < p.z || xz.x > p.x + size.x || xz.y > p.z + size.z) continue;
                return p.y + terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y));
            }
            return fallback;
        }

        /// <summary>
        /// Instantiates <paramref name="prefab"/> as a prefab instance (in the editor) at a pose, then
        /// syncs physics so later ground rays and overlap tests in the same generate see it there.
        /// </summary>
        public static GameObject SpawnPrefab(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation)
        {
            GameObject go;
#if UNITY_EDITOR
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(position, rotation);
#else
            go = Object.Instantiate(prefab, position, rotation, parent);
#endif
            // Physics auto-sync is off in this project (DynamicsManager), so without this the
            // colliders stay where InstantiatePrefab created them -- at the settlement centre --
            // and every later ground ray and overlap test in this Generate hits them there.
            Physics.SyncTransforms();
            return go;
        }
    }
}
