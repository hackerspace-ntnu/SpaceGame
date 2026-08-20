// Limbs that are not legs.
//
// `WalkerRig.RootPrefixes` used to be { Limb_, Coxa_, Hip_ } and `LeggedLocomotion.Initialise`
// turned every limb the rig returned into a `LegState`. So an arm named `Arm_L` was not discovered
// at all, and an arm named `Limb_L` was discovered and then WALKED ON -- given a gait slot, a
// foothold and a share of the machine's weight.
//
// Two questions here, and they are separate on purpose:
//
//   1. Does the rig say which limbs are legs? (discovery)
//   2. Does an arm reach a point it is told to reach? (the seam)
//
// The second is deliberately tested against a bare `WalkerArm` as well as through a whole machine,
// because the whole value of keeping it a plain class is that it needs no scene, no gait and no
// component to be true.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    /// A machine whose arms are given the travel a shoulder actually has. The base defaults arm limits
    /// to the leg's ranges, which is right for a rig that has never thought about it and far too tight
    /// for one reaching around itself.
    public class ArmedTestMachine : LeggedLocomotion
    {
        public IStrideModel Stride = new YawArcStride(40f);
        public IGaitPattern Gait = new AlternatingGait(0.4f, 0.5f);
        public IBodyMotion Motion = new LevelDeckBody();
        public IFootStyle Feet = new FlatSole();

        protected override IStrideModel CreateStride() => Stride;
        protected override IGaitPattern CreateGait() => Gait;
        protected override IBodyMotion CreateBody() => Motion;
        protected override IFootStyle CreateFeet() => Feet;

        protected override WalkerLimbSolver.Limits BuildArmLimits(in WalkerLimbGeometry g)
            => WalkerArmTests.ShoulderLimits(g.PitchCount);

        /// What a robot's own component would do each frame once it has written its targets.
        public void DriveArms() => SolveArms();
    }

    public class WalkerArmTests
    {
        private GameObject ground;
        private GameObject root;
        private Mesh cube;

        /// Generous travel, so the acceptance measurement is about the IK rather than about a limit.
        public static WalkerLimbSolver.Limits ShoulderLimits(int pitchCount)
            => WalkerLimbSolver.Limits.Uniform(150f, 170f, 90f, pitchCount);

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(600f, 1f, 600f);

            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        // ─────────── building rigs ───────────

        private Transform Joint(string name, Transform parent, Vector3 offset, Vector3 hinge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;

            var pin = new GameObject(name.Split('_')[0] + "Pin");
            pin.transform.SetParent(go.transform, false);
            pin.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, hinge.normalized);
            pin.transform.localScale = new Vector3(0.03f, 0.03f, 0.3f);
            pin.AddComponent<MeshFilter>().sharedMesh = cube;
            return go.transform;
        }

        /// The mesh whose lowest point is the contact -- a sole on a leg, a palm on an arm.
        private void Pad(Transform parent, Vector3 offset)
        {
            var go = new GameObject("PadMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = new Vector3(0.2f, 0.05f, 0.25f);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>();
        }

        /// A classic leg: Coxa (yaw) -> Hip -> Knee -> Ankle, splayed outboard.
        private void Leg(string id, Vector3 attach)
        {
            Vector3 outward = new Vector3(attach.x, 0f, attach.z).normalized;
            Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

            Transform coxa = Joint("Coxa_" + id, root.transform, attach, Vector3.up);
            Transform hip = Joint("Hip_" + id, coxa, Vector3.zero, pitch);
            Transform knee = Joint("Knee_" + id, hip, outward * 0.7f - Vector3.up * 0.3f, pitch);
            Transform ankle = Joint("Ankle_" + id, knee, outward * 0.28f - Vector3.up * 1.4f, pitch);
            Pad(ankle, -Vector3.up * 0.15f);
        }

        /// An arm: an `Arm_` root carrying joints whose names are the rig's own business. The names are
        /// deliberately NOT the classic leg's, because an arm is never assembled by name.
        private void Arm(string id, Vector3 attach)
        {
            Vector3 outward = new Vector3(attach.x, 0f, attach.z).normalized;
            Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

            // Bent at rest, so there is reach left over for a target to be commanded into. An arm
            // modelled near-straight solves fine and has nowhere to go.
            Transform shoulder = Joint("Arm_" + id, root.transform, attach, Vector3.up);
            Transform upper = Joint("Upper_" + id, shoulder, Vector3.zero, pitch);
            Transform elbow = Joint("Elbow_" + id, upper, outward * 0.55f - Vector3.up * 0.3f, pitch);
            Transform wrist = Joint("Wrist_" + id, elbow, -outward * 0.35f - Vector3.up * 0.55f, pitch);
            Pad(wrist, -Vector3.up * 0.12f);
        }

        /// Two legs and two arms, sharing ids across the two roles exactly as a humanoid would.
        private void BuildBiped()
        {
            root = new GameObject("Armed");
            root.transform.position = new Vector3(0f, 2f, 0f);

            Leg("L", new Vector3(-0.45f, 0f, 0f));
            Leg("R", new Vector3(0.45f, 0f, 0f));
            Arm("L", new Vector3(-0.65f, 1.5f, 0f));
            Arm("R", new Vector3(0.65f, 1.5f, 0f));

            Physics.SyncTransforms();
        }

        private static int CountRole(List<WalkerRig.Limb> limbs, WalkerRig.LimbRole role)
        {
            int n = 0;
            foreach (WalkerRig.Limb limb in limbs) if (limb.Role == role) n++;
            return n;
        }

        // ─────────── discovery ───────────

        [Test]
        public void AnArmRootIsDiscoveredAndCarriesTheArmRole()
        {
            BuildBiped();
            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(4, limbs.Count, "two legs and two arms should all be found");
            Assert.AreEqual(2, CountRole(limbs, WalkerRig.LimbRole.Leg));
            Assert.AreEqual(2, CountRole(limbs, WalkerRig.LimbRole.Arm));
        }

        /// The regression that matters most. Both shipping rigs are rooted at `Coxa_`, and a role that
        /// defaulted the other way -- or a prefix table that reordered -- would turn six legs into six
        /// arms and stand the machine on nothing.
        [Test]
        public void TheClassicRootsStillMeanLeg()
        {
            root = new GameObject("Legged");
            root.transform.position = new Vector3(0f, 2f, 0f);
            Leg("A", new Vector3(-0.45f, 0f, 0.4f));
            Leg("B", new Vector3(0.45f, 0f, 0.4f));
            Physics.SyncTransforms();

            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            Assert.AreEqual(2, limbs.Count);
            foreach (WalkerRig.Limb limb in limbs)
                Assert.AreEqual(WalkerRig.LimbRole.Leg, limb.Role,
                    "a Coxa_ root must still mean a leg, or both shipping machines lose their legs");
        }

        /// A humanoid naming its legs `Coxa_L`/`Coxa_R` and its arms `Arm_L`/`Arm_R` is the obvious
        /// thing to do, and it shares both ids across the two roles. One id namespace would drop the
        /// arms silently; the classic-name lookup applied to an arm would build it out of the LEG's
        /// bones, which is worse.
        [Test]
        public void AnArmAndALegMayShareAnId()
        {
            BuildBiped();
            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);

            WalkerRig.Limb armL = limbs.Find(l => l.Role == WalkerRig.LimbRole.Arm && l.Id == "L");
            WalkerRig.Limb legL = limbs.Find(l => l.Role == WalkerRig.LimbRole.Leg && l.Id == "L");

            Assert.IsNotNull(armL, "the arm sharing an id with a leg was dropped");
            Assert.IsNotNull(legL);
            Assert.AreEqual("Arm_L", armL.Root.name);
            Assert.AreEqual("Coxa_L", legL.Root.name);

            foreach (Transform joint in armL.Pitch)
                Assert.IsTrue(joint.name.EndsWith("_L") && !joint.name.StartsWith("Hip_") &&
                              !joint.name.StartsWith("Knee_") && !joint.name.StartsWith("Ankle_"),
                    "the arm was assembled out of the leg's bones: " + joint.name);
        }

        // ─────────── the machine ───────────

        [Test]
        public void AMachineWithArmsDoesNotCountThemAsLegs()
        {
            BuildBiped();
            var m = root.AddComponent<ArmedTestMachine>();
            m.Initialise();

            Assert.IsTrue(m.IsReady);
            Assert.AreEqual(2, m.LegCount, "the arms were turned into legs and are being walked on");
            Assert.AreEqual(2, m.ArmCount);
        }

        /// Not walked on means exactly that: no gait slot, no foothold, and nothing in `Step` writes to
        /// the bones. An arm holds whatever pose it was left in until something drives it.
        [Test]
        public void TheGaitNeverPosesAnArm()
        {
            BuildBiped();
            var m = root.AddComponent<ArmedTestMachine>();
            m.Initialise();
            m.SnapToGround();

            var before = new List<Quaternion>();
            foreach (WalkerArm arm in m.Arms)
            {
                before.Add(arm.Rig.Root.localRotation);
                foreach (Transform joint in arm.Rig.Pitch) before.Add(joint.localRotation);
            }

            Vector3 start = m.transform.position;
            for (int i = 0; i < 300; i++)
            {
                m.SetTwist(m.MaxSpeed * 0.5f, 0f);
                m.Step(1f / 60f);
                Physics.SyncTransforms();
            }

            Assert.Greater(Vector2.Distance(new Vector2(start.x, start.z),
                                            new Vector2(m.transform.position.x, m.transform.position.z)),
                           1f, "the machine must actually have walked for this to be saying anything");

            int k = 0;
            foreach (WalkerArm arm in m.Arms)
            {
                AssertUnmoved(before[k++], arm.Rig.Root.localRotation, arm.Rig.Root.name);
                foreach (Transform joint in arm.Rig.Pitch)
                    AssertUnmoved(before[k++], joint.localRotation, joint.name);
            }
        }

        private static void AssertUnmoved(Quaternion before, Quaternion after, string name)
            => Assert.Less(Quaternion.Angle(before, after), 1e-3f,
                           name + " was posed by the gait; arms are not legs");

        // ─────────── the seam ───────────

        /// The acceptance measurement. A commanded world point, solved through the existing
        /// `WalkerLimbSolver` and read back through `WalkerLimbPose` -- so what is checked is where the
        /// POSE puts the hand, not the angles it was asked for.
        [Test]
        public void AnArmSolvesToACommandedWorldTarget()
        {
            BuildBiped();
            var m = root.AddComponent<ArmedTestMachine>();
            m.Initialise();

            Assert.AreEqual(2, m.ArmCount);

            float worst = 0f;
            foreach (WalkerArm arm in m.Arms)
            {
                Vector3 rest = arm.Rig.ContactPoint;
                foreach (Vector3 offset in Offsets())
                {
                    arm.Target = rest + offset;
                    arm.TipDirection = Vector3.down;
                    arm.Solve();

                    Assert.LessOrEqual(arm.ReachFraction, 1f,
                        "the probe target should be inside the arm's reach; it was " +
                        arm.ReachFraction.ToString("F3"));
                    worst = Mathf.Max(worst, Vector3.Distance(arm.Tip, arm.Target));
                }
            }

            Assert.Less(worst, 1e-3f, "worst arm placement error " + worst.ToString("F6") + " m");
        }

        /// The same thing with no component and no gait at all: a limb, a solver and a target. This is
        /// what "keep it a plain class" was for.
        [Test]
        public void AWalkerArmNeedsNoMachine()
        {
            BuildBiped();
            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);
            WalkerRig.Limb rig = limbs.Find(l => l.Role == WalkerRig.LimbRole.Arm);
            Assert.IsNotNull(rig);

            var arm = new WalkerArm(rig, root.transform, ShoulderLimits(rig.Geometry.PitchCount));

            // Untouched, it holds the pose it was modelled in.
            Assert.AreEqual(0f, Vector3.Distance(arm.Target, rig.ContactPoint), 1e-5f);

            Vector3 target = rig.ContactPoint + new Vector3(0.12f, 0.3f, 0.22f);
            arm.Target = target;
            arm.Solve();

            Assert.Less(Vector3.Distance(arm.Tip, target), 1e-3f,
                        "a bare WalkerArm missed its target by " +
                        Vector3.Distance(arm.Tip, target).ToString("F6") + " m");
        }

        /// The tip direction is the other half of the seam: the same target, reached with the hand
        /// pointing somewhere else, is a different pose of the same linkage.
        [Test]
        public void TheTipDirectionChangesThePoseButNotTheContact()
        {
            BuildBiped();
            List<WalkerRig.Limb> limbs = WalkerRig.Build(root.transform, root.transform);
            WalkerRig.Limb rig = limbs.Find(l => l.Role == WalkerRig.LimbRole.Arm);

            var arm = new WalkerArm(rig, root.transform, ShoulderLimits(rig.Geometry.PitchCount));
            Vector3 target = rig.ContactPoint + new Vector3(0.1f, 0.25f, 0.15f);
            arm.Target = target;

            arm.TipDirection = Vector3.down;
            arm.Solve();
            float downAngle = arm.Solved.Pitch[arm.Solved.PitchCount - 1];
            Assert.Less(Vector3.Distance(arm.Tip, target), 1e-3f);

            arm.TipDirection = new Vector3(1f, -1f, 0f);
            arm.Solve();
            float angledAngle = arm.Solved.Pitch[arm.Solved.PitchCount - 1];
            Assert.Less(Vector3.Distance(arm.Tip, target), 1e-3f,
                        "the contact must land on the target whichever way the tip points");

            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(downAngle * Mathf.Rad2Deg,
                                                      angledAngle * Mathf.Rad2Deg)), 1f,
                           "the tip direction was ignored");
        }

        /// Driving the arms is one call, and it is the subclass's to make.
        [Test]
        public void SolveArmsPosesEveryArmAtItsTarget()
        {
            BuildBiped();
            var m = root.AddComponent<ArmedTestMachine>();
            m.Initialise();

            var targets = new List<Vector3>();
            foreach (WalkerArm arm in m.Arms)
            {
                Vector3 target = arm.Rig.ContactPoint + new Vector3(0f, 0.28f, 0.18f);
                arm.Target = target;
                targets.Add(target);
            }

            m.DriveArms();

            for (int i = 0; i < m.ArmCount; i++)
                Assert.Less(Vector3.Distance(m.Arms[i].Tip, targets[i]), 1e-3f);
        }

        private static IEnumerable<Vector3> Offsets()
        {
            yield return Vector3.zero;
            yield return new Vector3(0.15f, 0f, 0f);
            yield return new Vector3(-0.15f, 0f, 0f);
            yield return new Vector3(0f, 0.3f, 0f);
            yield return new Vector3(0f, -0.15f, 0f);
            yield return new Vector3(0f, 0f, 0.25f);
            yield return new Vector3(0f, 0f, -0.25f);
            yield return new Vector3(0.12f, 0.25f, 0.2f);
            yield return new Vector3(-0.12f, 0.25f, -0.2f);
        }
    }
}
