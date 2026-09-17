// The Sky City as a settlement, read off the fleet prefab the scene places:
//
//   the wiring never ran, or a fleet rebuild dropped what it put on the root;
//   the town belongs to the wrong tribe, or is peopled by the wrong prefabs;
//   the town waits a whole interval before anyone lives there;
//   a reload counts the town before its saved people are back, and spawns a second one;
//   the people wander toward roofs they cannot reach and stand at the railings;
//   the spawn anchor is not on the promenades, so the reachability check rejects every point or,
//   worse, accepts the roofs.
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public class SkyCitySettlementTests
    {
        // Where persistentScene places the fleet.
        private static readonly Vector3 ScenePosition = new(3970.32f, 228f, 1025f);

        // The far end of the port promenade, in the city's modelled metres (sky_city_BUILD.md).
        private static readonly Vector3 PortLaneFarEnd = new(-5.6f, 0f, -40f);
        private const float SampleRadius = 2f;

        private GameObject fleet;

        [SetUp]
        public void SetUp()
        {
            fleet = AssetDatabase.LoadAssetAtPath<GameObject>(SkyFleetBuilder.FleetPrefabPath);
            Assert.IsNotNull(fleet, SkyFleetBuilder.FleetPrefabPath);
        }

        [Test]
        public void TheWiringPostConditionHolds()
        {
            Assert.IsTrue(SkyCitySettlementWiring.Verify(fleet, out string report), report);
        }

        [Test]
        public void TheCityIsTheSkyTribesHome()
        {
            var sky = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);

            var marker = fleet.GetComponent<WorldSiteMarker>();
            Assert.IsNotNull(marker, "WorldSiteMarker");
            Assert.AreEqual(SiteKind.Home, marker.Kind);
            Assert.AreEqual(WorldSite.SkyCityName, marker.SiteName);

            var alarm = fleet.GetComponent<SettlementAlarm>();
            Assert.IsNotNull(alarm, "SettlementAlarm");
            Assert.AreSame(sky, new SerializedObject(alarm).FindProperty("owner").objectReferenceValue);

            var population = fleet.GetComponent<SettlementPopulation>();
            Assert.IsNotNull(population, "SettlementPopulation");
            var so = new SerializedObject(population);
            Assert.AreSame(sky, so.FindProperty("owner").objectReferenceValue);
            Assert.AreEqual(16, so.FindProperty("maxPopulation").intValue);
            Assert.AreEqual(45f, so.FindProperty("spawnInterval").floatValue);
            Assert.Greater(so.FindProperty("initialWaves").intValue, 0, "the city fills on arrival");
            Assert.IsTrue(so.FindProperty("keepGroundChunksLoaded").boolValue,
                          "the city is in persistentScene and its people in the chunks under it");

            SerializedProperty inhabitants = so.FindProperty("inhabitants");
            Assert.AreEqual(NomadPrefabBuilder.SkyTribePeople.Length, inhabitants.arraySize);
            for (int i = 0; i < inhabitants.arraySize; i++)
            {
                SerializedProperty entry = inhabitants.GetArrayElementAtIndex(i);
                Object prefab = entry.FindPropertyRelative("prefab").objectReferenceValue;
                Assert.AreEqual(NomadPrefabBuilder.SkyTribePeople[i].PrefabPath, AssetDatabase.GetAssetPath(prefab));
                Assert.Greater(entry.FindPropertyRelative("weight").intValue, 0);
            }
        }

        [Test]
        public void TheCitysPeopleOnlyWanderWhereTheyCanWalk()
        {
            foreach (NomadPrefabBuilder.NomadRecipe recipe in NomadPrefabBuilder.SkyTribePeople)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                Assert.IsNotNull(prefab, recipe.PrefabPath);
                var wander = prefab.GetComponent<WanderModule>();
                Assert.IsNotNull(wander, $"{recipe.Name} WanderModule");
                Assert.IsTrue(new SerializedObject(wander).FindProperty("onlyReachableDestinations").boolValue,
                              $"{recipe.Name} would wander toward the roofs - rebuild the sky nomads");
            }
        }

        [Test]
        public void TheCityIsAirborneSoGroundErrandsSkipItButNameLookupFindsIt()
        {
            var marker = fleet.GetComponent<WorldSiteMarker>();
            Assert.IsNotNull(marker, "WorldSiteMarker");
            Assert.IsTrue(marker.Airborne,
                "the Sky City is 228 m up - a ground Home search that can pick it would walk NPCs underneath it");

            // WorldSiteMarker.OnEnable is what would normally publish this, but the prefab asset is
            // never enabled in a scene here - register the same record it would, to prove the
            // registry-level behaviour the wiring depends on.
            WorldSiteRegistry.Clear();
            try
            {
                WorldSiteRegistry.Register(marker.Kind, ScenePosition, 100f, marker.SiteName, airborne: marker.Airborne);

                Assert.IsFalse(WorldSiteRegistry.TryFindNearest(SiteKind.Home, ScenePosition, 500f, out _),
                    "a ground Home search near the city must not be sent to it");

                Assert.IsTrue(WorldSiteRegistry.TryFindByName(WorldSite.SkyCityName, out WorldSite site),
                    "the war-party director looks the city up by name");
                Assert.AreEqual(SiteKind.Home, site.Kind);
            }
            finally
            {
                WorldSiteRegistry.Clear();
            }
        }

        [Test]
        public void TheSpawnAnchorReachesBothPromenades()
        {
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(SkyCityNavMeshBaker.AssetPath);
            if (data == null)
                Assert.Ignore($"{SkyCityNavMeshBaker.AssetPath} has not been baked.");

            Transform anchor = (Transform)new SerializedObject(fleet.GetComponent<SettlementPopulation>())
                .FindProperty("reachableFrom").objectReferenceValue;
            Assert.IsNotNull(anchor, "reachableFrom");
            Assert.IsTrue(anchor.IsChildOf(fleet.transform), "the anchor moves with the city");

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data, ScenePosition, Quaternion.identity);
            try
            {
                Assert.IsTrue(NavMesh.SamplePosition(ScenePosition + anchor.localPosition, out NavMeshHit from,
                                                     SampleRadius, NavMesh.AllAreas), "anchor on the NavMesh");
                Assert.IsTrue(NavMesh.SamplePosition(ScenePosition + PortLaneFarEnd * SkyCityBuilder.Scale,
                                                     out NavMeshHit to, SampleRadius, NavMesh.AllAreas), "port lane");

                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
            }
            finally
            {
                instance.Remove();
            }
        }
    }
}
