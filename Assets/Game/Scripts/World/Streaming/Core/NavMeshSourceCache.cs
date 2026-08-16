using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SpaceGame.World
{
    /// <summary>
    /// Maintains a per-chunk cache of <see cref="NavMeshBuildSource"/> so the streamer never has
    /// to run <see cref="NavMeshBuilder.CollectSources"/> over the whole scene on the main thread.
    /// Collection happens incrementally across frames with a configurable per-frame time budget,
    /// so a freshly loaded chunk (even one containing a large procedural cave mesh) cannot stall
    /// the frame.
    ///
    /// Lifecycle:
    ///   • <see cref="BeginCollect"/> when a chunk finishes loading. Sources accumulate into a
    ///     pending list; once the chunk is fully scanned the list is committed and <see cref="HasDirty"/>
    ///     flips true.
    ///   • <see cref="Drop"/> when a chunk unloads. Removes the entry and marks dirty.
    ///   • <see cref="Tick"/> must be called every frame to advance in-flight collection jobs.
    ///   • <see cref="ConsumeDirty"/> returns true once when the cache has changed since the last
    ///     successful rebuild; the streamer uses this to decide whether to kick a new async bake.
    ///   • <see cref="BuildFlatSourceList"/> hands the union of all chunk source lists to
    ///     <see cref="NavMeshBuilder.UpdateNavMeshDataAsync"/>.
    /// </summary>
    internal sealed class NavMeshSourceCache
    {
        private readonly Dictionary<Vector2Int, List<NavMeshBuildSource>> committed = new();
        private readonly Queue<CollectionJob> jobs = new();
        private readonly HashSet<Vector2Int> pendingChunks = new();
        private CollectionJob activeJob;
        private bool dirty;

        /// <summary>Time budget per frame spent collecting sources, in milliseconds.</summary>
        public float perFrameBudgetMs = 1.0f;

        /// <summary>Number of triangles processed before the job yields and re-checks the budget.</summary>
        public int triangleBatchSize = 4096;

        /// <summary>True while any chunk is still being scanned.</summary>
        public bool HasPendingWork => activeJob != null || jobs.Count > 0;

        /// <summary>True if a rebuild would observe new or removed sources.</summary>
        public bool HasDirty => dirty;

        /// <summary>Total number of committed sources currently in the cache.</summary>
        public int TotalSourceCount
        {
            get
            {
                int n = 0;
                foreach (var list in committed.Values) n += list.Count;
                return n;
            }
        }

        /// <summary>
        /// Enqueue collection of a chunk's NavMesh sources. Safe to call again for the same coord —
        /// it cancels any in-flight job for that coord and starts fresh.
        /// </summary>
        public void BeginCollect(Vector2Int coord, Scene scene, LayerMask layerMask, NavMeshCollectGeometry geometry, int defaultArea)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            // Cancel any in-flight job for this coord — we'll re-collect from scratch.
            if (activeJob != null && activeJob.Coord == coord)
                activeJob = null;

            if (pendingChunks.Contains(coord))
            {
                var newQueue = new Queue<CollectionJob>();
                foreach (var j in jobs) if (j.Coord != coord) newQueue.Enqueue(j);
                jobs.Clear();
                foreach (var j in newQueue) jobs.Enqueue(j);
            }

            var roots = scene.GetRootGameObjects();
            var meshes = new List<MeshFilter>();
            var terrains = new List<Terrain>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                if (geometry != NavMeshCollectGeometry.PhysicsColliders)
                    root.GetComponentsInChildren(true, meshes);
                root.GetComponentsInChildren(true, terrains);
            }

            var colliders = new List<Collider>();
            if (geometry == NavMeshCollectGeometry.PhysicsColliders)
            {
                foreach (var root in roots)
                {
                    if (root == null) continue;
                    root.GetComponentsInChildren(true, colliders);
                }
            }

            jobs.Enqueue(new CollectionJob
            {
                Coord = coord,
                LayerMask = layerMask,
                Geometry = geometry,
                DefaultArea = defaultArea,
                MeshFilters = geometry == NavMeshCollectGeometry.PhysicsColliders ? null : meshes,
                Colliders = geometry == NavMeshCollectGeometry.PhysicsColliders ? colliders : null,
                Terrains = terrains,
                Pending = new List<NavMeshBuildSource>(),
            });
            pendingChunks.Add(coord);
        }

        /// <summary>Drop a chunk's sources. Marks dirty if anything was actually removed.</summary>
        public void Drop(Vector2Int coord)
        {
            if (committed.Remove(coord)) dirty = true;

            if (activeJob != null && activeJob.Coord == coord)
            {
                activeJob = null;
                pendingChunks.Remove(coord);
            }
            if (pendingChunks.Contains(coord))
            {
                var newQueue = new Queue<CollectionJob>();
                foreach (var j in jobs) if (j.Coord != coord) newQueue.Enqueue(j);
                jobs.Clear();
                foreach (var j in newQueue) jobs.Enqueue(j);
                pendingChunks.Remove(coord);
            }
        }

        /// <summary>
        /// Advance any in-flight collection job, never exceeding <see cref="perFrameBudgetMs"/>.
        /// Call every frame from the streamer's <c>Update</c>.
        /// </summary>
        public void Tick()
        {
            if (activeJob == null && jobs.Count == 0) return;

            float budgetSeconds = perFrameBudgetMs * 0.001f;
            float startTime = Time.realtimeSinceStartup;

            while (true)
            {
                if (activeJob == null)
                {
                    if (jobs.Count == 0) return;
                    activeJob = jobs.Dequeue();
                }

                StepJob(activeJob);

                if (activeJob.IsDone)
                {
                    committed[activeJob.Coord] = activeJob.Pending;
                    pendingChunks.Remove(activeJob.Coord);
                    activeJob = null;
                    dirty = true;
                }

                if (Time.realtimeSinceStartup - startTime >= budgetSeconds) return;
            }
        }

        /// <summary>
        /// Consume the dirty flag. Returns true if there is genuinely new data to bake.
        /// The streamer calls this when deciding whether to kick an async build.
        /// </summary>
        public bool ConsumeDirty()
        {
            if (!dirty) return false;
            dirty = false;
            return true;
        }

        /// <summary>Append every cached source into <paramref name="output"/>.</summary>
        public void BuildFlatSourceList(List<NavMeshBuildSource> output)
        {
            foreach (var list in committed.Values) output.AddRange(list);
        }

        /// <summary>Clear everything. Used on despawn.</summary>
        public void Clear()
        {
            committed.Clear();
            jobs.Clear();
            pendingChunks.Clear();
            activeJob = null;
            dirty = false;
        }

        // -------------------------------------------------------------------------
        // Internal job stepping
        // -------------------------------------------------------------------------

        private void StepJob(CollectionJob job)
        {
            // Step 1 — terrains (cheap, one source per terrain, do all at once).
            if (!job.TerrainsDone)
            {
                foreach (var terrain in job.Terrains)
                {
                    if (terrain == null || terrain.terrainData == null) continue;
                    if ((job.LayerMask.value & (1 << terrain.gameObject.layer)) == 0) continue;

                    job.Pending.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Terrain,
                        sourceObject = terrain.terrainData,
                        transform = Matrix4x4.TRS(terrain.transform.position, Quaternion.identity, Vector3.one),
                        area = job.DefaultArea,
                    });
                }
                job.TerrainsDone = true;
                return;
            }

            // Step 2 — physics colliders (when geometry == PhysicsColliders).
            if (job.Colliders != null && !job.CollidersDone)
            {
                int processed = 0;
                while (job.ColliderIndex < job.Colliders.Count && processed < triangleBatchSize)
                {
                    var col = job.Colliders[job.ColliderIndex++];
                    processed++;
                    if (col == null || col.isTrigger) continue;
                    if ((job.LayerMask.value & (1 << col.gameObject.layer)) == 0) continue;

                    if (!TryColliderToSource(col, job.DefaultArea, out var src)) continue;
                    job.Pending.Add(src);
                }
                if (job.ColliderIndex >= job.Colliders.Count) job.CollidersDone = true;
                return;
            }

            // Step 3 — mesh filters (when geometry == RenderMeshes).
            if (job.MeshFilters != null && !job.MeshesDone)
            {
                while (job.MeshIndex < job.MeshFilters.Count)
                {
                    var mf = job.MeshFilters[job.MeshIndex++];
                    if (mf == null) continue;
                    var mesh = mf.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    if ((job.LayerMask.value & (1 << mf.gameObject.layer)) == 0) continue;

                    job.Pending.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mesh,
                        transform = mf.transform.localToWorldMatrix,
                        area = job.DefaultArea,
                    });

                    // Yield per mesh — a large mesh (cave) is the dominant cost when present.
                    if (mesh.triangles.Length > triangleBatchSize) return;
                }
                if (job.MeshIndex >= job.MeshFilters.Count) job.MeshesDone = true;
                return;
            }

            job.IsDone = true;
        }

        private static bool TryColliderToSource(Collider col, int area, out NavMeshBuildSource src)
        {
            src = default;
            var t = col.transform;
            switch (col)
            {
                case MeshCollider mc:
                    if (mc.sharedMesh == null || !mc.sharedMesh.isReadable) return false;
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mc.sharedMesh,
                        transform = t.localToWorldMatrix,
                        area = area,
                    };
                    return true;
                case BoxCollider bc:
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(t.TransformPoint(bc.center), t.rotation,
                            Vector3.Scale(t.lossyScale, bc.size)),
                        size = Vector3.one,
                        area = area,
                    };
                    return true;
                case SphereCollider sc:
                    {
                        float maxScale = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                        src = new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Sphere,
                            transform = Matrix4x4.TRS(t.TransformPoint(sc.center), t.rotation, Vector3.one),
                            size = Vector3.one * (sc.radius * 2f * maxScale),
                            area = area,
                        };
                        return true;
                    }
                case CapsuleCollider cc:
                    {
                        float maxScale = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                        src = new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Capsule,
                            transform = Matrix4x4.TRS(t.TransformPoint(cc.center), t.rotation, Vector3.one),
                            size = new Vector3(cc.radius * 2f * maxScale, cc.height * maxScale, cc.radius * 2f * maxScale),
                            area = area,
                        };
                        return true;
                    }
                case TerrainCollider tc:
                    if (tc.terrainData == null) return false;
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Terrain,
                        sourceObject = tc.terrainData,
                        transform = Matrix4x4.TRS(t.position, Quaternion.identity, Vector3.one),
                        area = area,
                    };
                    return true;
            }
            return false;
        }

        private sealed class CollectionJob
        {
            public Vector2Int Coord;
            public LayerMask LayerMask;
            public NavMeshCollectGeometry Geometry;
            public int DefaultArea;
            public List<MeshFilter> MeshFilters;
            public List<Collider> Colliders;
            public List<Terrain> Terrains;
            public List<NavMeshBuildSource> Pending;
            public int MeshIndex;
            public int ColliderIndex;
            public bool TerrainsDone;
            public bool CollidersDone;
            public bool MeshesDone;
            public bool IsDone;
        }
    }
}
