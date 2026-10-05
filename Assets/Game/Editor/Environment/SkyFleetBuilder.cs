// Builds the Sky Tribe's fleet: the flagship (SkyCityFleet.prefab, the Sky City under way) and the
// escort vessels that drift round it.
//
// The vessels come out of Blender via
// Assets/Game/Art/Models/_Source~/models/vehicles/sky_fleet_export.py, one FBX
// each, already shrunk to scale and centred on their origin. Each becomes its own
// prefab - they are their own ships - and SkyCityFleet.prefab nests the Sky City
// prefab. Build the city first (Tools > Environment > Build Sky City Prefab); this
// reuses it, not a copy.
//
// Re-run from: Tools > Environment > Build Sky Fleet Prefabs. Where the fleet flies -- its route,
// the escorts' stations, how many escorts -- is the scene's, not the prefabs': see SkyFleetPlacement,
// which the build runs last so the scene's overrides follow the rebuilt prefabs.
//
// Every hull MOVES, through the agent stack (SkyFleetMovers): an AgentController drives
// FlyingRigidbodyMotor in kinematic-hull mode, the flagship's brain is DriftRouteModule and an
// escort's is FleetEscortModule. So nothing here is marked static: a batching-static mesh is
// combined where it stood when the scene loaded and stays there while its hull flies off.
//
// The escorts used to be nested in the fleet prefab. They are separate scene objects now: a hull
// nested under another hull's moving transform cannot steer on its own, and a NetworkObject nested
// under another NetworkObject is not something netcode supports for a scene object.
//
// Collision is by renderer rule, like the buildings: nobody walks these ships
// yet, so they need to be solid to fly into and land on, not to walk round. Gas
// bags and hulls are convex; the cage, the engines' ducts and the gondola decks
// are mesh colliders, because a hull fills what they are open around.
//
// Smoke -- one DustCloudRecipe cloud per engine duct, black, thrown astern, driven by EngineSmoke.
// The ducts are measured from each model's SternGear assembly (two ducted fans and a rudder per
// assembly) and checked against it on every build.
//
// NavMesh -- the build ends by re-baking the flagship's NavMesh, which also puts
// StaticNavMeshData back on the fleet root. See SkyCityNavMeshBaker.
//
// Settlement -- and then re-wires the root as the Sky Tribe's home (site marker, alarm,
// population), which the from-scratch save also drops (SkyCitySettlementWiring), and hands
// SettlementDeck the parts it coordinates.
//
// Multiplayer / persistence -- every hull is a scene-placed NetworkObject the server flies; each
// carries the savers SaveablePolicy gives it (pose, motor, the flagship's route). Both prefabs are
// registered network prefabs, as every root-NetworkObject prefab is.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.World;
using Duct = SpaceGame.EditorTools.SkyFleetMovers.Duct;
using Fit = SpaceGame.EditorTools.StaticPropBuilder.Fit;
using NamedFit = SpaceGame.EditorTools.StaticPropBuilder.NamedFit;

namespace SpaceGame.EditorTools
{
    public static class SkyFleetBuilder
    {
        public const string FleetPrefabPath = PrefabFolder + "/SkyCityFleet.prefab";
        private const string FbxFolder = "Assets/Game/Art/Models/Environment/Structures/SkyFleet";
        private const string PrefabFolder = "Assets/Game/Prefabs/Environment/Structures/SkyFleet";
        private const string FleetRootName = "SkyCityFleet";

        /// <summary>Every engine assembly in the fleet's models: two ducted fans and a rudder.</summary>
        public const string EngineMeshPrefix = "Mesh_SkyCity_SternGear";

        // One cull level, as on the city.
        internal const float LodCullRatio = 0.02f;

        public struct Vessel
        {
            public string Name;
            public string Fbx;
            /// <summary>The engine ducts, in the model's own metres (measured from its SternGear).</summary>
            public Duct[] Ducts;

            public string PrefabPath => $"{PrefabFolder}/{Name}.prefab";
            public string FbxPath => $"{FbxFolder}/{Fbx}.fbx";
        }

        // An escort's fans are 3.2 m across and 1.2 m deep.
        private const float EscortDuctDepth = 1.19f;

