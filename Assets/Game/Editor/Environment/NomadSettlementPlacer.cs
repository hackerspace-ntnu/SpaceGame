// Places the nine nomad settlements -- two large, three medium, four small -- across the main world.
//
// A builder rather than nine generators dropped into scenes by hand, for the reason
// ClankerSettlementBuilder records at length: a hand-placed generator sits on ground nobody can click
// again, and its layout depends on a sequence nobody wrote down. This command chooses every site from
// constants and a fixed seed, so running it twice gives the same nine towns.
//
// Site selection. Chunk terrain is binary on disk and can only be read with the chunk scenes open, so
// the placer opens every chunk that has terrain, scores a grid of candidate centres by the height
// range across the town's disc (SettlementSiteScore -- pure and tested, reused from the Clanker
// builder), and takes the flattest that clears everything it must keep away from: the spawn point,
// the Clanker settlement, every terrain feature's footprint, the edge of its own chunk, and every
// site already chosen.
//
// Two constraints that are not obvious:
//
//   * A settlement must not straddle a chunk boundary. Everything it makes lives in ONE chunk scene,
//     so a town whose far side reached into the neighbouring chunk would have half its buildings
//     appear and vanish with a chunk the player is nowhere near.
//   * A terrain feature's mesh is NOT in the chunk scene -- it is spawned at bake time from a baked
//     asset -- so neither a raycast nor Terrain.SampleHeight can see a mesa. The authored footprint
//     on TerrainFeatureSpawner is the only thing that says where one will be.
//
// Re-run from: Tools > SpaceGame > Settlements > Build Nomad Settlements
//              Tools > SpaceGame > Settlements > Build Nomad Settlements + Bake NavMesh
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;
using SpaceGame.World.Safety;

namespace SpaceGame.EditorTools
{
    public static class NomadSettlementPlacer
    {
        private const string BuildingDir = "Assets/Game/Prefabs/Environment/Structures/NomadSettlement";
        private const string CharacterDir = "Assets/Game/Prefabs/agents/Characters";
        private const string MountedPrefab = "Assets/Game/Prefabs/agents/Caravan/NomadOstrich.prefab";

        /// <summary>Changing this moves every settlement. It is the whole world's layout in one number.</summary>
        private const int BaseSeed = 20260913;

        private const float CandidateStep = 50f;
        private const float FeatureMargin = 60f;
        private const float SpawnKeepOut = 300f;
        private const float ClankerKeepOut = 250f;

        /// <summary>
        /// Metres between any two settlements. Chunks are 500 m with loadRadius 1, so the streaming
        /// window is 1500 m across; 800 m keeps the common case to one town live at a time, and keeps
        /// each one a separate landmark rather than one continuous smear of adobe.
        /// </summary>
        private const float MinimumSeparation = 800f;

        /// <summary>
        /// The freestanding shade sails. The ten Wall* ones are already hung on the buildings by
        /// NomadSettlementBuilder and must not be scattered as well.
        /// </summary>
        private static readonly string[] FreestandingTents =
        {
            "QuadSmall", "QuadLarge", "Tri", "TriTall", "Penta", "HexLow", "Ribbon", "Kite",
        };

        private static readonly string[] NomadPrefabs =
        {
            CharacterDir + "/Nomad_Tan.prefab",
            CharacterDir + "/Nomad_Umber.prefab",
            CharacterDir + "/Nomad_Maroon.prefab",
            CharacterDir + "/Nomad_StrawHat.prefab",
        };

