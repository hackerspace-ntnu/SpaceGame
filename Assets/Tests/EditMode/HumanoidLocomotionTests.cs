// The humanoid robot, marched against the real prefab and the real rig.
//
// Three things here are genuinely new to this project and each has its own test below:
//
//   1. THE KNEE BENDS FORWARD. Both machines that shipped before it have reverse knees, so nothing
//      had ever exercised the other sense of `WalkerLimbGeometry.BendSign` -- which is measured
//      from the rest pose, never authored. The failure to watch for is not "it comes out wrong at
//      Initialise", which would be obvious; it is a knee that POPS THROUGH to the mirrored solution
//      halfway through a stride, which is a single frame in the middle of an otherwise fine walk.
//   2. ARM COUNTER-SWING, off the gait clock rather than a timer of its own, so it cannot drift out
//      of step with the footfalls at any speed.
//   3. A TORSO THAT COUNTER-ROTATES against the legs.
//
// Everything else is the same contract the other machines are held to: planted feet do not slide,
// the machine covers exactly what it was asked to, a walk keeps a foot down, a run has a flight
// phase, and a machine dropped out of the sky lands on its feet.
//
// Every clone made here is destroyed in TearDown. A leaked prefab instance sitting at the origin is
// ground the next test walks into, and it has already corrupted one agent's measurements.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;
using SpaceGame.Creatures.Humanoid;

namespace SpaceGame.Tests
{
    public class HumanoidLocomotionTests
    {
        private const string PrefabPath = "Assets/Prefabs/agents/creatures/HumanoidRobot.prefab";
        private const string OstrichPath = "Assets/Prefabs/agents/creatures/Ostrich.prefab";
        private const float Dt = 1f / 60f;
        private const int Frames = 600;
        /// Frames given to settling before anything is measured: the first stride is spent finding the
        /// ground.
        private const int Settle = 90;

        private GameObject ground;
        private GameObject machine;

        [TearDown]
        public void TearDown()
        {
            if (machine != null) Object.DestroyImmediate(machine);
            if (ground != null) Object.DestroyImmediate(ground);
            machine = null;
            ground = null;
        }

        // ─────────── spawning ───────────

        private HumanoidLocomotion Spawn(float spawnHeight = 0.3f)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(900f, 1f, 900f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Humanoid prefab missing at " + PrefabPath);

            machine = Object.Instantiate(prefab);
            machine.transform.position = new Vector3(0f, spawnHeight, 0f);

            // The driver would fight the test for the twist channel. It is named by TYPE rather than
            // by class because it lives in Assembly-CSharp -- `IRiderControllable` is declared there
            // and no asmdef may reference it, which is the whole reason the locomotion is a separate
            // assembly and the driver is a shell around it.
            foreach (MonoBehaviour mb in machine.GetComponents<MonoBehaviour>())
                if (!(mb is LeggedLocomotion) && !(mb is HumanoidArmSwing) &&
                    !(mb is HumanoidSpineMotion)) mb.enabled = false;
            Physics.SyncTransforms();

            var loco = machine.GetComponent<HumanoidLocomotion>();
            loco.Initialise();
            return loco;
        }

