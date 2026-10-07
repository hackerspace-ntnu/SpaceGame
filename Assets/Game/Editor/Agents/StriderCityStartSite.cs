// Where the Striders' walking city starts in a new world.
//
// Left to itself NpcWorldSim seeds the city at a Ruin, and no Ruin is registered when groups are
// seeded, so it fell back to the sim's own origin ~4 km from where a new player lands, and a group
// only exists as live objects within spawnRadius of a player. Nobody ever met it. So the city gets
// a fixed start: the walkable, level ground nearest CityStartAnchor, within CityStartReach of it.
// The anchor is where the user stood in play and asked for the city (2026-10-05). It was anchored on
// the SpawnPoint before, but the merge with main moved the SpawnPoint 1.2 km from the ground the
// user meant (and it was briefly on the map's centre before that); nobody found the city either time.
// The anchor is about 450 m from the Clanker town's centre, so the back of the column can reach the
// town's alarm ring: the user chose the place, and the two tribes fight there.
//
// Level means the city's whole footprint (StriderCityBuilder.CityLevelGround, the square its column
// covers) is within the city's slope limit, measured by HullFootprint over the chunk heightmaps,
// the same rule and measure its stops are held to at runtime. The candidates are a grid round the
// anchor, nearest first, and the first acceptable level one wins. Nothing acceptable is an error,
// never a fallback.
//
// Walkable means on the world NavMesh: the one author-time bake (NavMeshSystem.md) is added for the
// search and removed again, sampled at the terrain height of each candidate. Terrain is binary on
// disk, so the chunk scenes round the anchor are opened for the search and closed afterwards, the
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
        /// <summary>Where the user stood in play when they asked for the city to start there (2026-10-05).</summary>
        public static readonly Vector2 CityStartAnchor = new Vector2(3418f, 1199f);

        /// <summary>How far from <see cref="CityStartAnchor"/> the search may go for level ground.</summary>
        public const float CityStartReach = 300f;

        /// <summary>Spacing of the candidate grid: fine enough not to step over a level pan, coarse enough to keep the search to ~110 footprints.</summary>
        internal const float CityStartStep = 50f;

        /// <summary>How far from a candidate's terrain point the NavMesh may be and still count as that ground.</summary>
        private const float NavMeshSampleReach = 10f;

        /// <summary>
        /// Picks the start. False, with an error logged, when there is no world config or NavMesh, or no
        /// walkable level ground within reach of the anchor.
        /// </summary>
        public static bool TryChoose(out Vector3 start)
        {
            start = default;

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
                float reach = CityStartReach + rule.footprintRadius * Mathf.Sqrt(2f);
                ClankerSettlementBuilder.OpenChunksAround(config, new Vector3(CityStartAnchor.x, 0f, CityStartAnchor.y), reach, opened);
                Terrain[] terrains = Terrain.activeTerrains;

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
                    IsOwedGround(config, new Vector3(at.x, 0f, at.y)) && OnNavMesh(at, out Vector3 _);

                if (!TryPickNearest(Candidates(CityStartAnchor), Acceptable, Heightmap, rule, out Vector2 site, out float spread))
                {
                    Debug.LogError($"[StriderCityStartSite] No walkable ground within {CityStartReach} m of {CityStartAnchor} is " +
                                   $"level: the city's {rule.footprintRadius * 2f:F0} m footprint must be within " +
                                   $"{rule.maxSlopeDegrees}° ({rule.MaxSpread:F1} m of relief). Raise CityStartReach or " +
                                   "StriderCityBuilder.CityMaxSlopeDegrees.");
                    return false;
                }

                OnNavMesh(site, out start);
                Debug.Log($"[StriderCityStartSite] City starts at {start:F0}, {Vector2.Distance(site, CityStartAnchor):F0} m " +
                          $"from the anchor {CityStartAnchor}, {spread:F1} m of relief across its footprint " +
                          $"(limit {rule.MaxSpread:F1}).");
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
        /// Every place the start may be, in order of preference: a <see cref="CityStartStep"/> grid round
        /// <paramref name="anchor"/> within <see cref="CityStartReach"/>, nearest first.
        /// </summary>
        internal static List<Vector2> Candidates(Vector2 anchor)
        {
            int cells = Mathf.FloorToInt(CityStartReach / CityStartStep);

            var candidates = new List<Vector2>();
            for (int x = -cells; x <= cells; x++)
                for (int z = -cells; z <= cells; z++)
                {
                    var offset = new Vector2(x, z) * CityStartStep;
                    if (offset.magnitude <= CityStartReach) candidates.Add(anchor + offset);
                }

            // OrderBy is stable, so equal distances keep the grid's order and a re-run picks the same point.
            return candidates.OrderBy(c => Vector2.Distance(c, anchor)).ToList();
        }

        /// <summary>
        /// The first of the <paramref name="acceptable"/> candidates whose footprint is level by
        /// <paramref name="rule"/>, measured whole by <see cref="HullFootprint.Measure"/>. False when none
        /// qualifies. Pure: the ground comes from <paramref name="sample"/>.
        /// </summary>
        internal static bool TryPickNearest(IReadOnlyList<Vector2> candidates, Func<Vector2, bool> acceptable,
                                            HullFootprint.GroundSampler sample, LevelGroundRule rule,
                                            out Vector2 site, out float spread)
        {
            var scratch = new Vector2[HullFootprint.SampleCount];
            foreach (Vector2 candidate in candidates)
            {
                if (!acceptable(candidate)) continue;
                HullFootprint.Ground ground = HullFootprint.Measure(candidate, 0f, rule.Extents, sample, scratch);
                if (!ground.Complete || ground.Spread > rule.MaxSpread) continue;

                site = candidate;
                spread = ground.Spread;
                return true;
            }

            site = default;
            spread = float.PositiveInfinity;
            return false;
        }

        /// <summary>Inside the grid and in a chunk authored with terrain — the empty padding columns are not ground.</summary>
        private static bool IsOwedGround(WorldStreamingConfig config, Vector3 position)
        {
            if (!config.IsWithinGrid(position)) return false;
            ChunkInfo? chunk = config.GetChunk(config.WorldToChunkCoord(position));
            return chunk.HasValue && chunk.Value.hasTerrain;
        }

    }
}