        public static readonly Vessel[] Vessels =
        {
            new Vessel { Name = "SkyFreighter", Fbx = "sky_freighter", Ducts = new[]
            {
                new Duct { Centre = new Vector3(-4.73f, -5.66f, 12.02f), Depth = EscortDuctDepth },
                new Duct { Centre = new Vector3(3.85f, -5.66f, 12.02f), Depth = EscortDuctDepth },
            } },
            new Vessel { Name = "SkySkiff", Fbx = "sky_skiff", Ducts = new[]
            {
                new Duct { Centre = new Vector3(5.00f, -3.50f, -4.89f), Depth = EscortDuctDepth },
                new Duct { Centre = new Vector3(-3.58f, -3.50f, -4.89f), Depth = EscortDuctDepth },
            } },
            new Vessel { Name = "SkyTug", Fbx = "sky_tug", Ducts = new[]
            {
                new Duct { Centre = new Vector3(3.76f, -2.18f, -9.66f), Depth = EscortDuctDepth },
                new Duct { Centre = new Vector3(-4.82f, -2.18f, -9.66f), Depth = EscortDuctDepth },
                new Duct { Centre = new Vector3(3.76f, 2.19f, -9.66f), Depth = EscortDuctDepth },
                new Duct { Centre = new Vector3(-4.82f, 2.19f, -9.66f), Depth = EscortDuctDepth },
            } },
        };

        /// <summary>The flagship's two ducted fans at the stern piers, in the Sky City model's own (unscaled) metres.</summary>
        public static readonly Duct[] FlagshipDucts =
        {
            new Duct { Centre = new Vector3(13.72f, 7.88f, -60.67f), Depth = 3.79f },
            new Duct { Centre = new Vector3(-13.72f, 7.88f, -60.67f), Depth = 3.79f },
        };

        // The flagship's fans are ~15 m across as drawn, an escort's 3.2 m.
        private const float FlagshipSmokeScale = 4f;

        // A slow drift (spec 2026-10-04): the city's own length in ~100 s, ~40 s to get going, and
        // braking that stops it well inside DriftRouteModule.arriveRadius (2² / (2 × 0.12) ≈ 17 m).
        public static readonly SkyFleetMovers.Flight FlagshipFlight = new()
        {
            MaxSpeed = 2f, Acceleration = 0.05f, Deceleration = 0.12f, FaceRotateSpeed = 0.05f,
        };

        // Three times the flagship's pace, so an escort that falls off station (a turn, a load)
        // catches up within a minute or two; turns gently enough to read as a ship, not a drone.
        public static readonly SkyFleetMovers.Flight EscortFlight = new()
        {
            MaxSpeed = 6f, Acceleration = 0.6f, Deceleration = 0.6f, FaceRotateSpeed = 0.3f,
        };

        // An escort mostly ambles about its station; the smoke peaks at half its top speed.
        private const float EscortSmokeFullSpeed = 3f;

        internal static readonly NamedFit[] Rules =
        {
            new NamedFit { Match = "Mesh_SkyCity_Bag", Fit = Fit.Convex, Note = "gas envelope" },
            new NamedFit { Match = "Mesh_SkyCity_Cage", Fit = Fit.Mesh, Note = "ring frames round the bags" },
            new NamedFit { Match = EngineMeshPrefix, Fit = Fit.Mesh, Note = "open engine ducts and pylons" },
            new NamedFit { Match = "Mesh_SkyCity_Under", Fit = Fit.Mesh, Note = "gondola deck and its rails" },
            new NamedFit { Match = "Mesh_SkyCity_Home", Fit = Fit.Convex, Note = "cabin capsule" },
            new NamedFit { Match = "Mesh_SkyCity_Prow", Fit = Fit.Convex, Note = "bow block" },
            new NamedFit { Match = "Mesh_SkyCity_Beacon", Fit = Fit.Convex, Note = "lantern tower" },
            new NamedFit { Match = "Cube", Fit = Fit.Box, Note = "the freighter's hull beam" },
            new NamedFit { Match = "Mesh_SkyCity_Outriggers", Fit = Fit.None, Note = "booms and rigging" },
            new NamedFit { Match = "Mesh_SkyCity_Sail", Fit = Fit.None, Note = "sailcloth" },
            new NamedFit { Match = "Mesh_SkyCity_Flag", Fit = Fit.None, Note = "pennants and mast rigs" },
        };

