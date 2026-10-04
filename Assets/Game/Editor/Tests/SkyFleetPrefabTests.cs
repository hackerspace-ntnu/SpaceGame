// The Sky City fleet's prefabs and its placement, read off disk.
//
//   every builder path names the asset that actually ships, so a rebuild overwrites it in place
//   instead of writing a second copy with a new GUID beside a moved one;
//   every hull is a moving vehicle on the agent stack — networked, kinematic, flown by
//   FlyingRigidbodyMotor through an AgentController — and none of it is static;
//   every hull smokes from each of its ducts, with the shared black cloud material;
//   the flagship is a settlement deck whose parts are wired and whose route survives a save;
//   persistentScene holds the fleet, sailing a route that clears the ground, with escorts on
//   stations that never touch the city or each other.
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class SkyFleetPrefabTests
    {
        private const string NetworkManagerPrefabPath = "Assets/Game/Prefabs/Systems/NetworkManager.prefab";

        // The GUIDs the scene and the nested prefabs reference. A builder path that resolves to any
        // other asset (or none) would orphan every one of those references on the next rebuild.
        [TestCase(SkyFleetBuilder.FleetPrefabPath, "adec8f29c01881849bae72863c3be026")]
        [TestCase(SkyCityBuilder.PrefabPath, "dec7883b7f581e44f8cd0a6af096eb67")]
        [TestCase(SkyCityBuilder.HullsPath, "197befca1cd1e9d49b622b88bc14f6ff")]
        public void BuilderPath_NamesTheShippedAsset(string path, string guid) =>
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets),
                            $"{path} does not resolve to the shipped asset {guid}.");

        private static string[] HullPaths =>
            SkyFleetBuilder.Vessels.Select(v => v.PrefabPath).Append(SkyFleetBuilder.FleetPrefabPath).ToArray();

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"no {path} - run Tools > Environment > Build Sky Fleet Prefabs");
            return prefab;
        }

        [TestCaseSource(nameof(HullPaths))]
        public void Hull_IsAServerFlownVehicleOnTheAgentStack(string path)
        {
            GameObject hull = Load(path);

            Assert.IsNotNull(hull.GetComponent<NetworkObject>());
            Assert.IsNotNull(hull.GetComponent<NetAuthority>(), "clients must not run their own brain");
            var netTransform = hull.GetComponent<NetworkTransform>();
            Assert.IsNotNull(netTransform);
            Assert.AreEqual(NetworkTransform.AuthorityModes.Server, netTransform.AuthorityMode);

            var body = hull.GetComponent<Rigidbody>();
            Assert.IsTrue(body != null && body.isKinematic, "non-convex mesh colliders need a kinematic body");

            var motor = hull.GetComponent<FlyingRigidbodyMotor>();
            Assert.IsTrue(motor != null && motor.KinematicHull);
            var controller = hull.GetComponent<AgentController>();
            Assert.IsNotNull(controller);
            Assert.AreSame(motor, new SerializedObject(controller).FindProperty("MotorComponent").objectReferenceValue);

            Assert.IsTrue(hull.GetComponent<DriftRouteModule>() != null || hull.GetComponent<FleetEscortModule>() != null,
                          "every hull has a brain");
            Assert.IsNotNull(hull.GetComponent<WalkerPlatformCarrier>(), "players on the deck must be carried");
        }

        [TestCaseSource(nameof(HullPaths))]
        public void Hull_HasNoStaticFlags_SoItsMeshesMoveWithIt(string path)
        {
            Transform flagged = Load(path).GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0);
            Assert.IsNull(flagged, $"{flagged?.name} is static; a batched mesh stays where the scene loaded it");
        }

        [TestCaseSource(nameof(HullPaths))]
        public void Hull_SmokesFromEveryDuct_InBlack(string path)
        {
            GameObject hull = Load(path);
            var smoke = hull.GetComponent<EngineSmoke>();
            Assert.IsNotNull(smoke);

            int ducts = path == SkyFleetBuilder.FleetPrefabPath
                ? SkyFleetBuilder.FlagshipDucts.Length
                : SkyFleetBuilder.Vessels.First(v => v.PrefabPath == path).Ducts.Length;
            Assert.AreEqual(ducts, smoke.EngineCount);

            var smokeMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyFleetMovers.SmokeMaterialPath);
            Assert.IsNotNull(smokeMaterial);
            Assert.Less(smokeMaterial.GetColor("_Color").maxColorComponent, 0.1f, "smoke is black");
            foreach (ParticleSystem cloud in hull.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.AreSame(smokeMaterial, cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial);
                Assert.AreEqual(ParticleSystemSimulationSpace.World, cloud.main.simulationSpace,
                                "a moving hull leaves its smoke behind");
            }
        }

        [TestCaseSource(nameof(HullPaths))]
        public void Hull_IsRegisteredForTheNetwork(string path)
        {
            var manager = Load(NetworkManagerPrefabPath).GetComponent<NetworkManager>();
            GameObject hull = Load(path);
            Assert.IsTrue(manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Any(l => l != null && l.Contains(hull)));
        }

        [Test]
        public void Flagship_IsASettlementDeckWithItsPartsWired()
        {
            GameObject fleet = Load(SkyFleetBuilder.FleetPrefabPath);
            var deck = new SerializedObject(fleet.GetComponent<SettlementDeck>());

            Assert.AreSame(fleet.GetComponent<DriftRouteModule>(), deck.FindProperty("route").objectReferenceValue);
            Assert.AreSame(fleet.GetComponent<StaticNavMeshData>(), deck.FindProperty("navMesh").objectReferenceValue);
            Assert.AreSame(fleet.GetComponent<SettlementPopulation>(), deck.FindProperty("population").objectReferenceValue);
            Assert.AreSame(fleet.GetComponent<WorldSiteMarker>(), deck.FindProperty("site").objectReferenceValue);
            var volume = deck.FindProperty("deckVolume").objectReferenceValue as BoxCollider;
            Assert.IsTrue(volume != null && volume.isTrigger);
            Assert.AreEqual(fleet.transform.localScale, Vector3.one, "StaticNavMeshData cannot apply scale");
        }

        [Test]
        public void Flagship_KeepsItsPlaceOnTheRoute_ThroughASave() =>
            PersistenceProbe.For(SkyFleetBuilder.FleetPrefabPath)
                .Mutate(go => go.GetComponent<DriftRouteModule>().RestoreDrift(2, true, 33f))
                .AssertSurvivesRoundTrip();

        // ── The scene ──────────────────────────────────────────────────────────

        private static void WithScene(System.Action<Scene> check)
        {
            Scene scene = SceneManager.GetSceneByPath(SkyFleetPlacement.ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SkyFleetPlacement.ScenePath, OpenSceneMode.Additive);
            try
            {
                check(scene);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void PersistentScene_HoldsTheFleet() => WithScene(scene =>
            Assert.IsTrue(SkyFleetPlacement.Verify(scene, out string report), report));

        [Test]
        public void EveryLeg_ClearsTheGroundUnderTheWholeFleet()
        {
            const float clearance = 25f;
            Vector3[] route = SkyFleetPlacement.Route;
            for (int i = 0; i < route.Length; i++)
            {
                Vector3 from = route[i], to = route[(i + 1) % route.Length];
                float ground = SkyFleetPlacement.HighestGroundAlong(from, to, SkyFleetPlacement.FleetHalfWidth, 40f);
                Assert.IsFalse(float.IsNaN(ground), $"leg {i} has no ground under it at all - is it off the map?");
                Assert.Less(ground, SkyFleetPlacement.CruiseAltitude - SkyFleetPlacement.FleetDraught - clearance,
                            $"leg {i} ({from} -> {to}) passes over ground at {ground:0} m");
            }
        }

        [Test]
        public void EscortStations_NeverTouchTheCityOrEachOther()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var city = (GameObject)PrefabUtility.InstantiatePrefab(Load(SkyCityBuilder.PrefabPath), scene);
                Bounds cityBounds = SkyFleetBuilder.RendererBounds(city);

                var reach = new Bounds[SkyFleetPlacement.Escorts.Length];
                for (int i = 0; i < reach.Length; i++)
                {
                    SkyFleetPlacement.EscortSlot slot = SkyFleetPlacement.Escorts[i];
                    string prefab = SkyFleetBuilder.Vessels.First(v => v.Name == slot.Vessel).PrefabPath;
                    var ship = (GameObject)PrefabUtility.InstantiatePrefab(Load(prefab), scene);
                    Bounds hull = ShipBounds(ship);
                    // Anywhere the wander can take it, at any heading: the hull's radius round the station.
                    float radius = new Vector2(hull.extents.x, hull.extents.z).magnitude;
                    reach[i] = new Bounds(slot.Station, new Vector3(radius, hull.extents.y, radius) * 2f);
                    var wander = new SerializedObject(ship.GetComponent<FleetEscortModule>());
                    reach[i].Expand(2f * wander.FindProperty("wanderAmplitude").vector3Value);
                }

                for (int i = 0; i < reach.Length; i++)
                {
                    Assert.IsFalse(reach[i].Intersects(cityBounds), $"escort {i} can drift into the city");
                    for (int j = i + 1; j < reach.Length; j++)
                        Assert.IsFalse(reach[i].Intersects(reach[j]), $"escorts {i} and {j} can drift into each other");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // The ship as drawn: its meshes, not its particle systems.
        private static Bounds ShipBounds(GameObject ship)
        {
            MeshRenderer[] renderers = ship.GetComponentsInChildren<MeshRenderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (MeshRenderer r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