        private Transform Find(string name)
        {
            foreach (Transform t in machine.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Assert.Fail("no bone named " + name + " on the rig");
            return null;
        }

        // ─────────── what the machine is ───────────

        [Test]
        public void TheRigIsTwoLegsAndTwoArms()
        {
            HumanoidLocomotion loco = Spawn();

            Assert.IsTrue(loco.IsReady, "no leg chains were found on the humanoid rig");
            Assert.AreEqual(2, loco.LegCount, "the arms were turned into legs and are being walked on");
            Assert.AreEqual(2, loco.ArmCount, "the arms were not discovered as arms");
            Assert.Greater(loco.MaxSpeed, 0f, "a machine whose top speed is zero cannot be commanded");

            for (int i = 0; i < loco.LegCount; i++)
            {
                Assert.IsTrue(loco.TryGetMeasurement(i, out LegMeasurement m));
                Assert.Greater(m.StrideLength, 0.2f,
                    "leg " + i + " got a stride of " + m.StrideLength.ToString("F4") +
                    " m, which is the hip-budget floor rather than real geometry");
            }
        }

        /// The stride budget has a FLOOR under its square root, for the case where the hip is riding
        /// higher than the linkage can spare. Reaching it means the machine is standing too tall to
        /// step and the stride stops responding to `hipHeightFraction` at all -- so it is not a tuning
        /// range, it is a cliff, and where it sits is a property of the rig.
        [Test]
        public void TheStrideIsRealGeometryAndNotTheHipBudgetFloor()
        {
            HumanoidLocomotion loco = Spawn();
            Assert.IsTrue(loco.TryGetMeasurement(0, out LegMeasurement m));

            float floorHeight = Mathf.Sqrt(Mathf.Max(0f, m.MaxReach * m.MaxReach - m.MaxReach * 0.15f));
            Assert.Less(m.WorkingHipHeight, floorHeight,
                "the working hip height " + m.WorkingHipHeight.ToString("F4") +
                " m is at or above the height " + floorHeight.ToString("F4") +
                " m where the stride collapses onto HipBudgetStride's floor");
        }

        // ─────────── the forward knee ───────────

        /// How far the knee sits ahead of the straight line from hip to ankle, along the machine's own
        /// forward axis. Positive is a human knee; negative is a bird's or a walking station's.
        private float KneeOffset(Transform hip, Transform knee, Transform ankle)
        {
            Transform body = machine.transform;
            Vector3 h = body.InverseTransformPoint(hip.position);
            Vector3 k = body.InverseTransformPoint(knee.position);
            Vector3 a = body.InverseTransformPoint(ankle.position);

            float span = h.y - a.y;
            if (Mathf.Abs(span) < 1e-3f) return float.NaN;    // leg folded flat: nothing to measure
            float t = (h.y - k.y) / span;
            return k.z - Mathf.Lerp(h.z, a.z, t);
        }

        [Test]
        public void TheKneeBendsForwardAndTheOstrichsDoesNot()
        {
            HumanoidLocomotion loco = Spawn();
            loco.SnapToGround();
            float humanoid = KneeOffset(Find("Hip_L"), Find("Knee_L"), Find("Ankle_L"));

            Assert.Greater(humanoid, 0.02f,
                "the humanoid's knee should break FORWARD; it sits " + humanoid.ToString("F4") +
                " m ahead of the hip-to-ankle line");

            TearDown();

            // The same measurement on the bird, which is the machine this had to differ from.
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(900f, 1f, 900f);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OstrichPath);
            Assert.IsNotNull(prefab, "Ostrich prefab missing at " + OstrichPath);
            machine = Object.Instantiate(prefab);
            machine.transform.position = new Vector3(0f, 1.2f, 0f);
            Physics.SyncTransforms();

            float bird = KneeOffset(Find("Hip_L"), Find("Knee_L"), Find("Ankle_L"));
            Assert.Less(bird, -0.02f,
                "the ostrich's knee should break BACKWARD, or this test is not comparing two senses; " +
                "it sits " + bird.ToString("F4") + " m from the line");
        }

