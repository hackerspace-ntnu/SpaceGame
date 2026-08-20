using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.World.Streaming;

// Namespace deliberately not 'SpaceGame.World.Editor': that shadows UnityEditor.Editor for every
// editor script already sitting in SpaceGame.World, breaking WorldStreamerEditor's base class.
namespace SpaceGame.World.NavMeshTools
{
    /// <summary>
    /// Bakes every chunk scene's collision into one world NavMesh asset.
    ///
    /// This runs at author time so the runtime never bakes. It reproduces what the runtime does to
    /// a chunk on load — aligning the terrain to its grid coordinate, spawning the terrain features
    /// from their baked meshes — so the mesh it produces matches the world the player walks on. Get
    /// either of those wrong and the NavMesh is silently offset from the ground.
    ///
    /// It iterates <see cref="WorldStreamingConfig.chunks"/>, never the chunk folder: 240 chunk
    /// scenes exist on disk and only 48 are in the grid.
    /// </summary>
    public static class WorldNavMeshBaker
    {
        public const string AssetPath = "Assets/Game/Settings/WorldNavMesh.asset";

        /// <summary>
        /// Layers excluded from the bake by default. A permanent NavMesh must not contain anything
        /// that moves: a character capsule baked in as an obstacle carves a hole that never heals,
        /// and the runtime bake this replaces had exactly that bug (it collected every render mesh
        /// on every layer, skinned NPC bodies included).
        /// </summary>
        private static readonly string[] ExcludedLayerNames =
            { "Player", "UI", "Hologram", "Interior", "TransparentFX", "Ignore Raycast", "Water" };

        [MenuItem("World/Streaming/Bake World NavMesh", priority = 0)]
        public static void BakeMenu()
        {
            var config = LoadConfig();
            if (config == null) return;

            var report = Bake(config);
            Debug.Log(report);
            EditorUtility.DisplayDialog("World NavMesh", report, "OK");
        }

        [MenuItem("World/Streaming/Bake World NavMesh", validate = true)]
        private static bool CanBake() => !EditorApplication.isPlaying;

