// The built Sky City fleet, read off disk:
//
//   the flagship nested as the city prefab, not a copy that drifts from it;
//   every escort present, as its own prefab, with solid gas bags;
//   no escort parked inside the flagship.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class SkyFleetPrefabTests
    {
        private GameObject fleet;

        [SetUp]
        public void SetUp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SkyFleetBuilder.FleetPrefabPath);
            if (asset == null)
                Assert.Ignore($"{SkyFleetBuilder.FleetPrefabPath} has not been built (Tools > Environment > Build Sky Fleet Prefabs).");
            // In a scene, so renderers have world bounds.
            fleet = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        }

        [TearDown]
        public void TearDown()
        {
            if (fleet != null) Object.DestroyImmediate(fleet);
        }

        [Test]
        public void FlagshipIsTheCityPrefabNotACopy()
        {
            Assert.IsNotNull(Instance(SkyCityBuilder.PrefabPath));
        }

        [Test]
        public void EveryEscortIsItsOwnPrefabWithSolidBags()
        {
            foreach (SkyFleetBuilder.Vessel vessel in SkyFleetBuilder.Vessels)
            {
                GameObject ship = Instance(vessel.PrefabPath);
                Assert.IsNotNull(ship, vessel.Name);
                var bags = ship.GetComponentsInChildren<MeshCollider>(true)
                    .Where(c => c.name.StartsWith("Mesh_SkyCity_Bag")).ToList();
                Assert.IsNotEmpty(bags, vessel.Name);
                Assert.IsTrue(bags.All(c => c.convex && c.sharedMesh != null), vessel.Name);
            }
        }

        [Test]
        public void NoEscortSitsInsideTheFlagship()
        {
            Bounds city = SkyFleetBuilder.RendererBounds(Instance(SkyCityBuilder.PrefabPath));
            foreach (SkyFleetBuilder.Vessel vessel in SkyFleetBuilder.Vessels)
            {
                Bounds ship = SkyFleetBuilder.RendererBounds(Instance(vessel.PrefabPath));
                Assert.IsFalse(ship.Intersects(city), $"{vessel.Name} overlaps the city");
            }
        }

        private GameObject Instance(string prefabPath)
        {
            // Nested: a child's direct source is inside the fleet prefab; its nearest
            // instance root names the prefab it really is.
            return fleet.transform.Cast<Transform>().Select(t => t.gameObject)
                .FirstOrDefault(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefabPath);
        }
    }
}
