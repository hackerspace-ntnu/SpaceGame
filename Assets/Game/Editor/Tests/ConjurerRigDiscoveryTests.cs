// Does the Lightning Conjurer's rig actually bind to the IK walker?
//
// The failure this exists to catch is silent. WalkerRig does not throw when a rig is wrong -- it
// warns, keeps whatever it could classify, and hands back a limb that solves with a shorter
// linkage than the model has. A leg that classifies as a one-joint stub still "works": the
// creature stands there, feet planted, sunk to whatever height its hips ended up at, with a
// console that says nothing you would notice among the import spam.
//
// Asserting on the measurement beats pressing play and forming an opinion, and every number below
// is one that _Source~/walkerize.py already verified on the Blender side. This is the same check
// on the far side of the FBX round trip, which is the half walkerize.py cannot see.
using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SpaceGame.Locomotion;

namespace SpaceGame.EditorTools
{
    public class ConjurerRigDiscoveryTests
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/creatures/LightningConjurer.prefab";

        private GameObject instance;

        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath}. Run Tools > Creatures > " +
                                     "Build Lightning Conjurer.");
            instance = Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        [TearDown]
        public void TearDown()
        {
            if (instance != null) Object.DestroyImmediate(instance);
        }

        private List<WalkerRig.Limb> Discover()
        {
            Transform armature = WalkerRig.FindArmature(instance.transform);
            Assert.IsNotNull(armature, "FindArmature returned nothing at all.");
            return WalkerRig.Build(armature, instance.transform);
        }

        [Test]
        public void BothLegsAreDiscovered()
        {
            List<WalkerRig.Limb> limbs = Discover();

            var ids = new List<string>();
            foreach (WalkerRig.Limb limb in limbs) ids.Add(limb.Id);

            Assert.AreEqual(2, limbs.Count,
                "Expected exactly two legs. Found: [" + string.Join(", ", ids) + "]. " +
                "A count of 0 means the rig is not in the walker convention at all -- re-run " +
                "_Source~/walkerize.py and re-export the FBX.");
        }

        [Test]
        public void EachLegHasTheFullThreeJointPitchChain()
        {
            foreach (WalkerRig.Limb limb in Discover())
            {
                // THE assertion. A pitch chain shorter than three means the pins did not survive
                // the FBX round trip and WalkerRig fell back to measuring the rest pose -- which
                // on a leg this straight returns axles tens of degrees out of true, breaking the
                // mutually-parallel run that Classify keeps.
                Assert.AreEqual(3, limb.Pitch.Length,
                    $"Leg {limb.Id} classified with a {limb.Pitch.Length}-joint pitch chain " +
                    "instead of Hip/Knee/Ankle. Its hinge pins are missing or mis-measured.");

                Assert.IsNotNull(limb.Root, $"Leg {limb.Id} has no yaw joint (Coxa_{limb.Id}); " +
                                            "a planted foot will be dragged when the body turns.");
                Assert.IsNotNull(limb.Tip, $"Leg {limb.Id} has no sole roll joint (Foot_{limb.Id}); " +
                                           "the sole cannot lie flat across a slope.");
            }
        }

        [Test]
        public void PitchHingesRunAcrossTheBodyAndTheYawHingeIsVertical()
        {
            foreach (WalkerRig.Limb limb in Discover())
            {
                WalkerLimbGeometry g = limb.Geometry;

                // The creature faces +Z, so a leg swinging fore and aft hinges about world X.
                // Checking the ABSOLUTE axis, not merely that the three agree with each other:
                // three pins sharing one WRONG axis are mutually parallel and still useless, which
                // is exactly the bug walkerize.py's own verification was initially blind to.
                for (int i = 0; i < g.Pitch.Length; i++)
                {
                    Vector3 world = limb.Pitch[i].TransformDirection(g.Pitch[i].AxleLocal);
                    Assert.GreaterOrEqual(Mathf.Abs(Vector3.Dot(world.normalized, Vector3.right)), 0.98f,
                        $"Leg {limb.Id} joint {limb.Pitch[i].name} hinges about {world.normalized}, " +
                        "which is not across the body. The leg will swing in the wrong plane.");
                }

                Vector3 yaw = instance.transform.TransformDirection(g.YawAxisBody);
                Assert.GreaterOrEqual(Mathf.Abs(Vector3.Dot(yaw.normalized, Vector3.up)), 0.98f,
                    $"Leg {limb.Id} yaw axis is {yaw.normalized}, not vertical.");
            }
        }

        [Test]
        public void FeetRestOnThePrefabOrigin()
        {
            // The builder drops the model so the soles sit on the prefab origin, and
            // CalibrateRideHeight reads the body's height above its feet straight off that. A sole
            // measured somewhere other than y = 0 puts the whole creature that far into, or above,
            // the ground -- which is what "stuck in the ground" looks like.
            foreach (WalkerRig.Limb limb in Discover())
            {
                Vector3 contact = limb.ContactPoint;

                Assert.AreEqual(0f, contact.y, 0.05f,
                    $"Leg {limb.Id}'s sole measures at y = {contact.y:0.000} on a prefab whose " +
                    "origin should be its foot level. The ride height will be off by this much.");
            }
        }

        [Test]
        public void LegReachMatchesAnEighteenMetreCreature()
        {
            // A blunt scale check. If the FBX ever imports at the wrong globalScale, every number
            // tuned in the builder -- step clearance, foothold reach, stop distances -- is wrong
            // together, and this is the cheapest place to notice.
            foreach (WalkerRig.Limb limb in Discover())
            {
                float reach = limb.Geometry.TotalLength;
                Assert.That(reach, Is.InRange(10f, 13f),
                    $"Leg {limb.Id} measures {reach:0.00} m of reach; expected about 11.8 m for a " +
                    "creature 18.1 m tall. The FBX import scale has changed.");
            }
        }
    }
}
