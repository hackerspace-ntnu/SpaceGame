// Puts Appa back into the world: wild herds wandering the desert, and Sand herders riding Appas
// and driving their own herd. Design: docs/superpowers/specs/2026-10-04-appa-herds-design.md.
//
// Appa.prefab has no builder any more (AppaBuilder was retired on main); the prefab is the source,
// and this script only ADDS to it what a herd needs, then derives two variants and writes the world
// sim's templates. Idempotent: re-run it any time — it overwrites what it owns and nothing else.
//
//   Appa.prefab           + FormationModule (inert until NpcWorldSim names the band), NpcTaskModule,
//                           GoalTravelModule, and a short wander radius so a resting herd grazes in
//                           place instead of scattering 70 m and being reeled back.
//   NomadAppa.prefab      Appa variant, born saddled, a Sand nomad in the saddle (NpcPassenger) and a
//                           HerdingModule. The mount stays Fauna: a mount carries, the rider fights.
//   NomadHerdAppa.prefab  Appa variant serializing the Sand faction: the herders' livestock. Stamp
//                           enlists a riderless member into its group's tribe on the server, and
//                           EntityFaction is not replicated, so the prefab must already say it.
//
// Re-run from: Tools > Creatures > Author Appa Herds
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class AppaHerdAuthoring
    {
        public const string AppaPath = "Assets/Game/Prefabs/agents/creatures/Appa.prefab";
        public const string HerderPath = "Assets/Game/Prefabs/Agents/Caravan/NomadAppa.prefab";
        public const string LivestockPath = "Assets/Game/Prefabs/Agents/Caravan/NomadHerdAppa.prefab";
        public const string RiderPath = "Assets/Game/Prefabs/Agents/Characters/Nomad_StrawHat.prefab";
        public const string SandFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SandTribeFaction.asset";

        /// <summary>The template the old, origin-stuck wild Appa was seeded from. Removed: see the spec.</summary>
        public const string RetiredTemplateId = "wild-appa";
        public const string HerdersTemplateId = "sand-appa-herders";

        /// <summary>One wild herd: where it starts (on the world NavMesh, sampled 2026-10-04) and how many follow its lead.</summary>
        public readonly struct WildHerd
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly Vector3 Start;
            public readonly int Followers;

            public WildHerd(string id, string displayName, Vector3 start, int followers)
            {
                Id = id;
                DisplayName = displayName;
                Start = start;
                Followers = followers;
            }
        }

        public static readonly WildHerd[] WildHerds =
        {
            new("appa-herd-dunes", "Appa Herd (dunes)", new Vector3(3150f, 113.7f, 1350f), 4),
            new("appa-herd-plateau", "Appa Herd (plateau)", new Vector3(2200f, 170.3f, 1500f), 5),
            new("appa-herd-south", "Appa Herd (south)", new Vector3(2800f, 104.9f, -300f), 4),
        };

        public static readonly Vector3 HerdersStart = new(3400f, 104.7f, 900f);
        public const int HerderDrovers = 2;
        public const int HerdedLivestock = 6;

        /// <summary>A grazing animal's pace folded: slower than the live walk, which stops to graze.</summary>
        public const float FoldedTravelSpeed = 1.4f;

        /// <summary>A herd is a mob, not a file: three lanes, wide gaps for a 1.9 m-radius animal, loose.</summary>
        public static readonly FormationShape HerdShape = new()
        {
            Lanes = 3,
            RowSpacing = 9f,
            LaneSpacing = 8f,
            LateralJitter = 3f,
            LongitudinalJitter = 3f,
            DriftAmplitude = 1.5f,
            DriftRate = 0.05f,
        };

        /// <summary>The rest ring a stopped herd scatters across, and how far a straggler may get before it runs back.</summary>
        private const float RestRadius = 14f;
        private const float RegroupDistance = 60f;
        private const float SlotTolerance = 3f;

        /// <summary>A resting Appa ambles this far at a time rather than its 70 m free roam.</summary>
        private const float GrazeWanderRadius = 10f;

        /// <summary>Where the saddle seat is in Appa's root space: MountModule.seatOffset, the player's seat.</summary>
        private static readonly Vector3 SeatPosition = new(0f, 2.33f, -0.18f);
        private const float DismountSideOffset = 4.5f;
        private const float DismountSampleDistance = 8f;

        private const string GrazingFlag = "IsGrazing";

        [MenuItem("Tools/Creatures/Author Appa Herds")]
        public static void Author()
        {
            if (!AddHerdModules()) return;
            if (!BuildHerder() || !BuildLivestock()) return;

            NetworkPrefabRegistrar.Sync(out int added, out int total);
            Debug.Log($"[AppaHerdAuthoring] Network prefabs: {added} added, {total} registered.");
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[AppaHerdAuthoring] Wire Saveable Prefabs failed; run it by hand or the herds have no prefabId.");

            WireWorldSim();
            Verify();
        }

        // ── Prefabs ──────────────────────────────────────────────────────────────

        /// <summary>What every Appa needs to travel as one of a group. Inert on a lone Appa: no band id, no tasks, no goal.</summary>
        private static bool AddHerdModules()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AppaPath);
            try
            {
                var formation = Ensure<FormationModule>(root);
                SerializedFields.Edit(formation, so =>
                {
                    SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                    SerializedFields.SetString(so, "formationId", string.Empty);
                    SerializedFields.SetBool(so, "isLeader", false);
                    SerializedFields.SetFloat(so, "restRadius", RestRadius);
                    SerializedFields.SetFloat(so, "regroupDistance", RegroupDistance);
                    SerializedFields.SetFloat(so, "slotTolerance", SlotTolerance);
                    RosterAuthoring.WriteShape(so.FindProperty("shape"), HerdShape);
                });

                var tasks = Ensure<NpcTaskModule>(root);
                SerializedFields.Edit(tasks, so =>
                {
                    SerializedFields.SetInt(so, "priority", ModulePriority.Fallback);
                    SerializedFields.Set(so, "animatorDriver", root.GetComponent<AgentAnimatorDriver>());
                });

                // Their savers here rather than left to Wire Saveable Prefabs: that pass reaches the
                // variants first (Agents/Caravan sorts before agents/creatures) and gave each its own
                // copy on top of the one the base then inherited.
                Ensure<NpcTaskSaveable>(root);
                Ensure<FormationSaveable>(root);

                var travel = Ensure<GoalTravelModule>(root);
                SerializedFields.Edit(travel, so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1));

                SerializedFields.Edit(root.GetComponent<WanderModule>(), so =>
                {
                    SerializedFields.SetBool(so, "limitWanderRadius", true);
                    SerializedFields.SetFloat(so, "wanderRadius", GrazeWanderRadius);
                });

                DistanceDormancyWiring.Ensure(root);
                PrefabUtility.SaveAsPrefabAsset(root, AppaPath, out bool saved);
                if (!saved) Debug.LogError($"[AppaHerdAuthoring] Could not save {AppaPath}.");
                return saved;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>A saddled Appa with a Sand nomad aboard who herds the livestock of its band.</summary>
        private static bool BuildHerder()
        {
            var rider = AssetDatabase.LoadAssetAtPath<GameObject>(RiderPath);
            if (rider == null)
            {
                Debug.LogError($"[AppaHerdAuthoring] No rider at {RiderPath}.");
                return false;
            }

            return SaveVariant(HerderPath, root =>
            {
                SerializedFields.Edit(root.GetComponent<SaddleSocket>(), so => SerializedFields.SetBool(so, "startSaddled", true));

                var seat = new GameObject("SeatPoint");
                seat.transform.SetParent(root.transform, false);
                seat.transform.localPosition = SeatPosition;

                var passenger = root.AddComponent<NpcPassenger>();
                SerializedFields.Edit(passenger, so =>
                {
                    SerializedFields.Set(so, "riderPrefab", rider);
                    SerializedFields.Set(so, "seatPoint", seat.transform);
                    SerializedFields.SetVector3(so, "seatOffset", Vector3.zero);
                    SerializedFields.SetBool(so, "spawnOnStart", true);
                    SerializedFields.SetFloat(so, "dismountSideOffset", DismountSideOffset);
                    SerializedFields.SetFloat(so, "dismountSampleDistance", DismountSampleDistance);
                });

                root.AddComponent<CrewSaveable>();

                var pose = root.AddComponent<MountedRiderPose>();
                SerializedFields.Edit(pose, so => SerializedFields.Set(so, "mountModule", root.GetComponent<MountModule>()));

                var herding = root.AddComponent<HerdingModule>();
                SerializedFields.Edit(herding, so => SerializedFields.SetInt(so, "priority", ModulePriority.Social + 1));
            });
        }

        /// <summary>The herders' animals: an Appa that belongs to the Sand people.</summary>
        private static bool BuildLivestock()
        {
            var sand = AssetDatabase.LoadAssetAtPath<FactionDefinition>(SandFactionPath);
            if (sand == null)
            {
                Debug.LogError($"[AppaHerdAuthoring] No faction at {SandFactionPath}.");
                return false;
            }

            return SaveVariant(LivestockPath, root =>
                SerializedFields.Edit(root.GetComponent<EntityFaction>(), so => SerializedFields.Set(so, "faction", sand)));
        }

        /// <summary>
        /// A fresh variant of Appa written over <paramref name="path"/>. Rebuilt from nothing each run, so a
        /// hand edit does not survive — but over an existing path the asset keeps its GUID, which is what the
        /// network list, the save files and the world sim's templates refer to it by.
        /// </summary>
        private static bool SaveVariant(string path, System.Action<GameObject> configure)
        {
            var appa = AssetDatabase.LoadAssetAtPath<GameObject>(AppaPath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(appa, preview);
                root.name = System.IO.Path.GetFileNameWithoutExtension(path);
                configure(root);

                DistanceDormancyWiring.Ensure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) Debug.LogError($"[AppaHerdAuthoring] Could not save {path}.");
                return saved;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static T Ensure<T>(GameObject root) where T : Component =>
            root.TryGetComponent(out T existing) ? existing : root.AddComponent<T>();

        // ── World sim ────────────────────────────────────────────────────────────

        /// <summary>The three wild herds and the herders, replacing the retired origin-bound wild Appa.</summary>
        [MenuItem("Tools/Creatures/Wire Appa Herd Templates")]
        public static void WireWorldSim()
        {
            var appa = AssetDatabase.LoadAssetAtPath<GameObject>(AppaPath);
            var herder = AssetDatabase.LoadAssetAtPath<GameObject>(HerderPath);
            var livestock = AssetDatabase.LoadAssetAtPath<GameObject>(LivestockPath);
            var sand = AssetDatabase.LoadAssetAtPath<FactionDefinition>(SandFactionPath);
            if (appa == null || herder == null || livestock == null || sand == null)
            {
                Debug.LogError("[AppaHerdAuthoring] Build the Appa prefabs first (Author Appa Herds).");
                return;
            }

            var owned = new HashSet<string>(WildHerds.Select(h => h.Id)) { RetiredTemplateId, HerdersTemplateId };

            RosterAuthoring.WithWorldSim(sim =>
            {
                var field = typeof(NpcWorldSim).GetField("templates",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var templates = ((NpcGroupTemplate[])field.GetValue(sim) ?? new NpcGroupTemplate[0])
                    .Where(t => t == null || !owned.Contains(t.id))
                    .ToList();

                foreach (WildHerd herd in WildHerds)
                    templates.Add(Template(herd.Id, herd.DisplayName, null, herd.Start, WildTasks(),
                        Member(appa, 1, leader: true), Member(appa, herd.Followers, leader: false)));

                // The livestock before the drovers, so the drovers' unused column slots are the tail's.
                templates.Add(Template(HerdersTemplateId, "Sand Appa Herders", sand, HerdersStart, HerderTasks(),
                    Member(herder, 1, leader: true), Member(livestock, HerdedLivestock, leader: false),
                    Member(herder, HerderDrovers, leader: false)));

                field.SetValue(sim, templates.ToArray());
                EditorUtility.SetDirty(sim);
            });
        }

        private static NpcGroupTemplate Template(string id, string displayName, FactionDefinition tribe, Vector3 start,
                                                 NpcTask[] tasks, params NpcGroupMemberSpec[] members) => new()
        {
            id = id,
            displayName = displayName,
            tribe = tribe,
            runtimeOnly = false,
            members = members,
            tasks = tasks,
            travelSpeed = FoldedTravelSpeed,
            startNearSite = SiteKind.AnimalGround,
            useStartPosition = true,
            startPosition = start,
            bountyHunters = false,
            transport = new NpcGroupTransport(),
            formation = HerdShape,
        };

        private static NpcGroupMemberSpec Member(GameObject prefab, int count, bool leader) => new()
        {
            prefab = prefab,
            role = RosterRole.Scout,
            isLeader = leader,
            crew = false,
            count = count,
        };

        /// <summary>Graze (head down for the whole stay), drink, and drift on. No sites exist yet, so each falls back to open ground at range.</summary>
        private static NpcTask[] WildTasks() => new[]
        {
            Task("grazing the open ground", SiteKind.AnimalGround, 1800f, new Vector2(120f, 300f), 25f, 2f, GrazingFlag),
            Task("drinking", SiteKind.WaterHole, 1500f, new Vector2(60f, 150f), 18f, 1f, string.Empty),
            Task("drifting across country", SiteKind.Landmark, 2200f, new Vector2(40f, 120f), 30f, 0.6f, string.Empty),
        };

        /// <summary>The herders move their animals between grazing and water, a drive at a time.</summary>
        private static NpcTask[] HerderTasks() => new[]
        {
            Task("driving the herd to new grazing", SiteKind.AnimalGround, 1600f, new Vector2(150f, 320f), 30f, 2f, GrazingFlag,
                 "Keep them moving. The grass is thin here.", "Watch the young one, it wanders."),
            Task("watering the herd", SiteKind.WaterHole, 1400f, new Vector2(80f, 160f), 25f, 1f, string.Empty,
                 "They smell water before we do.", "Let them drink. Long walk after."),
        };

        private static NpcTask Task(string label, SiteKind site, float searchRadius, Vector2 dwell, float arriveRadius,
                                    float weight, string dwellFlag, params string[] chatter) => new()
        {
            label = label,
            targetSite = site,
            searchRadius = searchRadius,
            searchFromHome = false,
            dwellSeconds = dwell,
            yields = null,
            yieldChance = 1f,
            chatter = chatter,
            dwellFlag = dwellFlag,
            weight = weight,
            arriveRadius = arriveRadius,
            travelSpeedMultiplier = 1f,
        };

        // ── Verify ───────────────────────────────────────────────────────────────

        /// <summary>Read every write back off disk (INVARIANTS: assume nothing throws).</summary>
        private static void Verify()
        {
            var problems = new List<string>();
            var appa = AssetDatabase.LoadAssetAtPath<GameObject>(AppaPath);
            var herder = AssetDatabase.LoadAssetAtPath<GameObject>(HerderPath);
            var livestock = AssetDatabase.LoadAssetAtPath<GameObject>(LivestockPath);

            if (appa == null || appa.GetComponent<FormationModule>() == null || appa.GetComponent<NpcTaskModule>() == null ||
                appa.GetComponent<GoalTravelModule>() == null)
                problems.Add("Appa lacks its herd modules");
            if (herder == null || herder.GetComponent<NpcPassenger>() == null || herder.GetComponent<HerdingModule>() == null)
                problems.Add("NomadAppa lacks its rider or its HerdingModule");
            if (livestock == null || AssetDatabase.GetAssetPath(livestock.GetComponent<EntityFaction>().Faction) != SandFactionPath)
                problems.Add("NomadHerdAppa is not Sand");

            foreach (string problem in problems) Debug.LogError($"[AppaHerdAuthoring] {problem}.");
            if (problems.Count == 0) Debug.Log("[AppaHerdAuthoring] verify: Appa, NomadAppa and NomadHerdAppa are as authored.");
        }
    }
}
