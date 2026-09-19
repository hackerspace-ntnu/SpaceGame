// The built Sky City, read off disk. Each assertion is one way the builder can produce a prefab
// that looks complete in the Inspector and drops the player through it in play:
//
//   collision islands that never became colliders, or whose source meshes still render;
//   a convex hull saved in memory only, so the MeshCollider ships with no mesh;
//   the lattice given a convex hull, which fills the lanes it stands over;
//   a ladder marker without its step-off points, or whose exit is not floor;
//   a ladder the player's own capsule cannot climb, or cannot step off.
using System.Linq;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
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

        // A capsule this much thinner than the player's is what the column is checked with, so a
        // rail the body only brushes is not reported as a wall.
        private const float BrushTolerance = 0.05f;

        // How far the step-off may start inside a collider. Unity's depenetration clears this within a
        // physics step; ladder 05's exit sits 0.10 m under Bag1's flank.
        private const float ExitOverlapTolerance = 0.15f;

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

        [Test]
        public void ThePlayerCanClimbEveryLadderAndStepOffAtTheTop()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(LadderClimberWiring.PlayerPrefabPath);
            var climber = player.GetComponent<LadderClimber>();
            Assert.IsNotNull(climber, "Tools > SpaceGame > Player > Wire Ladder Climber");
            float standoff = new SerializedObject(climber).FindProperty("standoff").floatValue;
            float lift = new SerializedObject(climber).FindProperty("stepOffLift").floatValue;
            float mantle = new SerializedObject(climber).FindProperty("mantleReach").floatValue;
            // World size: the player's capsule sits on a transform stretched 1.5 in Y, so the
            // authored 2 m capsule is a 3 m body.
            CapsuleCollider capsule = player.GetComponent<PlayerMovement>().BodyCapsule;
            Vector3 scale = capsule.transform.lossyScale;
            float bodyRadius = capsule.radius * Mathf.Max(scale.x, scale.z);
            float radius = bodyRadius - BrushTolerance;
            float height = capsule.height * scale.y;

            GameObject city = Object.Instantiate(prefab);
            try
            {
                Physics.SyncTransforms();
                foreach (Ladder ladder in city.GetComponentsInChildren<Ladder>(true))
                {
                    Assert.IsTrue(ladder.IsValid, ladder.name);
                    Vector3 toward = ladder.TowardClimber;
                    Vector3 line = ladder.Foot + toward * standoff;
                    Assert.IsTrue(ladder.Contains(line + Vector3.up * lift), $"{ladder.name}: its climbing line is outside its volume");

                    // The climb up to where the climber steps over the top on meeting something: from
                    // feet on the deck to feet a body height plus a floor's thickness below the step-off.
                    // Anything above that is climbed over, not climbed into.
                    float highestFeet = Mathf.Max(ladder.Foot.y + lift, ladder.TopHeight - height - mantle);
                    Vector3 low = line + Vector3.up * (lift + bodyRadius);
                    Vector3 high = new Vector3(line.x, highestFeet + height - bodyRadius, line.z);
                    Collider[] blocking = Physics.OverlapCapsule(low, high, radius, ~0, QueryTriggerInteraction.Ignore);
                    Assert.IsEmpty(blocking.Select(c => c.name), $"{ladder.name}: the climb is blocked below its top");

                    AssertRoomToStepOff(ladder, capsule, bodyRadius, height, lift);
                }
            }
            finally
            {
                Object.DestroyImmediate(city);
            }
        }

        private static void AssertRoomToStepOff(Ladder ladder, CapsuleCollider player, float radius, float height, float lift)
        {
            var probe = new GameObject("StepOffProbe").AddComponent<CapsuleCollider>();
            try
            {
                probe.radius = radius;
                probe.height = height;
                Vector3 centre = ladder.ExitPoint + Vector3.up * (lift + height * 0.5f);
                Physics.SyncTransforms();
                Vector3 spine = Vector3.up * (height * 0.5f - radius);
                foreach (Collider c in Physics.OverlapCapsule(centre - spine, centre + spine, radius, ~0,
                                                              QueryTriggerInteraction.Ignore))
                {
                    if (c == probe) continue;
                    bool inside = Physics.ComputePenetration(probe, centre, Quaternion.identity,
                                                             c, c.transform.position, c.transform.rotation,
                                                             out _, out float depth);
                    Assert.IsFalse(inside && depth > ExitOverlapTolerance,
                                   $"{ladder.name}: steps off {depth:F2} m into {c.name}");
                }
            }
            finally
            {
                Object.DestroyImmediate(probe.gameObject);
            }
        }
    }
}
