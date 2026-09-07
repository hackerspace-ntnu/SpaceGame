// Drops a small Clanker squad a short walk from the lander, so the robots can be met without the
// 800 m hike to their settlement. A playtest convenience, placed by a command rather than by hand
// for the usual reason: hand-placed instances in a chunk scene have no record of where they came
// from, and the next person cannot move or remove them without hunting through the hierarchy.
//
// The squad is a root named "ClankerSquad" in whichever chunk holds the spawn point; re-running
// replaces it. Its members are ordinary hand-placed chunk instances -- networked, saveable, and
// excluded from the NavMesh bake because they walk it (WorldNavMeshBaker.IsBakeable) -- so the
// only thing a run leaves stale is the chunk's dependency hash, which is why it re-bakes.
//
// Re-run from: Tools > SpaceGame > Agents > Place Clanker Squad Near Spawn
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public static class ClankerSquadPlacer
    {
        public const string RootName = "ClankerSquad";
        public const int SquadSize = 3;

        /// <summary>
        /// A stray robot horse, saddled and riderless, this far from the squad: something to
        /// climb on within sight of the ship, so mounting can be tried without the walk to town.
        /// </summary>
        public const float StrayHorseDistance = 16f;

        /// <summary>
        /// Where the squad stands relative to the spawn point. Close enough to reach in a minute,
        /// far enough that nobody spawns inside a firefight; the lander's arrival needs the first
        /// seconds to itself. All offsets stay inside the spawn's own chunk.
        /// </summary>
        public static readonly Vector3 Offset = new Vector3(-60f, 0f, 45f);
        private const float Spread = 4f;
        private const int Seed = 1701;

        [MenuItem("Tools/SpaceGame/Agents/Place Clanker Squad Near Spawn")]
        public static void PlaceMenu() => Place();

        public static void Place()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClankerBuilder.PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[ClankerSquadPlacer] No prefab at {ClankerBuilder.PrefabPath}; build the Clanker first.");
                return;
            }

            WorldStreamingConfig config = WorldNavMeshBaker.LoadConfig();
            if (config == null) return;
            if (!ClankerSettlementBuilder.TryFindSpawnPoint(out Vector3 spawn)) return;

            Vector3 centre = spawn + Offset;
            ChunkInfo? chunk = config.GetChunk(config.WorldToChunkCoord(centre));
            if (chunk == null || string.IsNullOrEmpty(chunk.Value.scenePath))
            {
                Debug.LogError($"[ClankerSquadPlacer] {centre} is not inside any chunk scene.");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(chunk.Value.scenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(chunk.Value.scenePath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                Terrain terrain = scene.GetRootGameObjects()
                    .Select(g => g.GetComponentInChildren<Terrain>(true))
                    .FirstOrDefault(t => t != null);
                if (terrain == null)
                {
                    Debug.LogError($"[ClankerSquadPlacer] {chunk.Value.sceneName} has no Terrain to stand the squad on.");
                    return;
                }

                GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
                if (root != null) Object.DestroyImmediate(root);
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = centre;

                var rng = new System.Random(Seed);
                var placed = new List<Vector3>();
                for (int i = 0; i < SquadSize; i++)
                {
                    float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    Vector3 flat = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Spread * (i + 1) / SquadSize;
                    float y = terrain.transform.position.y + terrain.SampleHeight(flat);
                    var member = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    member.transform.SetPositionAndRotation(new Vector3(flat.x, y, flat.z),
                                                            Quaternion.Euler(0f, (float)(rng.NextDouble() * 360f), 0f));
                    placed.Add(member.transform.position);
                }

                GameObject horsePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotHorseBuilder.WildPrefabPath);
                if (horsePrefab == null)
                {
                    Debug.LogWarning($"[ClankerSquadPlacer] No robot horse at {RobotHorseBuilder.WildPrefabPath}; " +
                                     "the squad is placed without its stray.");
                }
                else
                {
                    float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    Vector3 flat = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * StrayHorseDistance;
                    float y = terrain.transform.position.y + terrain.SampleHeight(flat);
                    var horse = (GameObject)PrefabUtility.InstantiatePrefab(horsePrefab, root.transform);
                    horse.transform.SetPositionAndRotation(new Vector3(flat.x, y, flat.z),
                                                           Quaternion.Euler(0f, (float)(rng.NextDouble() * 360f), 0f));
                    placed.Add(horse.transform.position);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    Debug.LogError($"[ClankerSquadPlacer] Could not save {chunk.Value.sceneName}.");
                    return;
                }

                Debug.Log($"[ClankerSquadPlacer] {SquadSize} Clankers and a stray robot horse placed in {chunk.Value.sceneName} around {centre} " +
                          $"({Vector3.Distance(spawn, centre):F0} m from the spawn point): " +
                          string.Join(", ", placed.Select(p => p.ToString("F0"))));
            }
            finally
            {
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Debug.Log(WorldNavMeshBaker.Bake(config));
        }
    }
}
