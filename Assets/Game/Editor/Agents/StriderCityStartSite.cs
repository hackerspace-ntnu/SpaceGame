// Where the Striders' walking city starts in a new world.
//
// Left to itself NpcWorldSim seeds the city at a Ruin, and no Ruin is registered when groups are
// seeded, so it fell back to the sim's own origin ~4 km from where a new player lands, and a group
// only exists as live objects within spawnRadius of a player. Nobody ever met it. So the city gets
// a fixed start: walkable, level ground about CityStartDistance from the SpawnPoint, clear of the
// Clanker town.
//
// Level means the city's whole footprint (StriderCityBuilder.CityLevelGround, the square its column
// covers) is within the city's slope limit, measured by LevelGroundSearch over the chunk heightmaps —
// the same rule and measure its stops are held to at runtime. Of every acceptable candidate in the
// distance band the flattest wins, ties to the earlier one, and the candidates are listed facing away
// from the Clanker town first. Nothing acceptable is an error, never a fallback.
//
// Walkable means on the world NavMesh: the one author-time bake (NavMeshSystem.md) is added for the
// search and removed again, sampled at the terrain height of each candidate. Terrain is binary on
// disk, so the chunk scenes round the spawn are opened for the search and closed afterwards, the
// way ClankerSettlementBuilder picks its site. Everything is constants, so a re-run picks the same
// point.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public static class StriderCityStartSite
    {
        /// <summary>
        /// Metres from the SpawnPoint. Past the Clanker town's annulus and a few minutes' walk, so the
        /// city is something a new player comes across on a first outing, not a crowd on the lander.
        /// </summary>
        public const float CityStartDistance = 800f;

        /// <summary>
        /// How far either side of <see cref="CityStartDistance"/> the search may go for level ground:
        /// a minute's walk, so the start is still a first outing's find.
        /// </summary>
        public const float CityStartBand = 100f;

        /// <summary>Spacing of the distances tried across the band: fine enough not to step over a level pan, coarse enough to keep the search to 80 footprints.</summary>
        private const float CityStartBandStep = 50f;

        /// <summary>
        /// How far the start keeps from the Clanker town's centre: its alarm ring plus the city's
        /// footprint, so no member of the column stands inside the ring whichever way it faces.
        /// (A flat 300 m once did; the city is ~435 m across, and the only level ground near the spawn
        /// is the basin the town itself was sited on.)
        /// </summary>
        public static float ClankerTownClearance =>
            ClankerSettlementBuilder.AlarmRadius + StriderCityBuilder.CityLevelGround.footprintRadius;

        /// <summary>Directions tried round the spawn, the first facing away from the Clanker town.</summary>
        private const int CandidateDirections = 16;

        /// <summary>How far from a candidate's terrain point the NavMesh may be and still count as that ground.</summary>
        private const float NavMeshSampleReach = 10f;

        /// <summary>
        /// Picks the start and reports the spawn point it was measured from. False, with an error
        /// logged, when there is no spawn point, no world config or NavMesh, or no walkable level
        /// ground in the band.
        /// </summary>
        public static bool TryChoose(out Vector3 start, out Vector3 spawn)
        {
            start = default;
            if (!ClankerSettlementBuilder.TryFindSpawnPoint(out spawn)) return false;

            WorldStreamingConfig config = WorldNavMeshBaker.LoadConfig();
            var navMesh = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshBaker.AssetPath);
            if (config == null || navMesh == null || navMesh.bakedData == null)
            {
                Debug.LogError($"[StriderCityStartSite] No world config or baked NavMesh at {WorldNavMeshBaker.AssetPath}.");
                return false;
            }

            LevelGroundRule rule = StriderCityBuilder.CityLevelGround;
            var opened = new List<Scene>();
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(navMesh.bakedData);
            try
            {
                // Far enough for the corners of the footprint round the farthest candidate.
                float reach = CityStartDistance + CityStartBand + rule.footprintRadius * Mathf.Sqrt(2f);
                ClankerSettlementBuilder.OpenChunksAround(config, spawn, reach, opened);
                Vector3? town = FindClankerTown();
                Terrain[] terrains = Terrain.activeTerrains;

                Vector2 spawnXZ = Flat(spawn);
                Vector2 away = town.HasValue ? (spawnXZ - Flat(town.Value)).normalized : Vector2.up;
                if (away == Vector2.zero) away = Vector2.up;

                bool Heightmap(Vector2 at, out float y)
                {
                    float? height = ClankerSettlementBuilder.TerrainHeightAt(terrains, at.x, at.y);
                    y = height ?? 0f;
                    return height.HasValue;
                }

                bool OnNavMesh(Vector2 at, out Vector3 onMesh)
                {
                    onMesh = default;
                    if (!Heightmap(at, out float y)) return false;
                    if (!NavMesh.SamplePosition(new Vector3(at.x, y, at.y), out NavMeshHit hit, NavMeshSampleReach, NavMesh.AllAreas))
                        return false;
                    onMesh = hit.position;
                    return true;
                }

                bool Acceptable(Vector2 at) =>
                    IsOwedGround(config, new Vector3(at.x, 0f, at.y))
                    && OnNavMesh(at, out Vector3 _)
                    && (!town.HasValue || Vector2.Distance(at, Flat(town.Value)) >= ClankerTownClearance);

                List<Vector2> candidates = Candidates(spawnXZ, away);
                if (!TryPickFlattest(candidates, Acceptable, Heightmap, rule, out Vector2 site, out float spread))
                {
                    Debug.LogError($"[StriderCityStartSite] No walkable ground {CityStartDistance - CityStartBand}-" +
                                   $"{CityStartDistance + CityStartBand} m from spawn {spawn:F0} in {CandidateDirections} " +
                                   $"directions is level: the city's {rule.footprintRadius * 2f:F0} m footprint must be " +
                                   $"within {rule.maxSlopeDegrees}° ({rule.MaxSpread:F1} m of relief). Widen the band or " +
                                   "raise StriderCityBuilder.CityMaxSlopeDegrees.");
                    return false;
                }

                OnNavMesh(site, out start);
                Debug.Log($"[StriderCityStartSite] City starts at {start:F0}, {Vector2.Distance(Flat(start), spawnXZ):F0} m " +
                          $"from spawn {spawn:F0} at a bearing of {Quaternion.LookRotation(start - spawn).eulerAngles.y:F0}°, " +
                          $"{spread:F1} m of relief across its footprint (limit {rule.MaxSpread:F1}) " +
                          (town.HasValue ? $"(Clanker town at {town.Value:F0})." : "(no Clanker town found)."));
                return true;
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
                foreach (Scene scene in opened)
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        /// <summary>
        /// Every place the start may be, in order of preference: bearings fanning out either side of
        /// <paramref name="away"/> (directly away first), and at each bearing the distances across the
        /// band nearest <see cref="CityStartDistance"/> first.
        /// </summary>
        internal static List<Vector2> Candidates(Vector2 spawn, Vector2 away)
        {
            var candidates = new List<Vector2>();
            float step = 360f / CandidateDirections;
            int bandSteps = Mathf.FloorToInt(CityStartBand / CityStartBandStep);

            for (int i = 0; i < CandidateDirections; i++)
            {
                float angle = (i + 1) / 2 * step * (i % 2 == 0 ? 1f : -1f);
                Vector3 turned = Quaternion.Euler(0f, angle, 0f) * new Vector3(away.x, 0f, away.y);
                var bearing = new Vector2(turned.x, turned.z);

                for (int d = 0; d <= 2 * bandSteps; d++)
                {
                    float offset = (d + 1) / 2 * CityStartBandStep * (d % 2 == 0 ? 1f : -1f);
                    candidates.Add(spawn + bearing * (CityStartDistance + offset));
                }
            }
            return candidates;
        }

        /// <summary>
        /// The flattest of the <paramref name="acceptable"/> candidates whose footprint is level by
        /// <paramref name="rule"/>, by <see cref="LevelGroundSearch.TryFindFlattest"/>; ties go to the
        /// earlier candidate. False when none qualifies. Pure: the ground comes from <paramref name="sample"/>.
        /// </summary>
        internal static bool TryPickFlattest(IReadOnlyList<Vector2> candidates, Func<Vector2, bool> acceptable,
                                             HullFootprint.GroundSampler sample, LevelGroundRule rule,
                                             out Vector2 site, out float spread)
        {
            site = default;
            List<Vector2> usable = candidates.Where(acceptable).ToList();
            if (!LevelGroundSearch.TryFindFlattest(usable, 0f, rule.Extents, rule.MaxSpread, sample,
                                                   out int index, out spread))
                return false;

            site = usable[index];
            return true;
        }

        /// <summary>Inside the grid and in a chunk authored with terrain — the empty padding columns are not ground.</summary>
        private static bool IsOwedGround(WorldStreamingConfig config, Vector3 position)
        {
            if (!config.IsWithinGrid(position)) return false;
            ChunkInfo? chunk = config.GetChunk(config.WorldToChunkCoord(position));
            return chunk.HasValue && chunk.Value.hasTerrain;
        }

        private static Vector3? FindClankerTown()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == ClankerSettlementBuilder.RootName);
                if (root != null) return root.transform.position;
            }
            return null;
        }

        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    }
}
