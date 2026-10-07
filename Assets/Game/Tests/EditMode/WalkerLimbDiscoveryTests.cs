// Rig discovery, on limb shapes no model in this project has yet.
//
// Two shapes at 2 and 6 legs -- both of which the base was extracted from -- prove very little. The
// rigs here are built procedurally, so a 3-legged machine, a quadruped with mismatched legs, a
// two-joint stub and a five-joint insect leg all cost no art.
//
// The rule under test is the classification: measure every joint's axle down the chain, then take
// the LONGEST RUN OF MUTUALLY-PARALLEL AXLES as the pitch chain. Joints before it are base DOFs,
// joints after it are tip DOFs. The simpler rule -- consume parallel joints until one is not --
// misclassifies an insect's roll trochanter as the sole, which is the case that decided this.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class WalkerLimbDiscoveryTests
    {
        private GameObject root;
        private Mesh cube;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Rig");
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        // ─────────── building rigs ───────────

        /// One joint: a bone, a pin mesh whose long axis IS the hinge, and nothing else.
        ///
        /// The pin carries a MeshFilter but no MeshRenderer, so it measures as a hinge without
        /// contributing to the contact-point search, which looks at renderers.
        private Transform Joint(string name, Transform parent, Vector3 offset, Vector3 hinge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;

            var pin = new GameObject(name.Split('_')[0] + "Pin");
            pin.transform.SetParent(go.transform, false);
            pin.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, hinge.normalized);
            pin.transform.localScale = new Vector3(0.05f, 0.05f, 0.5f);
            pin.AddComponent<MeshFilter>().sharedMesh = cube;

            return go.transform;
        }

        /// A sole: what the contact point is measured from.
        private void Sole(Transform parent, Vector3 offset)
        {
            var go = new GameObject("SoleMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = new Vector3(0.4f, 0.05f, 0.6f);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>();
        }

        private static readonly Vector3 Yaw = Vector3.up;
        private static readonly Vector3 Pitch = Vector3.right;
        private static readonly Vector3 Roll = Vector3.forward;

        // ─────────── the classic four-joint leg ───────────

        [Test]
        public void TheClassicLegClassifiesAsYawThenThreePitchThenRoll()
        {
            Transform coxa = Joint("Coxa_A", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform hip = Joint("Hip_A", coxa, Vector3.zero, Pitch);
            Transform knee = Joint("Knee_A", hip, new Vector3(0f, -0.8f, 0.4f), Pitch);
            Transform ankle = Joint("Ankle_A", knee, new Vector3(0f, -0.8f, -0.2f), Pitch);
            Transform foot = Joint("Foot_A", ankle, new Vector3(0f, -0.3f, 0f), Roll);
            Sole(foot, new Vector3(0f, -0.05f, 0.1f));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(1, limbs.Count);
            WalkerRig.Limb limb = limbs[0];

            Assert.AreEqual(coxa, limb.Root, "the coxa is the yaw joint");
            Assert.AreEqual(3, limb.Pitch.Length, "hip, knee and ankle are the pitch chain");
            Assert.AreEqual(hip, limb.Pitch[0]);
            Assert.AreEqual(knee, limb.Pitch[1]);
            Assert.AreEqual(ankle, limb.Pitch[2]);
            Assert.AreEqual(foot, limb.Tip, "the foot is the tip roll");
            Assert.IsTrue(limb.Geometry.HasYaw);
            Assert.IsTrue(limb.Geometry.HasRoll);
            Assert.AreEqual(2, limb.Geometry.FreeLinks, "three segments leaves two free links");
        }

        // ─────────── the shapes nothing here has yet ───────────

        /// A limb whose root axle is parallel to the pitch chain has no yaw joint. It is planar, stage 1
        /// is a no-op, and invariant I5 says the gait is told rather than promised zero slip.
        [Test]
        public void ALegWithNoYawJointIsPlanar()
        {
            Transform hip = Joint("Hip_A", root.transform, new Vector3(0f, 2f, 0f), Pitch);
            Transform knee = Joint("Knee_A", hip, new Vector3(0f, -0.8f, 0.4f), Pitch);
            Transform ankle = Joint("Ankle_A", knee, new Vector3(0f, -0.8f, -0.2f), Pitch);
            Sole(ankle, new Vector3(0f, -0.3f, 0.1f));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(1, limbs.Count);
            Assert.IsNull(limbs[0].Root, "there is no yaw joint to find");
            Assert.IsFalse(limbs[0].Geometry.HasYaw);
            Assert.AreEqual(3, limbs[0].Pitch.Length);
        }

        /// The case the longest-run rule exists for. An insect leg is coxa (yaw) -> TROCHANTER (roll)
        /// -> femur -> tibia -> tarsus, and "consume parallel joints until one is not" stops dead at the
        /// trochanter and calls it the sole -- leaving a one-joint limb that cannot reach anything.
        [Test]
        public void AnInsectLegDoesNotMistakeItsTrochanterForASole()
        {
            Transform coxa = Joint("Coxa_I", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform troch = Joint("Trochanter_I", coxa, Vector3.zero, Roll);
            Transform femur = Joint("Femur_I", troch, new Vector3(0f, -0.1f, 0.2f), Pitch);
            Transform tibia = Joint("Tibia_I", femur, new Vector3(0f, -0.7f, 0.5f), Pitch);
            Transform tarsus = Joint("Tarsus_I", tibia, new Vector3(0f, -0.7f, -0.3f), Pitch);
            Sole(tarsus, new Vector3(0f, -0.3f, 0.1f));

            // The trochanter is a base DOF the solver does not drive, and saying so is the point: it is
            // measured, classified correctly, and then held at rest rather than silently ignored.
            LogAssert.Expect(LogType.Warning, new Regex(@"Limb I has 1 base joint"));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(1, limbs.Count);
            WalkerRig.Limb limb = limbs[0];

            Assert.AreEqual(coxa, limb.Root, "the coxa is still the yaw joint");
            Assert.AreEqual(3, limb.Pitch.Length,
                "femur, tibia and tarsus are the pitch chain; the trochanter is a base DOF");
            Assert.AreEqual(femur, limb.Pitch[0]);
            Assert.AreEqual(tarsus, limb.Pitch[2]);
            Assert.AreEqual(1, limb.HeldBase.Length, "the trochanter is measured and held at rest");
            Assert.AreEqual(troch, limb.HeldBase[0]);
            Assert.IsNull(limb.Tip, "there is nothing past the tarsus to be a sole");
        }

        [Test]
        public void AStubbyTwoJointLegSolvesWithOneFreeLink()
        {
            Transform coxa = Joint("Coxa_S", root.transform, new Vector3(0f, 1f, 0f), Yaw);
            Transform hip = Joint("Hip_S", coxa, Vector3.zero, Pitch);
            Transform knee = Joint("Knee_S", hip, new Vector3(0f, -0.5f, 0.3f), Pitch);
            Sole(knee, new Vector3(0f, -0.4f, 0.05f));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(1, limbs.Count);
            Assert.AreEqual(2, limbs[0].Pitch.Length);
            Assert.AreEqual(1, limbs[0].Geometry.FreeLinks, "two segments leaves one free link");
            Assert.IsTrue(limbs[0].Geometry.HasYaw);
        }

        // ─────────── the hazards a name lookup never had ───────────

        /// A model exported without add_leaf_bones=False grows a `_end` bone at every chain tip. It
        /// carries no pin, so it is not a hinge, and a walk that followed it would measure a
        /// zero-length segment and skip the limb.
        [Test]
        public void LeafBonesAreNotMistakenForJoints()
        {
            Transform coxa = Joint("Coxa_L", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform hip = Joint("Hip_L", coxa, Vector3.zero, Pitch);
            Transform knee = Joint("Knee_L", hip, new Vector3(0f, -0.8f, 0.4f), Pitch);
            Transform ankle = Joint("Ankle_L", knee, new Vector3(0f, -0.8f, -0.2f), Pitch);
            Sole(ankle, new Vector3(0f, -0.3f, 0.1f));

            var leaf = new GameObject("Ankle_L_end");
            leaf.transform.SetParent(ankle, false);
            leaf.transform.localPosition = new Vector3(0f, -0.4f, 0f);

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(1, limbs.Count);
            Assert.AreEqual(3, limbs[0].Pitch.Length);
            foreach (Transform joint in limbs[0].Pitch)
                Assert.IsFalse(joint.name.EndsWith("_end"), "a leaf bone was taken for a hinge");
        }

        /// COL_ boxes hang off every joint on these rigs and are pure collision proxies.
        [Test]
        public void CollisionBoxesAreNotMistakenForJoints()
        {
            Transform coxa = Joint("Coxa_C", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform hip = Joint("Hip_C", coxa, Vector3.zero, Pitch);

            // A collision proxy that is itself pin-shaped, so only the name saves it.
            Transform decoy = Joint("COL_Hip_C", hip, new Vector3(0.3f, 0f, 0f), Pitch);
            Assert.IsNotNull(decoy);

            Transform knee = Joint("Knee_C", hip, new Vector3(0f, -0.8f, 0.4f), Pitch);
            Transform ankle = Joint("Ankle_C", knee, new Vector3(0f, -0.8f, -0.2f), Pitch);
            Sole(ankle, new Vector3(0f, -0.3f, 0.1f));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(1, limbs.Count);
            foreach (Transform joint in limbs[0].Pitch)
                Assert.IsFalse(joint.name.StartsWith("COL_"), "a collision box was taken for a hinge");
        }

        /// A limb that genuinely forks is not a chain. Guessing which branch is "the" limb is a coin
        /// toss that shows up as a leg bending the wrong way, so it warns and declines.
        [Test]
        public void ABranchingChainWarnsAndIsSkipped()
        {
            Transform coxa = Joint("Coxa_B", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform femur = Joint("Femur_B", coxa, new Vector3(0f, -0.2f, 0f), Pitch);
            Joint("TibiaLeft_B", femur, new Vector3(-0.3f, -0.7f, 0f), Pitch);
            Joint("TibiaRight_B", femur, new Vector3(0.3f, -0.7f, 0f), Pitch);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"Limb B branches at Femur_B"));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(0, limbs.Count, "a forked limb must not be guessed at");
        }

        // ─────────── measurement ───────────

        [Test]
        public void ContactPointIsTheLowestPointOfTheSoleNotTheLastBone()
        {
            Transform coxa = Joint("Coxa_A", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform hip = Joint("Hip_A", coxa, Vector3.zero, Pitch);
            Transform knee = Joint("Knee_A", hip, new Vector3(0f, -0.8f, 0.4f), Pitch);
            Transform ankle = Joint("Ankle_A", knee, new Vector3(0f, -0.8f, -0.2f), Pitch);
            Sole(ankle, new Vector3(0f, -0.3f, 0.1f));

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);
            Vector3 contact = limbs[0].ContactPoint;

            // The sole box is 0.05 tall, centred 0.3 below the ankle: its underside is at -0.325.
            Assert.AreEqual(ankle.position.y - 0.325f, contact.y, 1e-3f);
            Assert.Less(contact.y, ankle.position.y, "the contact must be below the last hinge");
        }

        [Test]
        public void MaxReachIsTheWholeChainWithAMargin()
        {
            Transform coxa = Joint("Coxa_A", root.transform, new Vector3(0f, 2f, 0f), Yaw);
            Transform hip = Joint("Hip_A", coxa, Vector3.zero, Pitch);
            Transform knee = Joint("Knee_A", hip, new Vector3(0f, -1f, 0f), Pitch);
            Transform ankle = Joint("Ankle_A", knee, new Vector3(0f, -1f, 0f), Pitch);
            Sole(ankle, new Vector3(0f, -0.3f, 0f));

            WalkerLimbGeometry g = WalkerRig.Build(root.transform, root.transform)[0].Geometry;

            Assert.AreEqual(g.TotalLength * 0.97f, g.MaxReach, 1e-4f);
            Assert.AreEqual(1f, g.Pitch[0].Length, 1e-3f, "hip to knee");
            Assert.AreEqual(1f, g.Pitch[1].Length, 1e-3f, "knee to ankle");
            Assert.AreEqual(0.325f, g.Pitch[2].Length, 1e-3f, "ankle to contact");
        }
    }
}