        /// <summary>The towns, in the order they claim ground. Largest first: they need the most of it.</summary>
        private static readonly (NomadSettlementSize Size, string Name)[] Settlements =
        {
            (NomadSettlementSize.Large, "RasTamir"),
            (NomadSettlementSize.Large, "GhafWells"),
            (NomadSettlementSize.Medium, "Sabkha"),
            (NomadSettlementSize.Medium, "KhorDune"),
            (NomadSettlementSize.Medium, "AlMarrah"),
            (NomadSettlementSize.Small, "DryStand"),
            (NomadSettlementSize.Small, "TwoPoles"),
            (NomadSettlementSize.Small, "LatchCamp"),
            (NomadSettlementSize.Small, "Windward"),
        };

        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements")]
        public static void BuildMenu() => Build(bakeNavMesh: false);

        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements + Bake NavMesh")]
        public static void BuildAndBakeMenu() => Build(bakeNavMesh: true);

        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements", validate = true)]
        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements + Bake NavMesh", validate = true)]
        private static bool CanBuild() => !EditorApplication.isPlaying;

        public static void Build(bool bakeNavMesh)
        {
            if (!LoadPrefabs(out Prefabs prefabs)) return;

            WorldStreamingConfig config = WorldNavMeshBaker.LoadConfig();
            if (config == null) return;

            if (!ClankerSettlementBuilder.TryFindSpawnPoint(out Vector3 spawn)) return;

            var opened = new List<Scene>();
            var report = new StringBuilder("NomadSettlementPlacer\n");
            var skipped = new List<string>();
            int placed = 0;
            bool fatal = false;

            try
            {
                OpenTerrainChunks(config, opened);
                RemoveExistingSettlements();
                Physics.SyncTransforms();

                List<Bounds> keepOut = BuildKeepOut(spawn);
                var chosen = new List<Vector3>();

                for (int index = 0; index < Settlements.Length; index++)
                {
                    (NomadSettlementSize size, string name) = Settlements[index];
                    float siteRadius = NomadSettlementGenerator.SiteRadiusFor(size);

                    if (!TryChooseSite(config, siteRadius, keepOut, chosen, out Vector3 centre, out float range))
                    {
                        // Not fatal for the same reason a rough site is not: the towns already placed
                        // are good, and the world is simply out of room for this one.
                        Debug.LogWarning($"[NomadSettlementPlacer] No level ground of radius {siteRadius} m " +
                                         $"left for '{name}' ({size}) at {MinimumSeparation} m from every " +
                                         "other settlement. Relax MaxGrade, lower MinimumSeparation, or " +
                                         "place fewer towns.");
                        skipped.Add(name);
                        report.AppendLine($"  {name} ({size}) SKIPPED -- no site left");
                        continue;
                    }

                    chosen.Add(centre);

                    ChunkInfo? chunk = config.GetChunk(config.WorldToChunkCoord(centre));
                    if (chunk == null || string.IsNullOrEmpty(chunk.Value.scenePath))
                    {
                        Debug.LogError($"[NomadSettlementPlacer] {centre} is not inside any chunk scene.");
                        fatal = true;
                        break;
                    }

                    Scene target = SceneManager.GetSceneByPath(chunk.Value.scenePath);
                    if (!target.IsValid() || !target.isLoaded)
                    {
                        Debug.LogError($"[NomadSettlementPlacer] {chunk.Value.sceneName} is not open; the site " +
                                       "search should have opened it.");
                        fatal = true;
                        break;
                    }

                    NomadSettlementGenerator generator =
                        PlaceGenerator(target, prefabs, centre, index, size, name);
                    generator.Generate();

                    if (!Verify(generator, size, out string verdict))
                    {
                        // Skipped, not fatal. The site search hands out the flattest ground first, so
                        // the last towns get the roughest -- and aborting there threw away eight good
                        // settlements and the NavMesh bake with them. The half-built town is removed
                        // (a settlement is whole or it is absent) and its ground is marked so the
                        // search does not simply hand the same rough patch to the next one.
                        Debug.LogWarning($"[NomadSettlementPlacer] '{name}' skipped: {verdict}", generator);

                        UnityEngine.Object.DestroyImmediate(generator.gameObject);
                        EditorSceneManager.MarkSceneDirty(target);
                        EditorSceneManager.SaveScene(target);

                        chosen.RemoveAt(chosen.Count - 1);
                        keepOut.Add(new Bounds(centre, new Vector3(siteRadius * 2f, 10000f, siteRadius * 2f)));

                        skipped.Add(name);
                        report.AppendLine($"  {name} ({size}) SKIPPED -- {verdict}");
                        continue;
                    }

                    EditorSceneManager.MarkSceneDirty(target);
                    if (!EditorSceneManager.SaveScene(target))
                    {
                        Debug.LogError($"[NomadSettlementPlacer] Could not save {chunk.Value.sceneName}.");
                        fatal = true;
                        break;
                    }

                    placed++;
                    report.AppendLine($"  {name} ({size}) in {chunk.Value.sceneName} at {centre}, " +
                                      $"site range {range:F2} m. {verdict}");
                }
            }
            finally
            {
                foreach (Scene scene in opened)
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            report.AppendLine($"  {placed} of {Settlements.Length} settlements placed.");
            if (skipped.Count > 0)
                report.AppendLine($"  skipped: {string.Join(", ", skipped)}");
            Debug.Log(report.ToString());

            // A fault in the tooling stops everything; a site the world had no room for does not.
            if (fatal || placed == 0) return;

            if (!bakeNavMesh)
            {
                Debug.LogWarning("[NomadSettlementPlacer] The world NavMesh is now stale: run " +
                                 "World > Streaming > Bake World NavMesh before playing, or every nomad " +
                                 "stands still and the build check fails.");
                return;
            }

            Debug.Log(WorldNavMeshBaker.Bake(WorldNavMeshBaker.LoadConfig()));
        }

        // ── prefabs ──────────────────────────────────────────────────────────────

        private struct Prefabs
        {
            public GameObject[] Large, Medium, Small, Tents, Nomads;
            public GameObject Mounted;
        }

        private static bool LoadPrefabs(out Prefabs prefabs)
        {
            bool ok = true;
            prefabs = new Prefabs
            {
                Large = LoadFolder(BuildingDir + "/Large", ref ok),
                Medium = LoadFolder(BuildingDir + "/Medium", ref ok),
                Small = LoadFolder(BuildingDir + "/Small", ref ok),
                Tents = FreestandingTents
                    .Select(n => LoadRequired($"{BuildingDir}/Tents/NomadSail_{n}.prefab", ref ok))
                    .ToArray(),
                Nomads = NomadPrefabs.Select(p => LoadRequired(p, ref ok)).ToArray(),
                Mounted = LoadRequired(MountedPrefab, ref ok),
            };

            if (!ok)
            {
                Debug.LogError("[NomadSettlementPlacer] A prefab is missing (see above). Run " +
                               "Tools > Environment > Build Nomad Settlement Prefabs first.");
                return false;
            }

            // How wide the sets actually measure. This is not decoration: the first build measured
            // footprints off Renderer.bounds on prefab ASSETS, got numbers that were not metres, and
            // silently refused most of the buildings for being on ground that was fine. The set runs
            // 1.97-11.30 m (NomadSettlementBuilder's size-class header), so a range far outside that
            // means the measurement is broken again, not that the terrain is rough.
            Debug.Log("[NomadSettlementPlacer] measured footprints (m): " +
                      $"large {Describe(prefabs.Large)}, medium {Describe(prefabs.Medium)}, " +
                      $"small {Describe(prefabs.Small)}, tents {Describe(prefabs.Tents)}.");

            WarnOversized("large", prefabs.Large);
            WarnOversized("medium", prefabs.Medium);
            WarnOversized("small", prefabs.Small);

            return true;
        }

        private static string Describe(GameObject[] set)
        {
            Vector2 range = NomadSettlementGenerator.FootprintRange(set);
            return $"{range.x:F2}-{range.y:F2}";
        }

        /// <summary>
        /// Widest prefab the documented set contains, plus room for a hybrid with annexes. Past this
        /// the prefab is the outlier, not the plan.
        /// </summary>
        private const float ExpectedWidest = 15f;

        /// <summary>
        /// Names the prefabs too wide to fit the ring they are asked to stand on.
        ///
        /// Named rather than counted, because a clearance is half a width: one 44 m building takes a
        /// 23 m bite out of a 26 m core ring and the town silently comes up several buildings short.
        /// The range alone said "something here is big" and cost two builds to pin down.
        /// </summary>
        private static void WarnOversized(string setName, GameObject[] set)
        {
            List<string> oversized = NomadSettlementGenerator.OversizedPrefabs(set, ExpectedWidest);
            if (oversized.Count == 0) return;

            Debug.LogWarning($"[NomadSettlementPlacer] {oversized.Count} {setName} prefab(s) are wider " +
                             $"than {ExpectedWidest} m and will crowd out their ring: " +
                             string.Join(", ", oversized));
        }

        private static GameObject[] LoadFolder(string folder, ref bool ok)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            if (guids.Length == 0)
            {
                Debug.LogError($"[NomadSettlementPlacer] No prefabs under {folder}.");
                ok = false;
                return Array.Empty<GameObject>();
            }

            // Ordered, because FindAssets does not promise one and the seed is meaningless if the
            // array it indexes into is in a different order on the next machine.
            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(p => p != null)
                .ToArray();
        }

