// Places the mock Clanker settlement: the first faction settlement in the world, assembled from
// the industrial structures the project already ships and garrisoned by the patrol robots until
// the Clanker bodies arrive (docs/superpowers/plans/2026-09-07-faction-system.md, Phase A).
//
// Why a builder rather than a generator dropped into a scene by hand: RobotSettlementGenerator
// and its recipe have been in the project for months, and on 2026-09-07 no scene contained the
// generator and every building slot on SettlementConfig.asset was empty -- the robot settlement
// had never actually been built anywhere. A generator placed by hand also cannot be reproduced:
// it rolls global UnityEngine.Random unless useSeed is ticked, and the ground it was dropped on is
// a click nobody can repeat. This command writes the recipe, chooses the ground, places the
// generator and generates, all from constants, so running it twice gives the same town.
//
// Re-run from: Tools > SpaceGame > Settlements > Build Mock Clanker Settlement
//              Tools > SpaceGame > Settlements > Build Mock Clanker Settlement + Bake NavMesh
//
// Site selection. The settlement wants ~300 m of level ground within walking distance of the
// spawn point but not on top of it. Chunk terrain is binary on disk, so the ground can only be
// read with the chunk scenes open: the builder opens every chunk that touches the search annulus,
// scores a 25 m grid of candidate centres by the height range across a 150 m disc
// (SettlementSiteScore -- pure, tested), throws out any candidate whose disc overlaps a terrain
// feature's footprint (mesas are not in the Terrain heightmap; they are meshes spawned at bake
// time, so a raycast in the editor cannot see them), and takes the flattest. Every scene it opened
// is closed again afterwards, the target chunk after saving.
//
// Two consequences worth knowing:
//   * The NavMesh is a single author-time bake over all 48 chunks (NavMeshSystem.md). New
//     buildings in a chunk make it stale, and WorldNavMeshBuildCheck fails the build until it is
//     re-baked. The "+ Bake NavMesh" variant does that in the same run.
//   * The Clankers, the two mounted outriders and the rocket are hand-placed instances of
//     networked, saveable prefabs
//     inside a chunk scene -- the documented "hand-placed chunk instance" path. They are
//     scene-authored, so their save ids derive from the hierarchy path: do not rename the
//     "ClankerSettlement" root or the "Generated" child once a world has been saved with them.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public static class ClankerSettlementBuilder
    {
        public const string RecipePath = "Assets/Game/ScriptableObjects/Settlements/ClankerSettlement.asset";

        /// <summary>Root object in the chunk scene. Part of the save-id path of everything under it.</summary>
        public const string RootName = "ClankerSettlement";

        /// <summary>Fixed seed so a rebuild reproduces the same town. Change it and the town changes.</summary>
        public const int Seed = 1701;

        /// <summary>
        /// The search annulus around the spawn point. Close enough to walk to on a first outing,
        /// far enough that the lander's crash site is not inside a robot compound.
        /// </summary>
        public const float MinDistanceFromSpawn = 450f;
        public const float MaxDistanceFromSpawn = 800f;

        /// <summary>
        /// Radius of the ground the town needs level. recipe.outerRadius × the rock apron factor
        /// TryPlaceRock uses (1.15), rounded up.
        /// </summary>
        public const float SiteRadius = 150f;

        private const float CandidateStep = 25f;
        private const float FeatureMargin = 60f;

        private const string PersistentScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        private const string StructureDir = "Assets/Game/Prefabs/Environment/Structures";
        private const string RobotDir = "Assets/Game/Prefabs/Agents/Robots";
        private const string RocketPath = "Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/CowBotRocket.prefab";

        /// <summary>The pre-existing, never-placed recipe. Only its foundation pad material is reused.</summary>
        private const string LegacyRecipePath = "Assets/Game/ScriptableObjects/Settlements/SettlementConfig.asset";

        // What stands where. The refinery is the landmark in the middle; relay outposts are the
        // "barracks" ring; a lattice outpost or two is the eco hub; a derelict mining rig on the
        // mid ring reads as the thing the Clankers came here to dig up. There is no turret prefab
        // in the project, so the outer ring is empty.
        //
        // No rocks, deliberately. Every prefab under Prefabs/Environment/Nature/Rocks has no
        // collider and carries the 100x FBX scale one level down (BoulderLarge_A's mesh child is
        // at 160x, BoulderSmall_C's root at 0.01x), so the "large" boulders come out 600-850 m
        // across and the small ones a metre. Measured 2026-09-07 in the first generated town: three
        // boulders hanging 25-45 m over the sand, each wider than the settlement. DEFECTS.md has
        // the entry; put rocks back when the prefabs are rebuilt through a builder.
        private const string CentrePrefab = StructureDir + "/Industrial/RefineryTower.prefab";
        private const string BarracksPrefab = StructureDir + "/Outpost/RelayOutpost.prefab";
        private const string EcoHubPrefab = StructureDir + "/Outpost/LatticeOutpost.prefab";
        private const string MidRingPrefab = StructureDir + "/Industrial/MiningRigDerelict.prefab";

        // The Clanker itself (ClankerBuilder). Until 2026-09-07 the garrison was the PatrolRobot
        // family standing in; "PatrolRobot 2" was never used because it ships on HumansFaction
        // (design doc §2.2) and would have garrisoned the town on the players' side.
        private static readonly string[] GarrisonPrefabs = { ClankerBuilder.PrefabPath };

        // Mounted Clankers (RobotHorseBuilder): robot horses with a Clanker in the saddle, on
        // their own slots so the town always fields this many rather than rolling for them.
        private const string OutriderPrefab = RobotHorseBuilder.OutriderPrefabPath;
        public const int OutriderCount = 3;

        // Ring radii. The refinery's clearance radius is (87 + padding) / 2 ≈ 47 m and a relay
        // outpost's ≈ 14 m, so the inner ring has to sit past 61 m from the centre; 75 leaves room
        // for the slot jitter. Everything else follows outward.
        private const float InnerRadius = 75f;
        private const float MidRadius = 105f;
        private const float OuterRadius = 130f;
        private const float BuildingPadding = 6f;

        /// <summary>Metres past the outer ring at which the alarm counts someone as inside.</summary>
        public const float AlarmMargin = 40f;

        /// <summary>
        /// The town's people, kept topped up by SettlementPopulation. The generated garrison is
        /// five on foot plus two mounted pairs (nine faction entities); the cap leaves room for a
        /// few more and replaces the fallen a wave at a time.
        /// </summary>
        public const int PopulationCap = 26;
        public const float PopulationInterval = 45f;
        public const int PopulationWave = 3;
        private const string OwnerFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/ClankerFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        [MenuItem("Tools/SpaceGame/Settlements/Build Mock Clanker Settlement")]
        public static void BuildMenu() => Build(bakeNavMesh: false);

        [MenuItem("Tools/SpaceGame/Settlements/Build Mock Clanker Settlement + Bake NavMesh")]
        public static void BuildAndBakeMenu() => Build(bakeNavMesh: true);

        [MenuItem("Tools/SpaceGame/Settlements/Build Mock Clanker Settlement", validate = true)]
        [MenuItem("Tools/SpaceGame/Settlements/Build Mock Clanker Settlement + Bake NavMesh", validate = true)]
        private static bool CanBuild() => !EditorApplication.isPlaying;

        public static void Build(bool bakeNavMesh)
        {
            RobotSettlementRecipe recipe = WriteRecipe();
            if (recipe == null) return;

            WorldStreamingConfig config = WorldNavMeshBaker.LoadConfig();
            if (config == null) return;

            if (!TryFindSpawnPoint(out Vector3 spawn)) return;

            var opened = new List<Scene>();
            try
            {
                OpenChunksAround(config, spawn, opened);
                Physics.SyncTransforms();

                if (!TryChooseSite(config, spawn, out Vector3 centre, out float heightRange))
                {
                    Debug.LogError("[ClankerSettlementBuilder] No level ground of radius " +
                                   $"{SiteRadius} m between {MinDistanceFromSpawn} and {MaxDistanceFromSpawn} m " +
                                   "of the spawn point that clears every terrain feature. Widen the annulus " +
                                   "or shrink SiteRadius.");
                    return;
                }

                ChunkInfo? chunk = config.GetChunk(config.WorldToChunkCoord(centre));
                if (chunk == null || string.IsNullOrEmpty(chunk.Value.scenePath))
                {
                    Debug.LogError($"[ClankerSettlementBuilder] {centre} is not inside any chunk scene.");
                    return;
                }

                Scene target = SceneManager.GetSceneByPath(chunk.Value.scenePath);
                if (!target.IsValid() || !target.isLoaded)
                {
                    Debug.LogError($"[ClankerSettlementBuilder] {chunk.Value.sceneName} is not open; the site " +
                                   "search should have opened it.");
                    return;
                }

                RobotSettlementGenerator generator = PlaceGenerator(target, recipe, centre);
                generator.Generate();

                if (!Verify(generator, out string report))
                {
                    Debug.LogError("[ClankerSettlementBuilder] " + report, generator);
                    return;
                }

                EditorSceneManager.MarkSceneDirty(target);
                if (!EditorSceneManager.SaveScene(target))
                {
                    Debug.LogError($"[ClankerSettlementBuilder] Could not save {chunk.Value.sceneName}.");
                    return;
                }

                Debug.Log($"[ClankerSettlementBuilder] Built the mock Clanker settlement in {chunk.Value.sceneName} " +
                          $"at {centre} (height range over the site {heightRange:F1} m, " +
                          $"{Vector3.Distance(spawn, centre):F0} m from spawn). {report}", generator);
            }
            finally
            {
                foreach (Scene scene in opened)
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            if (!bakeNavMesh)
            {
                Debug.LogWarning("[ClankerSettlementBuilder] The world NavMesh is now stale: run " +
                                 "World > Streaming > Bake World NavMesh before playing, or the garrison " +
                                 "stands still and the build check fails.");
                return;
            }

            Debug.Log(WorldNavMeshBaker.Bake(config));
        }

        // ── recipe ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or updates the recipe in place. Updating rather than recreating keeps the asset
        /// GUID, which the placed generator references.
        /// </summary>
        public static RobotSettlementRecipe WriteRecipe()
        {
            var recipe = AssetDatabase.LoadAssetAtPath<RobotSettlementRecipe>(RecipePath);
            bool created = recipe == null;
            if (created)
            {
                recipe = ScriptableObject.CreateInstance<RobotSettlementRecipe>();
                AssetDatabase.CreateAsset(recipe, RecipePath);
            }

            bool ok = true;
            recipe.shieldGeneratorPrefab = LoadRequired(CentrePrefab, ref ok);
            recipe.barracksPrefab = LoadRequired(BarracksPrefab, ref ok);
            recipe.barracksCount = new Vector2Int(3, 4);
            recipe.ecoHubPrefab = LoadRequired(EcoHubPrefab, ref ok);
            recipe.ecoHubCount = new Vector2Int(1, 2);
            recipe.satelliteDishPrefab = LoadRequired(MidRingPrefab, ref ok);
            recipe.satelliteDishCount = new Vector2Int(1, 1);
            recipe.turretPrefab = null;
            recipe.turretCount = Vector2Int.zero;

            recipe.rockPrefabs = Array.Empty<GameObject>();
            recipe.rockCount = Vector2Int.zero;

            recipe.robotPrefabs = GarrisonPrefabs.Select(p => LoadRequired(p, ref ok)).ToArray();
            recipe.robotGroupCount = new Vector2Int(4, 5);
            recipe.robotsPerGroup = new Vector2Int(3, 4);
            recipe.robotGroupSpread = 5f;

            recipe.outriderPrefabs = new[] { LoadRequired(OutriderPrefab, ref ok) };
            recipe.outriderTotal = OutriderCount;

            recipe.vehiclePrefabs = new[] { LoadRequired(RocketPath, ref ok) };
            recipe.vehicleTotal = 1;

            recipe.innerRadius = InnerRadius;
            recipe.midRadius = MidRadius;
            recipe.outerRadius = OuterRadius;
            recipe.minStructureSpacing = 12f;
            recipe.buildingFootprint = 25f;
            recipe.buildingPadding = BuildingPadding;
            recipe.organizedLayout = true;
            recipe.faceCenter = true;

            var legacy = AssetDatabase.LoadAssetAtPath<RobotSettlementRecipe>(LegacyRecipePath);
            if (legacy != null && legacy.foundationPadMaterial != null)
                recipe.foundationPadMaterial = legacy.foundationPadMaterial;

            if (!ok)
            {
                Debug.LogError("[ClankerSettlementBuilder] A prefab the recipe needs is missing (see above). " +
                               "Run Tools > Environment > Build Building Prefabs first.");
                return null;
            }

            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();

            // Read the write back: a read-only AssetDatabase discards saves silently.
            var reloaded = AssetDatabase.LoadAssetAtPath<RobotSettlementRecipe>(RecipePath);
            if (reloaded == null || reloaded.shieldGeneratorPrefab == null || reloaded.robotPrefabs.Length == 0)
            {
                Debug.LogError($"[ClankerSettlementBuilder] {RecipePath} did not save.");
                return null;
            }

            Debug.Log($"[ClankerSettlementBuilder] {(created ? "Created" : "Updated")} {RecipePath}.");
            return reloaded;
        }

        private static GameObject LoadRequired(string path, ref bool ok)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[ClankerSettlementBuilder] Missing prefab: {path}");
                ok = false;
            }
            return prefab;
        }

        // ── site ─────────────────────────────────────────────────────────────────

        internal static bool TryFindSpawnPoint(out Vector3 position)
        {
            position = default;
            Scene scene = SceneManager.GetSceneByPath(PersistentScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen)
                scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Additive);

            try
            {
                SpawnPoint spawn = scene.GetRootGameObjects()
                    .Select(g => g.GetComponentInChildren<SpawnPoint>(true))
                    .FirstOrDefault(s => s != null);
                if (spawn == null)
                {
                    Debug.LogError($"[ClankerSettlementBuilder] No SpawnPoint in {PersistentScenePath}.");
                    return false;
                }

                position = spawn.transform.position;
                return true;
            }
            finally
            {
                if (!alreadyOpen)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        /// <summary>
        /// Opens every chunk whose bounds, grown by the site radius, touch the search annulus's
        /// bounding box. Only scenes this call opened go into <paramref name="opened"/>.
        /// </summary>
        private static void OpenChunksAround(WorldStreamingConfig config, Vector3 spawn, List<Scene> opened)
        {
            float reach = MaxDistanceFromSpawn + SiteRadius;
            var search = new Bounds(spawn, new Vector3(reach * 2f, 10000f, reach * 2f));

            foreach (ChunkInfo chunk in config.chunks)
            {
                if (string.IsNullOrEmpty(chunk.scenePath)) continue;

                Bounds grown = chunk.worldBounds;
                grown.Expand(new Vector3(SiteRadius * 2f, 10000f, SiteRadius * 2f));
                if (!grown.Intersects(search)) continue;

                Scene scene = SceneManager.GetSceneByPath(chunk.scenePath);
                if (scene.IsValid() && scene.isLoaded) continue;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(chunk.scenePath) == null) continue;

                opened.Add(EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive));
            }
        }

        private static bool TryChooseSite(WorldStreamingConfig config, Vector3 spawn,
                                          out Vector3 centre, out float bestRange)
        {
            Terrain[] terrains = Terrain.activeTerrains;
            List<Bounds> keepOut = FeatureKeepOutBounds();

            float? HeightAt(float x, float z)
            {
                var p = new Vector3(x, 0f, z);
                foreach (Terrain terrain in terrains)
                {
                    if (terrain == null || terrain.terrainData == null) continue;
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z) continue;
                    return origin.y + terrain.SampleHeight(p);
                }
                return null;
            }

            centre = default;
            bestRange = float.MaxValue;
            bool found = false;

            for (float x = spawn.x - MaxDistanceFromSpawn; x <= spawn.x + MaxDistanceFromSpawn; x += CandidateStep)
            for (float z = spawn.z - MaxDistanceFromSpawn; z <= spawn.z + MaxDistanceFromSpawn; z += CandidateStep)
            {
                var flat = new Vector2(x, z);
                float distance = Vector2.Distance(flat, new Vector2(spawn.x, spawn.z));
                if (distance < MinDistanceFromSpawn || distance > MaxDistanceFromSpawn) continue;

                if (!DiscInsideGrid(config, flat)) continue;
                if (DiscTouches(keepOut, flat)) continue;

                if (!SettlementSiteScore.TryEvaluate(HeightAt, flat, SiteRadius, out float range, out float centreHeight))
                    continue;

                if (range >= bestRange) continue;
                bestRange = range;
                centre = new Vector3(x, centreHeight, z);
                found = true;
            }

            return found;
        }

        private static bool DiscInsideGrid(WorldStreamingConfig config, Vector2 flat)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                var edge = new Vector3(flat.x + Mathf.Cos(a) * SiteRadius, 0f, flat.y + Mathf.Sin(a) * SiteRadius);
                if (!config.IsWithinGrid(edge)) return false;
            }
            return true;
        }

        private static bool DiscTouches(List<Bounds> keepOut, Vector2 flat)
        {
            var disc = new Bounds(new Vector3(flat.x, 0f, flat.y), new Vector3(SiteRadius * 2f, 10000f, SiteRadius * 2f));
            foreach (Bounds b in keepOut)
                if (b.Intersects(disc)) return true;
            return false;
        }

        /// <summary>
        /// World-space, axis-aligned, margin-grown boxes around every terrain feature in the open
        /// scenes. The feature's own mesh is not present in an editor scene (it is spawned at bake
        /// time), so its authored footprint is the only thing that says where the mesa will be.
        /// </summary>
        private static List<Bounds> FeatureKeepOutBounds()
        {
            var result = new List<Bounds>();
            foreach (TerrainFeatureSpawner spawner in
                     UnityEngine.Object.FindObjectsByType<TerrainFeatureSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Bounds local = spawner.Area.ComputeLocalBounds();
                Transform t = spawner.transform;
                Vector3 centre = t.TransformPoint(local.center);
                Vector3 size = Vector3.Scale(local.size, t.lossyScale);
                // A rotated feature's box is taken at its diagonal, so the margin errs on the safe side.
                float diagonal = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 1.42f;
                var world = new Bounds(centre, new Vector3(diagonal, 10000f, diagonal));
                world.Expand(FeatureMargin * 2f);
                result.Add(world);
            }
            return result;
        }

        // ── scene ────────────────────────────────────────────────────────────────

        private static RobotSettlementGenerator PlaceGenerator(Scene scene, RobotSettlementRecipe recipe, Vector3 centre)
        {
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.SetPositionAndRotation(centre, Quaternion.identity);

            if (!root.TryGetComponent(out RobotSettlementGenerator generator))
                generator = root.AddComponent<RobotSettlementGenerator>();

            generator.recipe = recipe;
            generator.useSeed = true;
            generator.seed = Seed;
            EditorUtility.SetDirty(generator);

            // The town notices intruders: anyone the owning faction is hostile toward inside the
            // outer ring plus a margin raises the siren and rallies every idle defender.
            if (!root.TryGetComponent(out SpaceGame.Agents.SettlementAlarm alarm))
                alarm = root.AddComponent<SpaceGame.Agents.SettlementAlarm>();
            alarm.Configure(
                AssetDatabase.LoadAssetAtPath<SpaceGame.Agents.FactionDefinition>(OwnerFactionPath),
                AssetDatabase.LoadAssetAtPath<SpaceGame.Agents.FactionRelationshipTable>(RelationshipsPath),
                OuterRadius + AlarmMargin);
            EditorUtility.SetDirty(alarm);

            // The town keeps its people coming: topped up to PopulationCap every PopulationInterval
            // seconds, mostly on foot and sometimes mounted, in the ring the garrison patrols and
            // never in a player's face. It holds while the alarm is raised so a fight can be won.
            if (!root.TryGetComponent(out SpaceGame.Agents.SettlementPopulation population))
                population = root.AddComponent<SpaceGame.Agents.SettlementPopulation>();
            population.Configure(
                AssetDatabase.LoadAssetAtPath<SpaceGame.Agents.FactionDefinition>(OwnerFactionPath),
                AssetDatabase.LoadAssetAtPath<SpaceGame.Agents.FactionRelationshipTable>(RelationshipsPath),
                new[]
                {
                    new SpaceGame.Agents.SettlementPopulation.Inhabitant { prefab = recipe.robotPrefabs[0], weight = 3 },
                    new SpaceGame.Agents.SettlementPopulation.Inhabitant { prefab = recipe.outriderPrefabs[0], weight = 1 },
                },
                PopulationCap, PopulationInterval,
                InnerRadius * 0.6f, OuterRadius * 0.9f, OuterRadius + AlarmMargin);
            var waves = new SerializedObject(population);
            waves.FindProperty("spawnsPerWave").intValue = PopulationWave;
            waves.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(population);
            return generator;
        }

        private static bool Verify(RobotSettlementGenerator generator, out string report)
        {
            Transform generated = generator.transform.Find("Generated");
            if (generated == null)
            {
                report = "Generate() produced no 'Generated' child.";
                return false;
            }

            int buildings = 0, robots = 0, outriders = 0, vehicles = 0, pads = 0;
            foreach (Transform child in generated)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                string path = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;

                if (path.StartsWith(StructureDir, StringComparison.Ordinal)) buildings++;
                else if (path == OutriderPrefab) outriders++;
                else if (path.StartsWith(RobotDir, StringComparison.Ordinal)) robots++;
                else if (path == RocketPath) vehicles++;
                else pads++;
            }

            // The centre building plus the inner-ring minimum. Fewer means the ground raycasts
            // missed -- the chunk's terrain collider was not there to hit.
            int minimumBuildings = 1 + generator.recipe.barracksCount.x + generator.recipe.ecoHubCount.x;
            report = $"{buildings} buildings, {robots} robots, {outriders} outrider(s), {vehicles} vehicle(s), {pads} pad(s).";
            if (buildings < minimumBuildings)
            {
                report = $"only {buildings} of at least {minimumBuildings} buildings were placed -- did the " +
                         "ground raycast find the terrain? " + report;
                return false;
            }

            if (robots == 0)
            {
                report = "no garrison was placed. " + report;
                return false;
            }

            if (outriders < generator.recipe.outriderTotal)
            {
                report = $"only {outriders} of {generator.recipe.outriderTotal} outriders were placed. " + report;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// How level a disc of ground is. Pure so the test suite can run it over a synthetic height
    /// function: no Terrain, no scene.
    /// </summary>
    public static class SettlementSiteScore
    {
        /// <summary>Rings sampled between the centre and the rim, plus the centre itself.</summary>
        public const int Rings = 3;
        public const int SamplesPerRing = 8;

        /// <summary>
        /// Samples the disc and reports the height range across it. False when any sample falls
        /// off the ground (<paramref name="heightAt"/> returned null) -- a site half off the edge
        /// of the world is not a site.
        /// </summary>
        public static bool TryEvaluate(Func<float, float, float?> heightAt, Vector2 centre, float radius,
                                       out float heightRange, out float centreHeight)
        {
            heightRange = 0f;
            centreHeight = 0f;

            float? c = heightAt(centre.x, centre.y);
            if (c == null) return false;

            centreHeight = c.Value;
            float min = c.Value, max = c.Value;

            for (int ring = 1; ring <= Rings; ring++)
            {
                float r = radius * ring / Rings;
                for (int i = 0; i < SamplesPerRing; i++)
                {
                    float a = i * Mathf.PI * 2f / SamplesPerRing;
                    float? h = heightAt(centre.x + Mathf.Cos(a) * r, centre.y + Mathf.Sin(a) * r);
                    if (h == null) return false;
                    if (h.Value < min) min = h.Value;
                    if (h.Value > max) max = h.Value;
                }
            }

            heightRange = max - min;
            return true;
        }
    }
}
