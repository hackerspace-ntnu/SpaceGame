// The Strider dune barges' running gear, driven by hand on the built prefabs: the track links step
// round their loop and the wheels turn at the speed each side of the hull is seen to move over the
// ground. Nothing here is networked or saved; TrackBelts reads only the hull's own transform.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class TrackBeltsTests
    {
        private const float Dt = 1f / 60f;
        private const float Cruise = 4f;

        private GameObject barge;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(barge);

        private static GameObject Prefab(string variant)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StriderBargeBuilder.PrefabPath(variant));
            Assert.IsNotNull(prefab, "run Tools/SpaceGame/Vehicles/Build Strider Barges");
            return prefab;
        }

        private TrackBelts Spawn(string variant = "StriderDuneBarge")
        {
            barge = Object.Instantiate(Prefab(variant));
            var belts = barge.GetComponent<TrackBelts>();
            Assert.IsNotNull(belts, $"{variant} has no TrackBelts");
            belts.ResetBaseline();
            return belts;
        }

        private static void Drive(TrackBelts belts, float speed, float yawRate, float cameraDistance, int frames)
        {
            Transform hull = belts.transform;
            for (int i = 0; i < frames; i++)
            {
                hull.position += hull.forward * (speed * Dt);
                hull.Rotate(0f, yawRate * Dt, 0f);
                belts.Present(Dt, cameraDistance);
            }
        }

        private static Vector3 Centre(Transform link) =>
            link.TransformPoint(link.GetComponent<MeshFilter>().sharedMesh.bounds.center);

        private static Transform LowestMidLink(TrackBelts belts, int belt)
        {
            Transform[] links = belts.LinksOf(belt);
            float bottom = links.Min(l => Centre(l).y);
            float midZ = links.Average(l => belts.transform.InverseTransformPoint(Centre(l)).z);
            return links.Where(l => Centre(l).y < bottom + 0.1f)
                        .OrderBy(l => Mathf.Abs(belts.transform.InverseTransformPoint(Centre(l)).z - midZ))
                        .First();
        }

        public static IEnumerable<TestCaseData> Barges()
        {
            yield return new TestCaseData("StriderDuneBarge", 4);
            yield return new TestCaseData("StriderDuneBargeLookout", 4);
            yield return new TestCaseData("StriderDuneBargeCompact", 2);
        }

        [TestCaseSource(nameof(Barges))]
        public void EveryTrackLinkAndWheel_IsOnABelt(string variant, int expectedBelts)
        {
            GameObject prefab = Prefab(variant);
            var belts = prefab.GetComponent<TrackBelts>();
            Assert.IsNotNull(belts, $"{variant} has no TrackBelts");
            Assert.AreEqual(expectedBelts, belts.BeltCount);

            var linked = new HashSet<Transform>();
            var wheeled = new HashSet<Transform>();
            for (int b = 0; b < belts.BeltCount; b++)
            {
                Assert.GreaterOrEqual(belts.LinksOf(b).Length, 20, $"belt {b} has too few links to be a loop");
                Assert.Greater(belts.WheelsOf(b).Length, 0, $"belt {b} has no wheels");
                linked.UnionWith(belts.LinksOf(b));
                wheeled.UnionWith(belts.WheelsOf(b));
            }

            Transform[] allLinks = prefab.GetComponentsInChildren<MeshFilter>(true)
                                         .Where(f => f.name.StartsWith("Mesh_TrackAssembly_") && f.name.Contains("_Link_"))
                                         .Select(f => f.transform).ToArray();
            // The return rollers sit on brackets: spun, the bracket would swing round like a lever.
            Transform[] allWheels = prefab.GetComponentsInChildren<Transform>(true)
                                          .Where(t => t.name.StartsWith("Bone_Wheel_") && !t.name.Contains("_ReturnRoller")).ToArray();
            CollectionAssert.AreEquivalent(allLinks, linked, "a track link is left standing still");
            CollectionAssert.AreEquivalent(allWheels, wheeled, "a wheel is left standing still, or a bracket is spun");
        }

        [Test]
        public void DrivingAtCruise_TheGroundRunStaysPutOnTheSand()
        {
            TrackBelts belts = Spawn();
            Drive(belts, Cruise, 0f, float.NaN, 120);           // past the speed smoothing
            for (int b = 0; b < belts.BeltCount; b++)
            {
                Transform link = LowestMidLink(belts, b);
                Vector3 before = Centre(link);
                // 1 m: inside even the pods' short, rounded ground run, which lifts a link as it goes,
                // so only the slide along the ground counts.
                Drive(belts, Cruise, 0f, float.NaN, 15);
                float slide = Vector3.Dot(Centre(link) - before, belts.transform.forward);
                Assert.Less(Mathf.Abs(slide), 0.1f, $"belt {b}'s ground run slid {slide:F2} m over the sand");
            }
        }

        [Test]
        public void DrivingForward_TheTopRunGoesForward_AndTheWheelsRollForward()
        {
            TrackBelts belts = Spawn();
            Transform hull = belts.transform;
            Drive(belts, Cruise, 0f, float.NaN, 120);
            for (int b = 0; b < belts.BeltCount; b++)
            {
                Transform top = belts.LinksOf(b).OrderByDescending(l => Centre(l).y).First();
                Transform wheel = belts.WheelsOf(b)[0];
                Vector3 rim = wheel.InverseTransformPoint(wheel.position + hull.up);
                float topBefore = hull.InverseTransformPoint(Centre(top)).z;
                float rimBefore = hull.InverseTransformPoint(wheel.TransformPoint(rim)).z;

                Drive(belts, Cruise, 0f, float.NaN, 3);
                Assert.Greater(hull.InverseTransformPoint(Centre(top)).z, topBefore, $"belt {b}'s top run runs backwards");
                Assert.Greater(hull.InverseTransformPoint(wheel.TransformPoint(rim)).z, rimBefore, $"belt {b}'s wheels roll backwards");
            }
        }

        [Test]
        public void TurningOnTheSpot_RunsTheTwoSidesInOppositeDirections()
        {
            TrackBelts belts = Spawn();
            Drive(belts, 0f, 30f, float.NaN, 120);
            float left = 0f, right = 0f;
            for (int b = 0; b < belts.BeltCount; b++)
            {
                if (belts.IsLeft(b)) left += belts.SpeedOf(b);
                else right += belts.SpeedOf(b);
            }
            Assert.Greater(Mathf.Abs(left), 0.5f, "the left track stands still while the hull turns");
            Assert.Less(left * right, 0f, "both sides run the same way while the hull turns on the spot");
        }

        [Test]
        public void AStandingBarge_MovesNothing()
        {
            TrackBelts belts = Spawn();
            Transform link = belts.LinksOf(0)[0];
            Vector3 before = link.localPosition;
            Quaternion wheelBefore = belts.WheelsOf(0)[0].localRotation;
            Drive(belts, 0f, 0f, float.NaN, 60);
            Assert.AreEqual(before, link.localPosition);
            Assert.AreEqual(wheelBefore, belts.WheelsOf(0)[0].localRotation);
        }

        [Test]
        public void FarFromTheCamera_TheRunningGearIsLeftAlone()
        {
            TrackBelts belts = Spawn();
            Transform link = belts.LinksOf(0)[0];
            Vector3 before = link.localPosition;
            Drive(belts, Cruise, 0f, 10000f, 60);
            Assert.AreEqual(before, link.localPosition);
        }

        [Test]
        public void ASnap_IsNotReadAsSpeed()
        {
            TrackBelts belts = Spawn();
            Drive(belts, 0f, 0f, float.NaN, 10);
            belts.transform.position += Vector3.forward * 500f;   // a load, a streaming migrate
            belts.Present(Dt, float.NaN);
            for (int b = 0; b < belts.BeltCount; b++)
                Assert.AreEqual(0f, belts.SpeedOf(b), 1e-4f);
        }
    }
}