        private static GameObject LoadRequired(string path, ref bool ok)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[NomadSettlementPlacer] Missing prefab: {path}");
                ok = false;
            }
            return prefab;
        }

        // ── sites ────────────────────────────────────────────────────────────────

        private static void OpenTerrainChunks(WorldStreamingConfig config, List<Scene> opened)
        {
            foreach (ChunkInfo chunk in config.chunks)
            {
                if (!chunk.hasTerrain || string.IsNullOrEmpty(chunk.scenePath)) continue;

                Scene scene = SceneManager.GetSceneByPath(chunk.scenePath);
                if (scene.IsValid() && scene.isLoaded) continue;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(chunk.scenePath) == null) continue;

                opened.Add(EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive));
            }
        }

        /// <summary>
        /// Wipes every nomad settlement from the open chunk scenes before placing the new ones.
        ///
        /// Not optional, and not the same thing as PlaceGenerator reusing a root by name. A town's
        /// site depends on its radii and on the seed, so any change to either moves it -- possibly
        /// into a different chunk. PlaceGenerator only looks for its root in the scene it is about to
        /// build in, so without this sweep the previous run's copy is left standing in the chunk it
        /// used to occupy, and the world quietly accumulates a derelict town per rebuild.
        /// </summary>
        private static void RemoveExistingSettlements()
        {
            NomadSettlementGenerator[] existing = UnityEngine.Object.FindObjectsByType<NomadSettlementGenerator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (NomadSettlementGenerator generator in existing)
            {
                Scene scene = generator.gameObject.scene;
                UnityEngine.Object.DestroyImmediate(generator.gameObject);
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            }

            if (existing.Length == 0) return;

            // Saved now rather than at the end: a town that moves chunks would otherwise leave its
            // old scene dirty but unsaved, and the finally block closes every scene it opened.
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[NomadSettlementPlacer] Removed {existing.Length} settlement(s) from a previous run.");
        }

        private static List<Bounds> BuildKeepOut(Vector3 spawn)
        {
            var keepOut = new List<Bounds>
            {
                new Bounds(spawn, new Vector3(SpawnKeepOut * 2f, 10000f, SpawnKeepOut * 2f)),
            };

            // The Clanker town, wherever its own site search put it. Nothing to avoid if it has not
            // been built yet.
            foreach (RobotSettlementGenerator robot in UnityEngine.Object.FindObjectsByType<RobotSettlementGenerator>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                keepOut.Add(new Bounds(robot.transform.position,
                                       new Vector3(ClankerKeepOut * 2f, 10000f, ClankerKeepOut * 2f)));
            }

            // Terrain features: a mesa's mesh is not in the scene, so its authored footprint is the
            // only thing that says where it will be once baked.
            foreach (TerrainFeatureSpawner spawner in UnityEngine.Object.FindObjectsByType<TerrainFeatureSpawner>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Bounds local = spawner.Area.ComputeLocalBounds();
                Transform t = spawner.transform;
                Vector3 centre = t.TransformPoint(local.center);
                Vector3 size = Vector3.Scale(local.size, t.lossyScale);
                // A rotated feature's box is taken at its diagonal, so the margin errs on the safe side.
                float diagonal = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 1.42f;
                var world = new Bounds(centre, new Vector3(diagonal, 10000f, diagonal));
                world.Expand(FeatureMargin * 2f);
                keepOut.Add(world);
            }

            return keepOut;
        }

        private static bool TryChooseSite(WorldStreamingConfig config, float siteRadius,
                                          List<Bounds> keepOut, List<Vector3> alreadyChosen,
                                          out Vector3 centre, out float bestRange)
        {
            float? HeightAt(float x, float z) =>
                TerrainProbe.TryGetTerrainHeight(new Vector3(x, 0f, z), out float height)
                    ? height
                    : (float?)null;

            centre = default;
            bestRange = float.MaxValue;
            bool found = false;

            Bounds world = config.chunks[0].worldBounds;
            foreach (ChunkInfo chunk in config.chunks) world.Encapsulate(chunk.worldBounds);

            for (float x = world.min.x; x <= world.max.x; x += CandidateStep)
            for (float z = world.min.z; z <= world.max.z; z += CandidateStep)
            {
                var flat = new Vector2(x, z);

                if (!InsideOneChunk(config, flat, siteRadius)) continue;
                if (TooCloseToChosen(flat, alreadyChosen)) continue;
                if (DiscTouches(keepOut, flat, siteRadius)) continue;

                if (!SettlementSiteScore.TryEvaluate(HeightAt, flat, siteRadius,
                                                     out float range, out float centreHeight)) continue;

                if (range >= bestRange) continue;
                bestRange = range;
                centre = new Vector3(x, centreHeight, z);
                found = true;
            }

            return found;
        }

        /// <summary>
        /// True when the whole site disc lies inside ONE chunk that has terrain. Everything a
        /// settlement makes goes into a single chunk scene, so a town reaching into its neighbour
        /// would have half its buildings stream in and out with a chunk nobody is near.
        /// </summary>
        private static bool InsideOneChunk(WorldStreamingConfig config, Vector2 flat, float siteRadius)
        {
            var centre = new Vector3(flat.x, 0f, flat.y);
            ChunkInfo? owner = config.GetChunk(config.WorldToChunkCoord(centre));
            if (owner == null || !owner.Value.hasTerrain) return false;

            Bounds bounds = owner.Value.worldBounds;
            return flat.x - siteRadius >= bounds.min.x && flat.x + siteRadius <= bounds.max.x
                && flat.y - siteRadius >= bounds.min.z && flat.y + siteRadius <= bounds.max.z;
        }

        private static bool TooCloseToChosen(Vector2 flat, List<Vector3> chosen)
        {
            foreach (Vector3 other in chosen)
            {
                float dx = other.x - flat.x;
                float dz = other.z - flat.y;
                if (dx * dx + dz * dz < MinimumSeparation * MinimumSeparation) return true;
            }
            return false;
        }

        private static bool DiscTouches(List<Bounds> keepOut, Vector2 flat, float siteRadius)
        {
            var disc = new Bounds(new Vector3(flat.x, 0f, flat.y),
                                  new Vector3(siteRadius * 2f, 10000f, siteRadius * 2f));
            foreach (Bounds bounds in keepOut)
                if (bounds.Intersects(disc)) return true;
            return false;
        }

        // ── scene ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The root name is part of the save-id path of every nomad under it: their ids derive from
        /// scene plus hierarchy path. Do not rename it once a world has been saved, or the lot are
        /// orphaned.
        /// </summary>
        private static string RootName(int index, string name) => $"NomadSettlement_{index:00}_{name}";

        private static NomadSettlementGenerator PlaceGenerator(Scene scene, Prefabs prefabs, Vector3 centre,
                                                                int index, NomadSettlementSize size, string name)
        {
            string rootName = RootName(index, name);
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.SetPositionAndRotation(centre, Quaternion.identity);

            if (!root.TryGetComponent(out NomadSettlementGenerator generator))
                generator = root.AddComponent<NomadSettlementGenerator>();

            generator.size = size;
            generator.seed = BaseSeed + index;
            generator.largeBuildings = prefabs.Large;
            generator.mediumBuildings = prefabs.Medium;
            generator.smallBuildings = prefabs.Small;
            generator.tents = prefabs.Tents;
            generator.nomads = prefabs.Nomads;
            generator.mountedNomad = prefabs.Mounted;
            // HerdModule keys herds by a GLOBAL string, so without a per-town prefix every flock in
            // the world would share one herd's movement broadcasts.
            generator.herdPrefix = $"nomad_s{index:00}";

            EditorUtility.SetDirty(generator);
            return generator;
        }

        /// <summary>
        /// Reads the built town back.
        ///
        /// The counts catch a site too rough to build on. The module check catches the real hazard of
        /// wiring frozen components through SerializedObject: a field name that does not exist writes
        /// nothing and reports nothing, so a settlement would look right in the Scene view while every
        /// nomad in it stood still.
        /// </summary>
        /// <summary>
        /// How much of the plan a town has to actually contain. Not 100 %: a seeded layout on real
        /// terrain will lose the odd structure to a dune and that is fine. But the first build shipped
        /// a "settlement" of one tent past a check that only failed at zero, so the bar is most of it.
        /// </summary>
        private const float RequiredStructureFraction = 0.75f;

        private static bool Verify(NomadSettlementGenerator generator, NomadSettlementSize size,
                                    out string report)
        {
            Transform generated = generator.transform.Find(NomadSettlementGenerator.GeneratedRootName);
            if (generated == null)
            {
                report = "Generate() produced no 'Generated' child.";
                return false;
            }

            int structures = 0, people = 0;
            foreach (Transform child in generated)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (source == null) continue;

                string path = AssetDatabase.GetAssetPath(source);
                if (path.StartsWith(BuildingDir, StringComparison.Ordinal)) structures++;
                else if (path.StartsWith(CharacterDir, StringComparison.Ordinal) || path == MountedPrefab) people++;
            }

            int anchored = 0;
            foreach (SpaceGame.Agents.PatrolModule patrol in
                     generated.GetComponentsInChildren<SpaceGame.Agents.PatrolModule>(true))
            {
                var so = new SerializedObject(patrol);
                bool radiusAnchored = so.FindProperty("mode").enumValueIndex == 0
                                   && so.FindProperty("radiusCenter").objectReferenceValue == generator.transform;
                bool onARoute = so.FindProperty("patrolPoints").arraySize > 0;
                if (radiusAnchored || onARoute) anchored++;
            }

            int herded = generated.GetComponentsInChildren<SpaceGame.Agents.HerdModule>(true).Length;

            int expected = NomadSettlementGenerator.ExpectedStructures(size);
            int required = Mathf.CeilToInt(expected * RequiredStructureFraction);

            report = $"{structures}/{expected} structures, {people} nomads ({anchored} anchored, {herded} herded).";

            if (structures < required)
            {
                report = $"only {structures} of {expected} structures were placed, under the {required} " +
                         "required -- the site is too rough, or the footprint measurement is wrong " +
                         "(check the measured footprints logged above against 1.97-11.30 m). " + report;
                return false;
            }

            if (people == 0)
            {
                report = "no nomads were placed. " + report;
                return false;
            }

            if (anchored == 0 && herded == 0)
            {
                report = "no behaviour was wired -- check the SerializedObject field names in " +
                         "NomadSettlementGenerator. " + report;
                return false;
            }

            return true;
        }
    }
}