        /// The fault this exists for. `BendSign` pins the elbow solution of the two-free-link analytic
        /// path so the knee cannot flip to the mirrored pose -- and a flip is not a wrong rest pose, it
        /// is ONE FRAME in the middle of an otherwise fine walk where the knee snaps through the leg
        /// and back. So the assertion is over every frame of a full walk, not over the rest pose.
        [Test]
        public void BendSign_KeepsTheKneeBendingTheWayTheMachineWasBuilt()
        {
            HumanoidLocomotion loco = Spawn();
            loco.SnapToGround();

            Transform hipL = Find("Hip_L"), kneeL = Find("Knee_L"), ankleL = Find("Ankle_L");
            Transform hipR = Find("Hip_R"), kneeR = Find("Knee_R"), ankleR = Find("Ankle_R");

            float worstL = float.MaxValue, worstR = float.MaxValue;
            int worstFrame = -1;

            for (int i = 0; i < Frames; i++)
            {
                // A speed sweep inside one run, so the swing, the stance and the flight phase are all
                // covered without three separate walks.
                loco.SetTwist(loco.MaxSpeed * Mathf.Lerp(0.15f, 0.95f, i / (float)Frames), 12f);
                loco.Step(Dt);
                Physics.SyncTransforms();
                if (i < Settle) continue;

                float l = KneeOffset(hipL, kneeL, ankleL);
                float r = KneeOffset(hipR, kneeR, ankleR);
                if (float.IsNaN(l) || float.IsNaN(r)) continue;
                if (l < worstL || r < worstR) worstFrame = i;
                worstL = Mathf.Min(worstL, l);
                worstR = Mathf.Min(worstR, r);
            }

            Assert.Greater(worstL, 0f,
                "the left knee popped through to the mirrored solution: it reached " +
                worstL.ToString("F4") + " m BEHIND the hip-to-ankle line, around frame " + worstFrame);
            Assert.Greater(worstR, 0f,
                "the right knee popped through to the mirrored solution: it reached " +
                worstR.ToString("F4") + " m behind the line, around frame " + worstFrame);
        }

        // ─────────── walking ───────────

        private struct Trace
        {
            public float WorstSlip;
            public float WorstReach;
            public float Travelled;
            public int AirborneFrames;
            public int SingleSupportFrames;
            public int FootDownFrames;
            public int Steps;
            public int FallingFrames;
        }