        [MenuItem("Tools/Environment/Build Sky Fleet Prefabs")]
        public static void Build()
        {
            var city = AssetDatabase.LoadAssetAtPath<GameObject>(SkyCityBuilder.PrefabPath);
            if (city == null)
            {
                Debug.LogError($"No {SkyCityBuilder.PrefabPath}. Build the Sky City prefab first.");
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("SkyFleetBuilder");
            StaticPropBuilder.EnsureFolder(PrefabFolder);

            // A preview scene, not the open one: these roots carry NetworkObjects, and a builder
            // working in an open, build-listed scene is the shared editor's scene to dirty.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (Vessel vessel in Vessels)
                    report.AppendLine($"  {vessel.Name}: {BuildVessel(vessel, scene)}");
                report.AppendLine($"  {FleetRootName}: {BuildFlagship(city, scene)}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            AssetDatabase.SaveAssets();
            // The fleet was saved from scratch, without its NavMesh component, over geometry the
            // old bake may no longer match.
            report.AppendLine($"  {SkyCityNavMeshBaker.Bake()}");
            report.AppendLine($"  {SkyCitySettlementWiring.Wire()}");
            report.AppendLine($"  {WireDeck()}");

            string[] paths = Vessels.Select(v => v.PrefabPath).Append(FleetPrefabPath).ToArray();
            report.AppendLine($"  {NetworkPrefabRegistrar.Sync(out _, out _)}");
            // A NetworkObject created by script ships GlobalObjectIdHash 0 until OnValidate runs
            // against the saved asset; reserialize so the real hash reaches the YAML.
            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.SaveAssets();
            // The rebuilt flagship's components have new file ids, so the scene's overrides on them
            // (route, baked save identity) are orphaned until placement writes them again.
            report.AppendLine($"  {SkyFleetPlacement.Place()}");
            Debug.Log(report.ToString());
        }

        private static string BuildVessel(Vessel vessel, UnityEngine.SceneManagement.Scene scene)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(vessel.FbxPath) == null)
                throw new System.InvalidOperationException($"No FBX at {vessel.FbxPath}. Run sky_fleet_export.py first.");

            StaticPropBuilder.ConfigureImporter(vessel.FbxPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(vessel.FbxPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            root.name = vessel.Name;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            StaticPropBuilder.FitCounts fits = StaticPropBuilder.ApplyFits(root, Rules);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);
            Vector3 size = RendererBounds(root).size;

            RequireDuctsOnEngines(vessel.Name, root.transform, vessel.Ducts);
            SkyFleetMovers.AddMover(root, EscortFlight);
            root.AddComponent<FleetEscortModule>();
            EngineSmoke smoke = SkyFleetMovers.AddSmoke(root, root.transform, vessel.Ducts, 1f, EscortSmokeFullSpeed);
            SaveablePolicy.Ensure(root, out _);

            PrefabUtility.SaveAsPrefabAsset(root, vessel.PrefabPath);
            return $"{renderers.Length} renderers, {size.x:F1} x {size.y:F1} x {size.z:F1} m, colliders {fits}, " +
                   $"{smoke.EngineCount} smoking ducts";
        }

        private static string BuildFlagship(GameObject city, UnityEngine.SceneManagement.Scene scene)
        {
            var fleet = new GameObject(FleetRootName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fleet, scene);
            var flagship = (GameObject)PrefabUtility.InstantiatePrefab(city, fleet.transform);

            RequireDuctsOnEngines(FleetRootName, flagship.transform, FlagshipDucts);
            SkyFleetMovers.AddMover(fleet, FlagshipFlight);
            fleet.AddComponent<DriftRouteModule>();
            // Its parts (NavMesh, population, site) are put on by the bake and the wiring; WireDeck
            // hands them over once they exist.
            fleet.AddComponent<SettlementDeck>();
            EngineSmoke smoke = SkyFleetMovers.AddSmoke(fleet, flagship.transform, FlagshipDucts,
                                                        FlagshipSmokeScale, FlagshipFlight.MaxSpeed);

            PrefabUtility.SaveAsPrefabAsset(fleet, FleetPrefabPath);
            return $"saved {FleetPrefabPath}, {smoke.EngineCount} smoking ducts";
        }

        private static void RequireDuctsOnEngines(string vessel, Transform model, IReadOnlyList<Duct> ducts)
        {
            List<int> off = SkyFleetMovers.DuctsOffTheEngines(model, ducts, EngineMeshPrefix);
            if (off.Count > 0)
                throw new System.InvalidOperationException(
                    $"{vessel}: duct(s) {string.Join(", ", off)} are not inside any {EngineMeshPrefix} mesh. " +
                    "The model changed; re-measure the ducts.");
        }

        /// <summary>
        /// Hands the flagship's SettlementDeck its parts, and gives the root the savers its components
        /// call for. Last, because the bake and the settlement wiring each re-save the prefab.
        /// </summary>
        private static string WireDeck()
        {
            GameObject fleet = PrefabUtility.LoadPrefabContents(FleetPrefabPath);
            try
            {
                var volume = fleet.transform.Find(SkyFleetMovers.DeckVolumeName).GetComponent<BoxCollider>();
                fleet.GetComponent<SettlementDeck>().Configure(
                    fleet.GetComponent<DriftRouteModule>(), fleet.GetComponent<StaticNavMeshData>(),
                    fleet.GetComponent<SettlementPopulation>(), fleet.GetComponent<WorldSiteMarker>(), volume);
                SaveablePolicy.Ensure(fleet, out string savers);
                PrefabUtility.SaveAsPrefabAsset(fleet, FleetPrefabPath);
                return $"[SkyFleetBuilder] deck wired; savers added: {(savers.Length > 0 ? savers : "none")}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fleet);
            }
        }

        /// <summary>World bounds of everything drawn under <paramref name="go"/>; it must be in a scene.</summary>
        public static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