        /// <summary>
        /// Bakes and writes <see cref="AssetPath"/>. Returns a human-readable report.
        /// Leaves the editor's open scenes exactly as it found them.
        /// </summary>
        public static string Bake(WorldStreamingConfig config)
        {
            if (config.chunks == null || config.chunks.Length == 0)
                return "[WorldNavMeshBaker] config has no chunks — nothing to bake.";

            // Refuse to bake over chunk scenes the user already has open. The bake mutates every
            // scene it touches — it moves terrain onto the grid and spawns the feature meshes — and
            // it can only safely discard those edits in scenes it opened itself. Left in an open
            // scene they look like real edits, and saving one writes the spawned geometry into the
            // scene file permanently. That is exactly how these chunks grew to 17-40 MB of
            // duplicated meshes in the first place.
            var alreadyOpen = new List<string>();
            foreach (var chunk in config.chunks)
            {
                if (string.IsNullOrEmpty(chunk.scenePath)) continue;
                var s = SceneManager.GetSceneByPath(chunk.scenePath);
                if (s.IsValid() && s.isLoaded) alreadyOpen.Add(chunk.sceneName);
            }

            if (alreadyOpen.Count > 0)
                return $"[WorldNavMeshBaker] {alreadyOpen.Count} chunk scene(s) are open in the " +
                       "editor. Close them first — the bake edits every scene it touches and must " +
                       "not risk those edits being saved into your scenes.\n  " +
                       string.Join(", ", alreadyOpen);

            var asset = LoadOrCreateAsset();
            var settings = asset.settings.ToBuildSettings();

            var opened = new List<Scene>();
            var sources = new List<NavMeshBuildSource>();
            var stamps = new List<WorldNavMeshAsset.Stamp>();
            bool haveBounds = false;
            var bounds = new Bounds();
            int featuresSpawned = 0, missingScenes = 0, unbakedFeatures = 0;

            try
            {
                // Open everything first, then collect. Collecting scene-by-scene and closing as we
                // go would dangle any source whose mesh is embedded in the scene rather than an
                // asset — closing the scene destroys it.
                for (int i = 0; i < config.chunks.Length; i++)
                {
                    var chunk = config.chunks[i];
                    EditorUtility.DisplayProgressBar("Baking world NavMesh",
                        $"opening {chunk.sceneName} ({i + 1}/{config.chunks.Length})",
                        0.45f * i / config.chunks.Length);

                    if (string.IsNullOrEmpty(chunk.scenePath)
                        || AssetDatabase.LoadAssetAtPath<SceneAsset>(chunk.scenePath) == null)
                    {
                        Debug.LogWarning($"[WorldNavMeshBaker] chunk {chunk.gridCoord}: no scene at " +
                                         $"'{chunk.scenePath}' — skipped.");
                        missingScenes++;
                        continue;
                    }

                    // Guaranteed not already open by the guard above, so everything we open is
                    // something we are responsible for discarding in the finally block.
                    Scene scene = EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive);
                    opened.Add(scene);

                    AlignTerrainToGrid(scene, config, chunk.gridCoord);
                    featuresSpawned += SpawnBakedFeatures(scene, ref unbakedFeatures);

                    stamps.Add(new WorldNavMeshAsset.Stamp
                    {
                        sceneGuid = AssetDatabase.AssetPathToGUID(chunk.scenePath),
                        sceneName = chunk.sceneName,
                        dependencyHash = AssetDatabase.GetAssetDependencyHash(chunk.scenePath).ToString(),
                    });
                }

                // Terrain colliders were repositioned above; their bounds are stale until synced.
                Physics.SyncTransforms();

                EditorUtility.DisplayProgressBar("Baking world NavMesh", "collecting sources", 0.5f);
                foreach (var chunk in config.chunks)
                {
                    var scene = SceneManager.GetSceneByPath(chunk.scenePath);
                    if (!scene.IsValid() || !scene.isLoaded) continue;
                    CollectScene(scene, asset.settings.layerMask, sources, ref bounds, ref haveBounds);
                }

                if (!haveBounds || sources.Count == 0)
                    return "[WorldNavMeshBaker] collected no NavMesh sources — nothing baked. " +
                           "Check the layer mask on " + AssetPath;

                bounds.Expand(2f);

                EditorUtility.DisplayProgressBar("Baking world NavMesh",
                    $"baking {sources.Count} sources over {bounds.size.x:0} x {bounds.size.z:0} m", 0.55f);

                var t0 = DateTime.UtcNow;
                var data = NavMeshBuilder.BuildNavMeshData(
                    settings, sources, bounds, Vector3.zero, Quaternion.identity);
                double bakeMs = (DateTime.UtcNow - t0).TotalMilliseconds;

                if (data == null)
                    return "[WorldNavMeshBaker] BuildNavMeshData returned null — nothing baked.";

                EditorUtility.DisplayProgressBar("Baking world NavMesh", "writing asset", 0.95f);
                StoreBakedData(asset, data);

                asset.config = config;
                asset.stamps = stamps.ToArray();
                asset.sourceCount = sources.Count;
                asset.bakedAtUtc = DateTime.UtcNow.ToString("u");
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return $"[WorldNavMeshBaker] baked {sources.Count} sources from " +
                       $"{stamps.Count} chunk scenes in {bakeMs / 1000.0:0.0} s.\n" +
                       $"bounds {bounds.size.x:0} x {bounds.size.y:0} x {bounds.size.z:0} m, " +
                       $"voxel {asset.settings.voxelSize:0.###} m\n" +
                       $"features spawned for bake: {featuresSpawned}" +
                       (unbakedFeatures > 0 ? $", WITHOUT baked meshes: {unbakedFeatures}" : "") +
                       (missingScenes > 0 ? $", missing scenes: {missingScenes}" : "") +
                       $"\nwrote {AssetPath}";
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                // Close only what we opened, and discard: the terrain moves and spawned features
                // above are bake scaffolding, not edits the user asked for.
                for (int i = opened.Count - 1; i >= 0; i--)
                {
                    if (opened[i].IsValid() && opened[i].isLoaded)
                        EditorSceneManager.CloseScene(opened[i], true);
                }
            }
        }

