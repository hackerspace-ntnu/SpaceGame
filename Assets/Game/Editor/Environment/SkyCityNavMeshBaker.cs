// Bakes the Sky City fleet's NavMesh and hands it to the fleet prefab.
//
// The world bake only walks the chunk scenes, and the fleet stands in persistentScene, so its decks
// had no NavMesh at all. This bakes the fleet prefab's own collision, in the prefab's own space,
// into one NavMeshData asset; StaticNavMeshData on the fleet root adds it wherever the fleet
// stands. Moving the fleet in the scene needs no re-bake. Changing its geometry does - rebuilding
// the fleet (Tools > Environment > Build Sky Fleet Prefabs) re-bakes, a city-only rebuild does not.
//
// Same agent, voxel and layer settings as the world bake, read from WorldNavMesh.asset, so a
// ground NPC paths the city exactly as it paths the desert. Same collider filter and mapping too
// (WorldNavMeshBaker.IsBakeable / TryColliderToSource).
//
// Re-run from: World > Streaming > Bake Sky City NavMesh
using System.Collections.Generic;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public static class SkyCityNavMeshBaker
    {
        public const string AssetPath = "Assets/Game/Settings/SkyCityNavMesh.asset";

        // Margin round the collected geometry, as the world bake leaves round its chunks.
        private const float BoundsMargin = 2f;

        [MenuItem("World/Streaming/Bake Sky City NavMesh", priority = 1)]
        public static void BakeMenu()
        {
            string report = Bake();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Sky City NavMesh", report, "OK");
        }

        [MenuItem("World/Streaming/Bake Sky City NavMesh", validate = true)]
        private static bool CanBake() => !EditorApplication.isPlaying;

        /// <summary>
        /// Bakes <see cref="AssetPath"/> from the fleet prefab and puts it on the fleet root.
        /// Returns a human-readable report.
        /// </summary>
        public static string Bake()
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshBaker.AssetPath);
            if (world == null)
                return $"[SkyCityNavMeshBaker] no {WorldNavMeshBaker.AssetPath} to take bake settings from " +
                       "- bake the world NavMesh first.";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(SkyFleetBuilder.FleetPrefabPath) == null)
                return $"[SkyCityNavMeshBaker] no {SkyFleetBuilder.FleetPrefabPath} " +
                       "- run Tools > Environment > Build Sky Fleet Prefabs.";

            NavMeshBuildSettings settings = world.settings.ToBuildSettings();
            var sources = new List<NavMeshBuildSource>();
            Bounds bounds;

            GameObject fleet = PrefabUtility.LoadPrefabContents(SkyFleetBuilder.FleetPrefabPath);
            try
            {
                // AddNavMeshData places the mesh by position and rotation only, so the prefab root
                // must not carry a scale the mesh would silently lose.
                if (fleet.transform.localScale != Vector3.one)
                    return $"[SkyCityNavMeshBaker] {SkyFleetBuilder.FleetPrefabPath}'s root is scaled " +
                           $"{fleet.transform.localScale} - StaticNavMeshData cannot apply scale.";

                // Prefab space: the sources are collected relative to the root, not wherever the
                // prefab happened to be saved.
                fleet.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                if (!Collect(fleet, world.settings.layerMask, sources, out bounds))
                    return "[SkyCityNavMeshBaker] collected no NavMesh sources - nothing baked. " +
                           "Check the layer mask on " + WorldNavMeshBaker.AssetPath;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fleet);
            }

            bounds.Expand(BoundsMargin);
            NavMeshData data = Store(settings, sources, bounds);
            if (data == null)
                return "[SkyCityNavMeshBaker] BuildNavMeshData returned null - nothing baked.";

            bool attached = AttachToFleet(data);

            return $"[SkyCityNavMeshBaker] baked {sources.Count} sources over " +
                   $"{bounds.size.x:0} x {bounds.size.y:0} x {bounds.size.z:0} m, " +
                   $"voxel {world.settings.voxelSize:0.###} m, slope {world.settings.agentSlope:0} deg, " +
                   $"climb {world.settings.agentClimb:0.##} m\n" +
                   $"wrote {AssetPath}" +
                   (attached ? $"\nput StaticNavMeshData on {SkyFleetBuilder.FleetPrefabPath}" : "");
        }

        private static bool Collect(GameObject root, LayerMask mask, List<NavMeshBuildSource> into, out Bounds bounds)
        {
            bounds = default;
            bool haveBounds = false;
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
            {
                if (!WorldNavMeshBaker.IsBakeable(col, mask)) continue;
                if (!WorldNavMeshBaker.TryColliderToSource(col, out NavMeshBuildSource src)) continue;

                into.Add(src);
                if (haveBounds) bounds.Encapsulate(col.bounds);
                else { bounds = col.bounds; haveBounds = true; }
            }
            return haveBounds;
        }

        /// <summary>
        /// Builds into the existing asset when there is one, so its GUID - and the fleet prefab's
        /// reference to it - survives a re-bake.
        /// </summary>
        private static NavMeshData Store(NavMeshBuildSettings settings, List<NavMeshBuildSource> sources, Bounds bounds)
        {
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
            if (existing != null)
            {
                if (!NavMeshBuilder.UpdateNavMeshData(existing, settings, sources, bounds)) return null;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) return null;
            AssetDatabase.CreateAsset(data, AssetPath);
            return data;
        }

        /// <summary>
        /// Puts <see cref="StaticNavMeshData"/> on the fleet root, pointing at <paramref name="data"/>.
        /// Leaves the prefab file untouched when it already does. Returns whether it changed.
        /// </summary>
        private static bool AttachToFleet(NavMeshData data)
        {
            GameObject fleet = PrefabUtility.LoadPrefabContents(SkyFleetBuilder.FleetPrefabPath);
            try
            {
                var provider = fleet.GetComponent<StaticNavMeshData>();
                if (provider != null && provider.Data == data)
                    return false;

                if (provider == null) provider = fleet.AddComponent<StaticNavMeshData>();
                provider.Configure(data);
                PrefabUtility.SaveAsPrefabAsset(fleet, SkyFleetBuilder.FleetPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fleet);
            }
        }
    }
}
