// Guards the plate that carries a boarding ramp's foot down to the sand: a hull is set down on the
// highest ground it spans, so the sand under a ramp's foot is usually lower than the foot, and a
// player capsule with no step offset cannot walk over the lip that leaves.
//
// In Editor/ because RampApron lives in Assembly-CSharp. Built far from the origin so nothing in an
// open scene is under the probe; Awake is called by hand because EditMode runs no lifecycle.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class RampApronTests
    {
        private static readonly Vector3 Origin = new(7000f, 2000f, 7000f);
        private const float RampSlope = 40f;
        private const float Tolerance = 0.01f;

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void ThePlateRunsFromTheRampsFootDownToTheSandWithNoLip()
        {
            Sand(-0.4f);
            RampApron apron = Hull(out BoxCollider ramp, out Vector3 foot);

            apron.Fit();

            BoxCollider plate = apron.GetComponent<BoxCollider>();
            Assert.IsTrue(plate.enabled, "a ramp foot 0.4 m above the sand got no plate.");

            TopEdges(plate, out Vector3 high, out Vector3 low);
            Assert.Less(Vector3.Distance(high, foot), Tolerance, "the plate does not start on the ramp's foot edge.");
            Assert.AreEqual(Origin.y - 0.4f, low.y, Tolerance, "the plate's far edge stands off the sand: a lip.");
            Assert.AreEqual(20f, Vector3.Angle(plate.transform.up, Vector3.up), 0.1f, "the plate is not at its slope.");
            Assert.IsTrue(Physics.GetIgnoreCollision(plate, ramp),
                          "the plate can collide with its own hull, and a kinematic plate shoves a hull it touches.");
        }

        [Test]
        public void AFootAlreadyOnTheSandGetsNoPlate()
        {
            Sand(0.05f);
            RampApron apron = Hull(out _, out _);

            apron.Fit();

            Assert.IsFalse(apron.GetComponent<BoxCollider>().enabled, "a foot buried in the sand grew a plate.");
        }

        [Test]
        public void SandFallingAwayIsMetWhereThePlateLandsNotUnderTheFoot()
        {
            GameObject sand = Sand(-0.4f);
            sand.transform.rotation = Quaternion.AngleAxis(-8f, Vector3.right);
            Physics.SyncTransforms();
            RampApron apron = Hull(out _, out _);

            apron.Fit();

            BoxCollider plate = apron.GetComponent<BoxCollider>();
            Assert.IsTrue(plate.enabled, "a foot above a falling dune got no plate.");
            TopEdges(plate, out _, out Vector3 low);
            Assert.IsTrue(Physics.Raycast(low + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f));
            Assert.AreEqual(hit.point.y, low.y, 0.03f, "the plate was sized from the sand under the foot and ends in mid-air.");
        }

        // ─────────────────────────── Fixture ───────────────────────────

        private GameObject Sand(float topY)
        {
            GameObject sand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(sand);
            sand.transform.localScale = new Vector3(60f, 1f, 60f);
            sand.transform.position = Origin + Vector3.up * (topY - 0.5f);
            Physics.SyncTransforms();
            return sand;
        }

        /// <summary>A hull with one 40° ramp whose walking surface ends on the hull's ground plane.</summary>
        private RampApron Hull(out BoxCollider ramp, out Vector3 foot)
        {
            var hull = new GameObject("TestHull");
            spawned.Add(hull);
            hull.transform.position = Origin;

            var rampGo = new GameObject("Ramp");
            rampGo.transform.SetParent(hull.transform, false);
            ramp = rampGo.AddComponent<BoxCollider>();
            ramp.size = new Vector3(3f, 0.1f, 4f);
            // Local +Z runs down the slope, out of the ship along -Z.
            Quaternion down = Quaternion.LookRotation(new Vector3(0f, -Mathf.Sin(RampSlope * Mathf.Deg2Rad),
                                                                 -Mathf.Cos(RampSlope * Mathf.Deg2Rad)));
            Vector3 topCentre = new Vector3(0f, 2f * Mathf.Sin(RampSlope * Mathf.Deg2Rad), -2f * Mathf.Cos(RampSlope * Mathf.Deg2Rad));
            rampGo.transform.SetPositionAndRotation(Origin + topCentre - (down * Vector3.up) * 0.05f, down);
            foot = Origin + new Vector3(0f, 0f, -4f * Mathf.Cos(RampSlope * Mathf.Deg2Rad));

            var apronGo = new GameObject("Apron");
            apronGo.transform.SetParent(hull.transform, false);
            apronGo.AddComponent<BoxCollider>();
            apronGo.AddComponent<Rigidbody>();
            var apron = apronGo.AddComponent<RampApron>();
            typeof(RampApron).GetField("ramp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(apron, ramp);
            typeof(RampApron).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(apron, null);

            Physics.SyncTransforms();
            return apron;
        }

        /// <summary>The middle of the plate's top face at its higher and lower ends.</summary>
        private static void TopEdges(BoxCollider plate, out Vector3 high, out Vector3 low)
        {
            Physics.SyncTransforms();
            Transform t = plate.transform;
            Vector3 top = plate.center + Vector3.up * (plate.size.y * 0.5f);
            Vector3 a = t.TransformPoint(top + Vector3.forward * (plate.size.z * 0.5f));
            Vector3 b = t.TransformPoint(top - Vector3.forward * (plate.size.z * 0.5f));
            high = a.y > b.y ? a : b;
            low = a.y > b.y ? b : a;
        }
    }
}