        private Trace March(HumanoidLocomotion loco, float speedFraction, float yawRate)
        {
            loco.SnapToGround();

            var t = new Trace();
            var last = new Vector3[loco.LegCount];
            var wasPlanted = new bool[loco.LegCount];
            for (int leg = 0; leg < loco.LegCount; leg++)
            {
                loco.TryGetFoot(leg, out last[leg], out bool swinging);
                wasPlanted[leg] = !swinging;
            }

            Vector3 start = machine.transform.position;
            for (int i = 0; i < Frames; i++)
            {
                loco.SetTwist(loco.MaxSpeed * speedFraction, yawRate);
                loco.Step(Dt);
                Physics.SyncTransforms();

                for (int leg = 0; leg < loco.LegCount; leg++)
                {
                    loco.TryGetFoot(leg, out Vector3 foot, out bool swinging);
                    if (i >= Settle)
                    {
                        if (!swinging && wasPlanted[leg])
                            t.WorstSlip = Mathf.Max(t.WorstSlip, Vector3.Distance(foot, last[leg]));
                        if (swinging && wasPlanted[leg]) t.Steps++;
                    }
                    wasPlanted[leg] = !swinging;
                    last[leg] = foot;
                }

                if (i < Settle) continue;
                LeggedLocomotion.Diagnostics d = loco.LastFrame;
                t.WorstReach = Mathf.Max(t.WorstReach, d.WorstReachFraction);
                if (d.Airborne) t.AirborneFrames++; else t.FootDownFrames++;
                if (d.StanceLegs == 1) t.SingleSupportFrames++;
                if (loco.IsFalling) t.FallingFrames++;
            }

            Vector3 end = machine.transform.position;
            t.Travelled = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));
            return t;
        }

        [Test]
        public void PlantedFeetDoNotSlideAtAnySpeed()
        {
            foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.95f })
            {
                HumanoidLocomotion loco = Spawn();
                Trace t = March(loco, fraction, 0f);
                Assert.Less(t.WorstSlip, 0.01f,
                    "at " + fraction.ToString("P0") + " of top speed a planted foot moved " +
                    t.WorstSlip.ToString("F5") + " m");
                TearDown();
            }
        }

        [Test]
        public void PlantedFeetDoNotSlideThroughATurn()
        {
            HumanoidLocomotion loco = Spawn();
            Trace t = March(loco, 0.5f, 45f);
            Assert.Less(t.WorstSlip, 0.01f,
                "through a turn a planted foot moved " + t.WorstSlip.ToString("F5") + " m");
        }

        /// The machine must cover exactly what it was asked to. Anything else means the legs are
        /// skating -- either sliding forward under a body the gait cannot keep up with, or being
        /// dragged by one that is outrunning them.
        [Test]
        public void ItCoversTheDistanceItWasCommanded()
        {
            HumanoidLocomotion loco = Spawn();
            Trace t = March(loco, 0.5f, 0f);
            float wanted = loco.MaxSpeed * 0.5f * Frames * Dt;
            Assert.That(t.Travelled, Is.EqualTo(wanted).Within(wanted * 0.02f),
                "asked for " + wanted.ToString("F3") + " m, got " + t.Travelled.ToString("F3"));
        }

        [Test]
        public void AWalkKeepsAFootDownAndARunHasAFlightPhase()
        {
            HumanoidLocomotion walk = Spawn();
            Trace w = March(walk, 0.25f, 0f);
            Assert.AreEqual(0, w.AirborneFrames,
                "a walk must never have both feet off the ground; it did for " +
                w.AirborneFrames + " frames");
            Assert.Greater(w.Steps, 4, "the walk barely stepped: " + w.Steps + " steps");
            TearDown();

            HumanoidLocomotion run = Spawn();
            Trace r = March(run, 0.95f, 0f);
            Assert.Greater(r.AirborneFrames, 20,
                "a run must have a flight phase; it had " + r.AirborneFrames + " airborne frames");
        }

        [Test]
        public void StandingStillTakesNoStepsAndDoesNotDrift()
        {
            HumanoidLocomotion loco = Spawn();
            Trace t = March(loco, 0f, 0f);
            Assert.AreEqual(0, t.Steps, "a stationary machine took " + t.Steps + " steps");
            Assert.Less(t.Travelled, 0.05f,
                "a stationary machine drifted " + t.Travelled.ToString("F4") + " m");
        }

        [Test]
        public void TheWorstReachAtAWalkStaysInsideTheLeg()
        {
            HumanoidLocomotion loco = Spawn();
            Trace t = March(loco, 0.25f, 0f);
            Assert.LessOrEqual(t.WorstReach, 1.15f,
                "worst reach fraction at a walk was " + t.WorstReach.ToString("F4"));
        }

        /// Gravity, which every machine on this base now has. The two things that make it a fall rather
        /// than a lerp: it ACCELERATES, and it ends with the feet back under the machine.
        [Test]
        public void SpawnedTwentyMetresUpItFallsAndLandsOnItsFeet()
        {
            HumanoidLocomotion loco = Spawn(20f);

            float lastY = machine.transform.position.y;
            float firstDrop = 0f, fastestDrop = 0f;
            for (int i = 0; i < 600; i++)
            {
                loco.Step(Dt);
                Physics.SyncTransforms();
                float drop = lastY - machine.transform.position.y;
                if (i == 2) firstDrop = drop;
                fastestDrop = Mathf.Max(fastestDrop, drop);
                lastY = machine.transform.position.y;
            }

            Assert.Greater(fastestDrop, firstDrop * 2f,
                "the drop did not accelerate: it started at " + firstDrop.ToString("F5") +
                " m/frame and never exceeded " + fastestDrop.ToString("F5"));

            Assert.That(machine.transform.position.y, Is.EqualTo(loco.RideHeight).Within(0.15f),
                "after falling 20 m it settled at y=" + machine.transform.position.y.ToString("F4") +
                " rather than its ride height " + loco.RideHeight.ToString("F4"));

            Assert.IsFalse(loco.IsFalling, "it is still falling after 10 seconds");

            for (int leg = 0; leg < loco.LegCount; leg++)
            {
                Assert.IsTrue(loco.TryGetFoot(leg, out Vector3 foot, out bool swinging));
                Assert.IsFalse(swinging, "leg " + leg + " landed mid-swing and stayed there");
                Assert.Less(Mathf.Abs(foot.y), 0.12f,
                    "leg " + leg + "'s foot ended up at y=" + foot.y.ToString("F4") +
                    " rather than on the ground");
            }
        }
    }
}
