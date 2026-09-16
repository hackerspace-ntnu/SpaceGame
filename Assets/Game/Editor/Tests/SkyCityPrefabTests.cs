// The built Sky City, read off disk. Each assertion is one way the builder can produce a prefab
// that looks complete in the Inspector and drops the player through it in play:
//
//   collision islands that never became colliders, or whose source meshes still render;
//   a convex hull saved in memory only, so the MeshCollider ships with no mesh;
//   the lattice given a convex hull, which fills the lanes it stands over;
//   a ladder marker without its step-off points, or whose exit is not floor.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class SkyCityPrefabTests
    {
        // sky_city_export.py reports how many islands it split out; at the time of writing, 645.
        private const int MinimumIslands = 600;
        private const int Ladders = 7;
        private const float FloorTolerance = 0.3f;
        private const float ProbeHeight = 1.0f;

        private GameObject prefab;

        [SetUp]
        public void SetUp()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkyCityBuilder.PrefabPath);
            if (prefab == null)
                Assert.Ignore($"{SkyCityBuilder.PrefabPath} has not been built (Tools > Environment > Build Sky City Prefab).");
        }

        [Test]
        public void EveryCollisionIslandBecameAColliderAndNoneStillRenders()
        {
            Transform group = prefab.transform.Find(SkyCityBuilder.CollisionGroup);
            Assert.IsNotNull(group, SkyCityBuilder.CollisionGroup);
            Assert.GreaterOrEqual(group.GetComponentsInChildren<Collider>(true).Length, MinimumIslands);
            Assert.IsEmpty(prefab.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.name.StartsWith(SkyCityBuilder.CollisionPrefix)).Select(r => r.name));
        }

        [Test]
        public void EveryHullColliderHasASavedConvexMesh()
        {
            Transform group = prefab.transform.Find(SkyCityBuilder.CollisionGroup);
            foreach (MeshCollider mc in group.GetComponentsInChildren<MeshCollider>(true))
            {
                Assert.IsTrue(mc.convex, mc.name);
                Assert.IsNotNull(mc.sharedMesh, $"{mc.name}: hull mesh was not saved to {SkyCityBuilder.HullsPath}");
                Assert.AreEqual(SkyCityBuilder.HullsPath, AssetDatabase.GetAssetPath(mc.sharedMesh), mc.name);
            }
        }

        [Test]
        public void LatticeCollidesAsMeshAndBagsAsHulls()
        {
            foreach (string name in new[] { "Mesh_SkyCity_Cage", "Mesh_SkyCity_Cradles", "Mesh_SkyCity_Decks", "Mesh_SkyCity_SternGear" })
            {
                var mc = prefab.GetComponentsInChildren<MeshCollider>(true).FirstOrDefault(c => c.name.StartsWith(name));
                Assert.IsNotNull(mc, name);
                Assert.IsFalse(mc.convex, $"{name}: a hull fills the lanes the lattice stands over");
            }
            var bags = prefab.GetComponentsInChildren<MeshCollider>(true).Where(c => c.name.StartsWith("Mesh_SkyCity_Bag")).ToList();
            Assert.AreEqual(3, bags.Count);
            Assert.IsTrue(bags.All(c => c.convex));
        }

        [Test]
        public void EveryLadderHasItsStepOffPointsAndItsExitIsFloor()
        {
            Transform group = prefab.transform.Find(SkyCityBuilder.LadderGroup);
            Assert.IsNotNull(group, SkyCityBuilder.LadderGroup);
            Assert.AreEqual(Ladders, group.childCount);

            GameObject city = Object.Instantiate(prefab);
            try
            {
                Physics.SyncTransforms();
                foreach (Transform ladder in city.transform.Find(SkyCityBuilder.LadderGroup))
                {
                    Transform top = ladder.Find(ladder.name + SkyCityBuilder.LadderTop);
                    Transform exit = ladder.Find(ladder.name + SkyCityBuilder.LadderExit);
                    Assert.IsNotNull(top, ladder.name);
                    Assert.IsNotNull(exit, ladder.name);
                    Assert.AreEqual(top.position.y, exit.position.y, 0.05f, $"{ladder.name}: steps off at its exit's height");
                    Assert.Greater(top.position.y, ladder.position.y, ladder.name);

                    bool hit = Physics.Raycast(exit.position + Vector3.up * ProbeHeight, Vector3.down, out RaycastHit floor,
                        ProbeHeight + FloorTolerance);
                    Assert.IsTrue(hit, $"{ladder.name}: nothing under its exit");
                    Assert.AreEqual(exit.position.y, floor.point.y, FloorTolerance, $"{ladder.name}: exit is not on the floor");
                }
            }
            finally
            {
                Object.DestroyImmediate(city);
            }
        }
    }
}