        /// <summary>
        /// Mirrors <c>WorldStreamer.CacheTerrainForChunk</c>: chunk terrain is snapped to its grid
        /// X/Z at load time while keeping its authored elevation. The bake has to do the same or
        /// the NavMesh sits wherever the scene happened to be saved.
        /// </summary>
        private static void AlignTerrainToGrid(Scene scene, WorldStreamingConfig config, Vector2Int coord)
        {
            Vector3 expected = config.ChunkToWorldPosition(coord);

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    var p = terrain.transform.position;
                    terrain.transform.position = new Vector3(expected.x, p.y, expected.z);
                }
            }
        }

        /// <summary>
        /// Instantiates each terrain feature from its baked mesh, exactly as the runtime does, so
        /// the bake sees the runtime's geometry. Spawners whose baked mesh is missing are left
        /// alone and counted — their scene-authored children (if any) still get collected, and if
        /// there are none the caller reports the gap rather than baking a hole.
        /// </summary>
        private static int SpawnBakedFeatures(Scene scene, ref int unbaked)
        {
            int spawned = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var spawner in root.GetComponentsInChildren<TerrainFeatureSpawner>(true))
                {
                    if (!spawner.HasBakedMesh)
                    {
                        unbaked++;
                        Debug.LogWarning($"[WorldNavMeshBaker] '{spawner.name}' in {scene.name} has " +
                                         "no baked mesh; baking whatever is authored in the scene.",
                                         spawner);
                        continue;
                    }

                    spawner.SpawnBaked();
                    spawned++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// Turns a chunk scene's collision into NavMesh sources.
        ///
        /// Collision, not render meshes. Navigation should follow what the player actually walks on,
        /// and collecting render meshes is what let the old runtime bake pull 66 skinned NPC bodies
        /// into the world NavMesh. Dynamic bodies are skipped for the same reason.
        /// </summary>
        private static void CollectScene(Scene scene, LayerMask mask, List<NavMeshBuildSource> into,
                                         ref Bounds bounds, ref bool haveBounds)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    if (terrain.terrainData == null) continue;
                    if (!InMask(mask, terrain.gameObject.layer)) continue;

                    into.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Terrain,
                        sourceObject = terrain.terrainData,
                        transform = Matrix4x4.TRS(terrain.transform.position, Quaternion.identity, Vector3.one),
                        area = 0,
                    });

                    Encapsulate(ref bounds, ref haveBounds,
                        new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f,
                                   terrain.terrainData.size));
                }

                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null || col.isTrigger) continue;
                    if (col is TerrainCollider) continue;          // already covered by the Terrain
                    if (!InMask(mask, col.gameObject.layer)) continue;

                    // Anything with a non-kinematic body is scenery that moves; baking it fixes it
                    // in place forever.
                    var body = col.attachedRigidbody;
                    if (body != null && !body.isKinematic) continue;

                    if (!TryColliderToSource(col, out var src)) continue;

                    into.Add(src);
                    Encapsulate(ref bounds, ref haveBounds, col.bounds);
                }
            }
        }

        private static bool InMask(LayerMask mask, int layer) => (mask.value & (1 << layer)) != 0;

        private static void Encapsulate(ref Bounds bounds, ref bool have, Bounds b)
        {
            if (!have) { bounds = b; have = true; }
            else bounds.Encapsulate(b);
        }

        /// <summary>
        /// Collider to NavMesh source. Lifted from the runtime <c>NavMeshSourceCache</c> this change
        /// deletes — the mapping was correct, it was the per-frame rebuilding around it that was not.
        /// </summary>
        private static bool TryColliderToSource(Collider col, out NavMeshBuildSource src)
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
                        area = 0,
                    };
                    return true;

                case BoxCollider bc:
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(t.TransformPoint(bc.center), t.rotation,
                                                  Vector3.Scale(t.lossyScale, bc.size)),
                        size = Vector3.one,
                        area = 0,
                    };
                    return true;

                case SphereCollider sc:
                {
                    float s = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Sphere,
                        transform = Matrix4x4.TRS(t.TransformPoint(sc.center), t.rotation, Vector3.one),
                        size = Vector3.one * (sc.radius * 2f * s),
                        area = 0,
                    };
                    return true;
                }

                case CapsuleCollider cc:
                {
                    float s = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                    src = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Capsule,
                        transform = Matrix4x4.TRS(t.TransformPoint(cc.center), t.rotation, Vector3.one),
                        size = new Vector3(cc.radius * 2f * s, cc.height * s, cc.radius * 2f * s),
                        area = 0,
                    };
                    return true;
                }
            }

            return false;
        }

        // ─────────────────────────────────────────────
        //  Asset plumbing
        // ─────────────────────────────────────────────

        /// <summary>
        /// The config this NavMesh is for.
        ///
        /// The baked asset's own <see cref="WorldNavMeshAsset.config"/> reference wins, because the
        /// project can hold several streaming configs — a second one appeared during this work and
        /// picking "the first result" silently baked a NavMesh for a different world's chunk list.
        /// Guessing is only acceptable when there is exactly one candidate; otherwise this refuses
        /// rather than choosing.
        /// </summary>
        public static WorldStreamingConfig LoadConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(AssetPath);
            if (asset != null && asset.config != null) return asset.config;

            var guids = AssetDatabase.FindAssets("t:WorldStreamingConfig");
            if (guids.Length == 0)
            {
                Debug.LogError("[WorldNavMeshBaker] no WorldStreamingConfig asset in the project.");
                return null;
            }

            if (guids.Length > 1)
            {
                var names = new List<string>();
                foreach (var g in guids) names.Add(AssetDatabase.GUIDToAssetPath(g));

                Debug.LogError($"[WorldNavMeshBaker] {guids.Length} WorldStreamingConfig assets exist " +
                               $"and {AssetPath} does not say which world it is for. Assign its " +
                               $"'Config' field and bake again. Candidates:\n  " +
                               string.Join("\n  ", names));
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<WorldStreamingConfig>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static WorldNavMeshAsset LoadOrCreateAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(AssetPath);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<WorldNavMeshAsset>();
            asset.settings.layerMask = DefaultLayerMask();
            AssetDatabase.CreateAsset(asset, AssetPath);
            Debug.Log($"[WorldNavMeshBaker] created {AssetPath}");
            return asset;
        }

        private static LayerMask DefaultLayerMask()
        {
            int mask = ~0;
            foreach (var name in ExcludedLayerNames)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0) mask &= ~(1 << layer);
            }
            return mask;
        }

        /// <summary>
        /// Replaces the asset's baked sub-object. The old one must be removed AND destroyed or it
        /// stays in the file as an orphan and the asset grows every bake.
        /// </summary>
        private static void StoreBakedData(WorldNavMeshAsset asset, NavMeshData data)
        {
            if (asset.bakedData != null)
            {
                AssetDatabase.RemoveObjectFromAsset(asset.bakedData);
                UnityEngine.Object.DestroyImmediate(asset.bakedData, true);
            }

            data.name = "WorldNavMeshData";
            AssetDatabase.AddObjectToAsset(data, asset);
            asset.bakedData = data;
        }
    }
}
