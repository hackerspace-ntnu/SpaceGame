// Where a settlement's characters may stand: every NavMesh surface inside it -- open ground, a
// courtyard, a roof or deck an agent could walk on. The world NavMesh cannot answer that while a
// settlement is being generated: it is one author-time bake that never contains buildings placed
// since (see TerrainGeneration.md), and in edit mode it is not even loaded. So this bakes a
// throwaway NavMesh over just the settlement, with the world bake's own settings and collider
// filter (NavMeshSources), samples it, and removes it again on Dispose. It is baked with every gate
// shut, as play starts: a gate leaf is cut out by a carving obstacle only in play mode, so without
// its collider a pen would read as open to the settlement and nothing could tell it keeps its stock.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    public sealed class SettlementWalkableArea : IDisposable
    {
        private readonly NavMeshData data;
        private NavMeshDataInstance instance;
        private readonly Vector3[] vertices;
        private readonly int[] triangleIndices;
        private readonly List<int> triangles = new();   // first index of each usable triangle
        private readonly List<float> cumulativeArea = new();

        public bool IsEmpty => triangles.Count == 0;

        private SettlementWalkableArea(NavMeshData data, NavMeshDataInstance instance, Vector3 center, float radius)
        {
            this.data = data;
            this.instance = instance;

            // The whole loaded NavMesh, not just this bake -- anything else walkable that happens to
            // be inside the settlement (a cave mouth's own surface) counts too.
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            vertices = tri.vertices;
            triangleIndices = tri.indices;
            Vector2 centerXZ = new Vector2(center.x, center.z);
            float total = 0f;
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = vertices[tri.indices[i]], b = vertices[tri.indices[i + 1]], c = vertices[tri.indices[i + 2]];
                Vector3 centroid = (a + b + c) / 3f;
                if ((new Vector2(centroid.x, centroid.z) - centerXZ).sqrMagnitude > radius * radius) continue;

                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area <= 0f) continue;
                total += area;
                triangles.Add(i);
                cumulativeArea.Add(total);
            }
        }

        /// <summary>
        /// Bakes the walkable surface inside a disc of <paramref name="radius"/> around
        /// <paramref name="center"/>. Null (with the reason logged) if nothing could be baked.
        /// </summary>
        public static SettlementWalkableArea Bake(Vector3 center, float radius, UnityEngine.Object context)
        {
            WorldNavMeshBakeSettings settings = FindWorldBakeSettings(context);
            NavMeshBuildSettings buildSettings = settings.ToBuildSettings();

            Physics.SyncTransforms();
            var sources = new List<NavMeshBuildSource>();
            float margin = settings.agentRadius * 2f;
            float halfSize = radius + margin;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                if (!NavMeshSources.InMask(settings.layerMask, terrain.gameObject.layer)) continue;
                Bounds tb = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
                if (tb.min.x > center.x + halfSize || tb.max.x < center.x - halfSize ||
                    tb.min.z > center.z + halfSize || tb.max.z < center.z - halfSize) continue;

                sources.Add(NavMeshSources.FromTerrain(terrain));
                minY = Mathf.Min(minY, tb.min.y);
                maxY = Mathf.Max(maxY, tb.max.y);
            }

            // A column tall enough to hold anything built on this ground; the bake bounds below are
            // then fitted to what was actually found.
            const float columnHalfHeight = 2000f;
            Collider[] hits = Physics.OverlapBox(center, new Vector3(halfSize, columnHalfHeight, halfSize),
                                                 Quaternion.identity, settings.layerMask, QueryTriggerInteraction.Ignore);
            foreach (Collider col in hits)
            {
                if (col is TerrainCollider) continue;   // covered by its Terrain above
                if (!NavMeshSources.IsBakeable(col, settings.layerMask, doorsShut: true)) continue;
                if (!NavMeshSources.TryColliderToSource(col, out NavMeshBuildSource src)) continue;
                sources.Add(src);
                minY = Mathf.Min(minY, col.bounds.min.y);
                maxY = Mathf.Max(maxY, col.bounds.max.y);
            }

            if (sources.Count == 0)
            {
                Debug.LogError($"[SettlementWalkableArea] Nothing to walk on within {radius:0.#} m of {center} -- no terrain or bakeable collider found.", context);
                return null;
            }

            float height = maxY - minY + settings.agentHeight * 2f;
            var bounds = new Bounds(
                new Vector3(center.x, (minY + maxY) * 0.5f, center.z),
                new Vector3(halfSize * 2f, height, halfSize * 2f));

            NavMeshData baked = NavMeshBuilder.BuildNavMeshData(buildSettings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (baked == null)
            {
                Debug.LogError("[SettlementWalkableArea] NavMeshBuilder.BuildNavMeshData returned null -- no walkable area baked.", context);
                return null;
            }

            NavMeshDataInstance added = NavMesh.AddNavMeshData(baked);
            if (!added.valid)
            {
                UnityEngine.Object.DestroyImmediate(baked);
                Debug.LogError("[SettlementWalkableArea] NavMesh.AddNavMeshData rejected the settlement bake.", context);
                return null;
            }

            return new SettlementWalkableArea(baked, added, center, radius);
        }

        /// <summary>A uniformly distributed point on the walkable surface -- every square metre equally likely, roof or ground.</summary>
        public Vector3 Sample(ref SettlementPlacementUtil.SeededRng rng) => PickPoint(triangles, cumulativeArea, ref rng);

        /// <summary>
        /// The walkable triangles whose bounds touch <paramref name="bounds"/>: a small patch of the surface
        /// (a pen's floor) that can be sampled over and over without walking the whole settlement each time.
        /// A sampled point may still lie just outside the bounds, since a triangle can straddle them.
        /// </summary>
        public Patch PatchWithin(Bounds bounds)
        {
            var patch = new Patch(this);
            float total = 0f;
            for (int i = 0; i + 2 < triangleIndices.Length; i += 3)
            {
                Vector3 a = vertices[triangleIndices[i]], b = vertices[triangleIndices[i + 1]], c = vertices[triangleIndices[i + 2]];
                var triangleBounds = new Bounds(a, Vector3.zero);
                triangleBounds.Encapsulate(b);
                triangleBounds.Encapsulate(c);
                if (!bounds.Intersects(triangleBounds)) continue;

                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area <= 0f) continue;
                total += area;
                patch.triangles.Add(i);
                patch.cumulativeArea.Add(total);
            }
            return patch;
        }

        /// <summary>A sampleable part of the walkable surface; see <see cref="PatchWithin"/>.</summary>
        public sealed class Patch
        {
            private readonly SettlementWalkableArea owner;
            internal readonly List<int> triangles = new();
            internal readonly List<float> cumulativeArea = new();

            internal Patch(SettlementWalkableArea owner) => this.owner = owner;

            public bool IsEmpty => triangles.Count == 0;

            /// <summary>A uniformly distributed point on this patch; call only when it is not empty.</summary>
            public Vector3 Sample(ref SettlementPlacementUtil.SeededRng rng) => owner.PickPoint(triangles, cumulativeArea, ref rng);
        }

        private Vector3 PickPoint(List<int> candidates, List<float> cumulative, ref SettlementPlacementUtil.SeededRng rng)
        {
            float pick = rng.NextFloat01() * cumulative[cumulative.Count - 1];
            int t = cumulative.BinarySearch(pick);
            if (t < 0) t = Mathf.Min(~t, candidates.Count - 1);

            int i = candidates[t];
            Vector3 a = vertices[triangleIndices[i]], b = vertices[triangleIndices[i + 1]], c = vertices[triangleIndices[i + 2]];
            float u = rng.NextFloat01(), v = rng.NextFloat01();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            return a + (b - a) * u + (c - a) * v;
        }

        public void Dispose()
        {
            if (instance.valid) instance.Remove();
            instance = default;
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
        }

        /// <summary>
        /// The world bake's settings, so a spot accepted here is one the real bake accepts too.
        /// Falls back to the defaults -- the same agent, every layer -- with a warning.
        /// </summary>
        private static WorldNavMeshBakeSettings FindWorldBakeSettings(UnityEngine.Object context)
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(WorldNavMeshAsset));
            if (guids.Length > 0)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                if (asset != null) return asset.settings;
            }
#endif
            Debug.LogWarning("[SettlementWalkableArea] No WorldNavMeshAsset found; placing characters with default NavMesh bake settings, which may accept ground the world bake does not.", context);
            return new WorldNavMeshBakeSettings();
        }
    }
}
