// The crab walker: four to eight legs, one component, and travel across its own nose.
//
// Everything here is built on a PROCEDURAL rig rather than on the prefab, for the reason the other
// machines' tests are: the numbers under test are geometry, and a synthetic rig states its geometry
// instead of inheriting it from art that may be re-exported tomorrow. The prefab is measured
// separately, through the harness.
//
// The shape of the machine, and why it is that shape:
//
//   * the legs radiate FORE AND AFT on long coxae, so each foot's yaw arc sweeps along the
//     machine's X -- which is the axis it travels on. A crawler's legs stick out to the sides and
//     sweep along Z, which is why the same `YawArcStride` serves both and the same gait cannot.
//   * two rows, spread along X, so the wave has somewhere to march. A row is a set of legs at the
//     same depth; the gait orders within a row and runs the rows in antiphase.
//   * four, six and eight legs are the same builder with a different count. Nothing in the
//     component or the gait is written for a particular number.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;
using SpaceGame.Creatures.Crab;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class CrabLocomotionTests
    {
        private const float Dt = 1f / 60f;
        private const int Frames = 480;
        private const int Settle = 120;

        private GameObject ground;
        private GameObject machine;
        private Mesh cube;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "CrabTestGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(600f, 1f, 600f);

            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);

            Physics.SyncTransforms();
        }

        /// Every clone this fixture makes is destroyed here, including the sloped ground some tests
        /// swap in. A test that leaks a machine into the scene corrupts the NEXT test's ground survey
        /// and every measurement taken after it -- that has already happened once in this suite.
        [TearDown]
        public void TearDown()
        {
            if (machine != null) Object.DestroyImmediate(machine);
            if (ground != null) Object.DestroyImmediate(ground);
            machine = null;
            ground = null;
        }

        // ─────────── one component, four to eight legs ───────────

        [Test]
        public void ItWalksAtFourSixAndEightLegs([Values(4, 6, 8)] int legCount)
        {
            CrabLocomotion m = Crab(legCount);
            m.Initialise();
            m.SnapToGround();

            Assert.AreEqual(legCount, m.LegCount, "the rig did not come back with the legs it was built with");
            Assert.Greater(m.MaxSpeed, 0f,
                legCount + " legs derived a top speed of zero; the gait's duty collapsed (I1)");

            float speed = m.MaxSpeed * 0.5f;
            Vector3 start = m.transform.position;
            Trace t = March(m, new Vector2(speed, 0f), 0f, Frames);
            Vector3 moved = m.transform.position - start;

            Assert.AreEqual(speed * Frames * Dt, moved.x, speed * Frames * Dt * 0.03f,
                legCount + " legs did not cover the commanded distance");
            Assert.Less(t.WorstSlip, 0.01f,
                legCount + " legs: a planted foot moved " + t.WorstSlip.ToString("F5") + " m");
            Assert.LessOrEqual(t.WorstReach, 1f,
                legCount + " legs: worst reach was " + t.WorstReach.ToString("F4"));
            Assert.Greater(t.TotalSteps, legCount,
                legCount + " legs took " + t.TotalSteps + " steps; the clock is not turning");
        }

        /// The support guarantee, at every count. The clock hands each leg a slice of exactly 1/n and
        /// the duty opens exactly `swingSlots` of them at a time, so this is arithmetic rather than
        /// hope -- and the point of asserting it is that the step-early rule is the one thing that could
        /// break it.
        [Test]
        public void ItNeverDropsBelowItsMinimumPlantedCount([Values(4, 6, 8)] int legCount)
        {
            CrabLocomotion m = Crab(legCount);
            m.Initialise();
            m.SnapToGround();

            int expected = CrabWaveGait.MinPlantedFor(legCount);
            Trace t = March(m, new Vector2(m.MaxSpeed * 0.7f, 0f), 0f, Frames);

            Assert.GreaterOrEqual(expected, 3,
                legCount + " legs promises only " + expected + " planted; a crab is statically stable");
            Assert.GreaterOrEqual(t.MinStance, expected,
                legCount + " legs dropped to " + t.MinStance + " planted feet, below the promised " +
                expected);
        }

        /// The same through a turn, which is when the step-early rule actually fires: the legs on the
        /// outside of a pivot are dragged much further than the wave alone would take them. A rule that
        /// respects the gate only while nothing is asking it to is not a gate.
        [Test]
        public void TheMinimumPlantedCountSurvivesATurn()
        {
            CrabLocomotion m = Crab(6);
            m.Initialise();
            m.SnapToGround();

            Trace t = March(m, new Vector2(m.MaxSpeed * 0.6f, 0f), 18f, Frames);

            Assert.GreaterOrEqual(t.MinStance, CrabWaveGait.MinPlantedFor(6),
                "dropped to " + t.MinStance + " planted feet while crabbing through a turn");
        }

        /// Forward is the machine's WEAK axis and this test says so out loud rather than leaving it to
        /// be discovered. The legs radiate fore and aft, so travelling along the nose moves each foot
        /// radially in and out of its own yaw arc instead of along it -- there is real range there, but
        /// it is bounded by the linkage rather than by a circle, and it costs reach.
        ///
        /// It still has to WORK: a crab that could only go sideways would be stuck the moment its path
        /// ran along its nose.
        [Test]
        public void ItAlsoWalksAlongItsNoseAtAWorseReach()
        {
            CrabLocomotion m = Crab(6);
            m.Initialise();
            m.SnapToGround();

            float speed = m.MaxSpeed * 0.5f;
            Vector3 start = m.transform.position;
            Trace t = March(m, new Vector2(0f, speed), 0f, Frames);
            Vector3 moved = m.transform.position - start;

            Assert.AreEqual(speed * Frames * Dt, moved.z, speed * Frames * Dt * 0.03f,
                "it did not cover the commanded distance along its nose");
            Assert.Less(t.WorstSlip, 0.01f, "a planted foot moved " + t.WorstSlip.ToString("F5") + " m");
            Assert.LessOrEqual(t.WorstReach, 1.15f,
                "forward reach was " + t.WorstReach.ToString("F4") + "; the weak axis has got worse");
        }

        // ─────────── travelling sideways ───────────

        [Test]
        public void ItTravelsSidewaysAtTheCommandedSpeed()
        {
            CrabLocomotion m = Crab(6);
            m.Initialise();
            m.SnapToGround();

            float speed = m.MaxSpeed * 0.5f;
            Vector3 start = m.transform.position;
            Trace t = March(m, new Vector2(speed, 0f), 0f, Frames);
            Vector3 moved = m.transform.position - start;

            Assert.AreEqual(speed * Frames * Dt, moved.x, speed * Frames * Dt * 0.02f,
                "the crab did not cover the commanded lateral distance");
            Assert.Less(Mathf.Abs(moved.z), 0.25f,
                "it drifted " + moved.z.ToString("F3") + " m along its nose while going sideways");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, m.transform.eulerAngles.y)), 1f,
                "it turned to face where it was going; a crab does not");
            Assert.Less(t.WorstSlip, 0.01f,
                "a planted foot moved " + t.WorstSlip.ToString("F5") + " m");
            Assert.LessOrEqual(t.WorstReach, 1f, "worst reach was " + t.WorstReach.ToString("F4"));
        }

        /// Both ways, because a wave ordered along one axis is not symmetric by construction -- the
        /// sequence marches with the travel one way round and against it the other. The crawler, whose
        /// wave runs front-to-back, is measurably worse driven backwards than forwards for exactly this
        /// reason, and re-ordering along the travel axis is what this machine's gait is for.
        [Test]
        public void BothSidewaysDirectionsAreEquallyGood()
        {
            CrabLocomotion m = Crab(6);
            m.Initialise();
            m.SnapToGround();
            float speed = m.MaxSpeed * 0.5f;

            Trace plus = March(m, new Vector2(speed, 0f), 0f, Frames);

            Object.DestroyImmediate(machine);
            machine = null;
            CrabLocomotion n = Crab(6);
            n.Initialise();
            n.SnapToGround();
            Trace minus = March(n, new Vector2(-speed, 0f), 0f, Frames);

            // The bar is that BOTH stay inside the linkage. They are not identical and were never going
            // to be -- the wave marches one way round the machine, so one direction runs with it and
            // the other against -- but neither may be the direction that does not work.
            Assert.LessOrEqual(plus.WorstReach, 1f, "+x reach " + plus.WorstReach.ToString("F4"));
            Assert.LessOrEqual(minus.WorstReach, 1f, "-x reach " + minus.WorstReach.ToString("F4"));
            Assert.Less(Mathf.Abs(plus.WorstReach - minus.WorstReach), 0.15f,
                "the two directions differ by " +
                Mathf.Abs(plus.WorstReach - minus.WorstReach).ToString("F4") +
                " of reach; one of them is no longer travelling along the wave");
            Assert.Less(minus.WorstSlip, 0.01f, "-x slip " + minus.WorstSlip.ToString("F5") + " m");
        }

        // ─────────── the shell on a slope ───────────

        /// A cross-slope, for a machine that travels along X, is a slope along Z: the fore and aft rows
        /// end up at different heights and the shell has to follow most of it or the downhill row runs
        /// out of leg. This is the failure the crawler's level deck had, arriving on the other axis.
        [Test]
        public void ItHoldsFourFeetDownAcrossATwentyFiveDegreeCrossSlope()
        {
            Object.DestroyImmediate(ground);
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "CrabTestSlope";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(600f, 1f, 600f);
            ground.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            Physics.SyncTransforms();

            CrabLocomotion m = Crab(6);
            m.Initialise();
            m.SnapToGround();

            Trace t = March(m, new Vector2(m.MaxSpeed * 0.4f, 0f), 0f, Frames);

            Assert.GreaterOrEqual(t.MinStance, 4,
                "dropped to " + t.MinStance + " feet on a 25 degree cross-slope");
            Assert.AreEqual(0, t.FallFrames, "it fell while walking across a slope it was standing on");
        }

        // ─────────── the claws ───────────

        [Test]
        public void TheClawsAreDiscoveredAsArmsAndNotWalkedOn()
        {
            CrabLocomotion m = Crab(6);
            m.Initialise();

            Assert.AreEqual(6, m.LegCount, "a claw was turned into a leg and is being walked on");
            Assert.AreEqual(2, m.ArmCount, "the claws were not discovered as arms");
        }

        [Test]
        public void TheClawsSwayWithTheGaitAndRaiseOnCommand()
        {
            CrabLocomotion m = Crab(6);
            m.Initialise();
            m.SnapToGround();

            // Walking: the claws move, and they move in time with the clock rather than on a timer.
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i < Frames; i++)
            {
                m.SetTwist(new Vector2(m.MaxSpeed * 0.5f, 0f), 0f);
                m.Step(Dt);
                m.PoseClaws(Dt);
                Physics.SyncTransforms();
                if (i <= Settle) continue;
                float local = m.transform.InverseTransformPoint(m.Arms[0].Tip).z;
                lowest = Mathf.Min(lowest, local);
                highest = Mathf.Max(highest, local);
            }

            Assert.Greater(highest - lowest, 1e-3f, "the claws never moved while the machine walked");

            // Raised: the tip ends up meaningfully higher than it was.
            float before = m.transform.InverseTransformPoint(m.Arms[0].Tip).y;
            m.RaiseClaws(true);
            for (int i = 0; i < 120; i++)
            {
                m.SetTwist(Vector2.zero, 0f);
                m.Step(Dt);
                m.PoseClaws(Dt);
            }

            Assert.IsTrue(m.ClawsRaised, "the raise never completed");
            Assert.Greater(m.transform.InverseTransformPoint(m.Arms[0].Tip).y, before + 1e-2f,
                "the claw did not come up when raised");
            Assert.LessOrEqual(m.Arms[0].ReachFraction, 1.05f,
                "the raised claw is out of reach at " + m.Arms[0].ReachFraction.ToString("F3"));
        }

        // ─────────── the gait pattern on its own ───────────

        /// Invariant I1 at the policy. An unbound pattern reporting a duty of zero makes MaxSpeed zero,
        /// which makes SetTwist clamp everything to zero, which stops a clock advanced by distance.
        [Test]
        public void TheGaitNeverReportsADutyOfZero()
        {
            var unbound = new CrabWaveGait();
            Assert.Greater(unbound.Duty(0f), 0f, "an unbound crab gait reported duty 0 (I1)");
            Assert.Greater(unbound.Duty(1f), 0f);

            foreach (int n in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var g = new CrabWaveGait();
                g.Bind(Row(n));
                Assert.Greater(g.Duty(0f), 0f, n + " legs reported duty 0 (I1)");
                Assert.Less(g.Duty(0f), 1f, n + " legs reported every foot airborne all the time");
            }
        }

        /// The other end of I1: a gate that can never be satisfied is a stall, not a gate. A leg
        /// stranded out of reach behind an impossible minimum would stay stranded for good.
        [Test]
        public void AnUnsatisfiableMinimumPlantedGateNeverBlocks()
        {
            var gait = new CrabWaveGait(1, 9);       // nine feet down, on a six-legged machine
            gait.Bind(Row(6));

            bool allowed = gait.MayStepEarly(new StepEarlyRequest
            {
                LegIndex = 0,
                PlantedCount = 6,
                LegCount = 6,
                StanceTime = 1f,
                SwingDuration = 0.2f,
                Unreachable = true,
            });

            Assert.IsTrue(allowed, "an impossible gate refused a stranded leg its step (I1)");
        }

        [Test]
        public void ASatisfiableGateStillRefusesAStepThatWouldBreakSupport()
        {
            var gait = new CrabWaveGait();
            gait.Bind(Row(6));
            Assert.AreEqual(4, gait.MinPlanted, "six legs did not derive four planted");

            Assert.IsFalse(gait.MayStepEarly(Request(planted: 4, legs: 6, unreachable: true)),
                "a step that would leave three feet down was allowed past a gate of four");
            Assert.IsTrue(gait.MayStepEarly(Request(planted: 5, legs: 6, unreachable: true)),
                "a step leaving four feet down was refused by a gate of four");
            Assert.IsFalse(gait.MayStepEarly(Request(planted: 6, legs: 6, unreachable: false)),
                "a leg that could reach its foothold was stepped anyway");
        }

        /// The whole point of the pattern. The offsets have to be a permutation of {0, 1/n, ...} or the
        /// support count stops being arithmetic, and the ordering has to come from where the feet are
        /// rather than from the leg's index.
        [Test]
        public void TheWaveIsOrderedAlongTheTravelAxisAndSpreadEvenly([Values(4, 6, 8)] int legCount)
        {
            var gait = new CrabWaveGait();
            LegMeasurement[] legs = TwoRows(legCount);
            gait.Bind(legs);

            var seen = new bool[legCount];
            for (int i = 0; i < legCount; i++)
            {
                float o = gait.PhaseOffset(i, 0f);
                int slot = Mathf.RoundToInt(o * legCount);
                Assert.AreEqual(o, slot / (float)legCount, 1e-5f,
                    "leg " + i + " sits at " + o.ToString("F4") + ", off the 1/" + legCount + " grid");
                Assert.IsFalse(seen[slot], "two legs share slice " + slot + "; the support count is a lie");
                seen[slot] = true;
            }

            // Within a row, the sequence marches along X: each leg hands off to its neighbour
            // downstream. That is the wave running WITH the travel rather than across it.
            //
            // Allowed to wrap once. The two rows are entered half a row apart -- so the leg lifting
            // alongside a fore-row one is the aft leg furthest from it -- which starts the aft wave in
            // the middle of the row rather than at its end. It is still one wave marching in +X; the
            // cycle is a circle and it simply begins somewhere else on it.
            for (int row = 0; row < 2; row++)
            {
                int wraps = 0;
                float previous = -1f;
                for (int i = row; i < legCount; i += 2)     // TwoRows interleaves fore and aft
                {
                    float o = gait.PhaseOffset(i, 0f);
                    if (previous >= 0f && o < previous) wraps++;
                    previous = o;
                }
                Assert.LessOrEqual(wraps, 1,
                    "row " + row + " steps out of X order in " + wraps +
                    " places; the wave is not marching along the travel axis");
            }
        }

        /// Re-authoring the rig with its legs in a different order must not change the gait. The base
        /// sorts legs by (x, z) before measuring, so anything keyed on the index means something
        /// different the moment the model changes.
        [Test]
        public void TheOrderingComesFromTheFootholdsNotTheLegIndices()
        {
            LegMeasurement[] forwards = TwoRows(6);
            var straight = new CrabWaveGait();
            straight.Bind(forwards);

            var reversed = new LegMeasurement[6];
            for (int i = 0; i < 6; i++)
            {
                reversed[i] = forwards[5 - i];
                reversed[i].Index = i;
            }
            var flipped = new CrabWaveGait();
            flipped.Bind(reversed);

            for (int i = 0; i < 6; i++)
                Assert.AreEqual(straight.PhaseOffset(5 - i, 0f), flipped.PhaseOffset(i, 0f), 1e-5f,
                    "leg " + i + " changed its slice when the rig was listed in another order");
        }

        [Test]
        public void SwingSlotsAndMinPlantedComeFromTheLegCount()
        {
            Assert.AreEqual(1, CrabWaveGait.SwingSlotsFor(4));
            Assert.AreEqual(2, CrabWaveGait.SwingSlotsFor(6));
            Assert.AreEqual(2, CrabWaveGait.SwingSlotsFor(8));
            Assert.AreEqual(3, CrabWaveGait.MinPlantedFor(4));
            Assert.AreEqual(4, CrabWaveGait.MinPlantedFor(6));
            Assert.AreEqual(6, CrabWaveGait.MinPlantedFor(8));

            for (int n = 4; n <= 8; n++)
                Assert.GreaterOrEqual(CrabWaveGait.MinPlantedFor(n), 3,
                    n + " legs would leave fewer than three feet down; a crab is statically stable");
        }

        // ─────────── the harness ───────────

        private struct Trace
        {
            public float WorstSlip;
            public float WorstReach;
            public int MinStance;
            public int TotalSteps;
            public int FallFrames;
        }

        private Trace March(CrabLocomotion m, Vector2 velocityLocal, float yawRate, int frames)
        {
            var t = new Trace { MinStance = int.MaxValue };
            var last = new Vector3[m.LegCount];
            var wasPlanted = new bool[m.LegCount];
            var wasSwinging = new bool[m.LegCount];

            for (int i = 0; i < frames; i++)
            {
                m.SetTwist(velocityLocal, yawRate);
                m.Step(Dt);
                Physics.SyncTransforms();

                for (int leg = 0; leg < m.LegCount; leg++)
                {
                    if (!m.TryGetFoot(leg, out Vector3 foot, out bool swinging)) continue;
                    if (!swinging && wasPlanted[leg] && i > Settle)
                        t.WorstSlip = Mathf.Max(t.WorstSlip, Vector3.Distance(foot, last[leg]));
                    if (swinging && !wasSwinging[leg]) t.TotalSteps++;
                    wasSwinging[leg] = swinging;
                    wasPlanted[leg] = !swinging;
                    last[leg] = foot;
                }

                if (i <= Settle) continue;
                t.WorstReach = Mathf.Max(t.WorstReach, m.LastFrame.WorstReachFraction);
                t.MinStance = Mathf.Min(t.MinStance, m.LastFrame.StanceLegs);
                if (m.IsFalling) t.FallFrames++;
            }

            return t;
        }

        // ─────────── synthetic rigs ───────────

        /// Legs in a single row, for the tests that only care about the pattern's arithmetic.
        private static LegMeasurement[] Row(int count)
        {
            var legs = new LegMeasurement[count];
            for (int i = 0; i < count; i++)
                legs[i] = new LegMeasurement
                {
                    Index = i,
                    HomeLocal = new Vector3(i - (count - 1) * 0.5f, 0f, 0f),
                };
            return legs;
        }

        /// A crab's layout: two rows fore and aft, each spread along the travel axis. Interleaved so
        /// even indices are the fore row and odd the aft, which is what the ordering test reads back.
        private static LegMeasurement[] TwoRows(int count)
        {
            int perRow = count / 2;
            var legs = new LegMeasurement[count];
            for (int i = 0; i < count; i++)
            {
                int k = i / 2;
                float x = (k - (perRow - 1) * 0.5f) * 1.4f;
                legs[i] = new LegMeasurement
                {
                    Index = i,
                    HomeLocal = new Vector3(x, 0f, i % 2 == 0 ? 1.6f : -1.6f),
                };
            }
            return legs;
        }

        private static StepEarlyRequest Request(int planted, int legs, bool unreachable)
            => new StepEarlyRequest
            {
                LegIndex = 0,
                PlantedCount = planted,
                LegCount = legs,
                StanceTime = 1f,
                SwingDuration = 0.2f,
                Unreachable = unreachable,
            };

        /// A crab: `legCount` legs in two rows fore and aft, radiating outward along Z on long coxae so
        /// each foot's yaw arc sweeps along X, plus two claw-arms on the front corners.
        ///
        /// Built procedurally so no art is needed and the geometry is stated rather than inherited. The
        /// proportions match the Blender model: a wide, low shell with the legs well outboard.
        private CrabLocomotion Crab(int legCount)
        {
            machine = new GameObject("SyntheticCrab" + legCount);
            machine.transform.position = new Vector3(0f, 2.4f, 0f);

            int perRow = legCount / 2;
            for (int row = 0; row < 2; row++)
            {
                float sz = row == 0 ? 1f : -1f;
                for (int k = 0; k < perRow; k++)
                {
                    float x = (k - (perRow - 1) * 0.5f) * (5.4f / Mathf.Max(perRow, 1));
                    var attach = new Vector3(x, 0f, sz * 1.10f);
                    // Outward is the row's own direction, fanned a little by how far out along X the
                    // leg sits: the shell is wider than it is deep, so a purely fore/aft splay would
                    // bunch the outer legs' arcs on top of one another.
                    Vector3 outward = new Vector3(x * 0.22f, 0f, sz * 1.6f).normalized;
                    BuildLeg("Coxa_" + row + k, attach, outward, 1.05f, 1.5f);
                }
            }

            // Two claws on the front corners, above the leg plane and pointing forward-outward.
            BuildArm("Arm_L", new Vector3(-1.5f, 0.45f, 1.35f), new Vector3(-0.55f, 0f, 1f).normalized);
            BuildArm("Arm_R", new Vector3(1.5f, 0.45f, 1.35f), new Vector3(0.55f, 0f, 1f).normalized);

            Physics.SyncTransforms();
            return machine.AddComponent<CrabLocomotion>();
        }

        private void BuildLeg(string id, Vector3 attach, Vector3 outward, float upper, float lower)
        {
            Transform coxa = Joint(id, machine.transform, attach, Vector3.up);
            Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

            string tail = id.Substring(id.IndexOf('_'));
            Transform hip = Joint("Hip" + tail, coxa, Vector3.zero, pitch);
            Transform knee = Joint("Knee" + tail, hip,
                outward * upper * 0.85f + Vector3.up * upper * 0.35f, pitch);
            Transform ankle = Joint("Ankle" + tail, knee,
                outward * lower * 0.45f - Vector3.up * lower, pitch);
            Sole(ankle, -Vector3.up * 0.18f);
        }

        /// A claw. Its joints are deliberately NOT named `Arm_*`: `Arm_` is a root prefix, so every
        /// joint carrying it would be claimed as an arm of its own and one claw would import as four.
        /// A shoulder is not a coxa, and an arm's chain is found by walking the hierarchy anyway.
        private void BuildArm(string id, Vector3 attach, Vector3 outward)
        {
            string side = id.Substring(id.IndexOf('_') + 1);
            Transform root = Joint(id, machine.transform, attach, Vector3.up);
            Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

            Transform shoulder = Joint("Shoulder_" + side, root, Vector3.zero, pitch);
            Transform elbow = Joint("Elbow_" + side, shoulder, outward * 0.85f + Vector3.up * 0.1f, pitch);
            Transform wrist = Joint("Wrist_" + side, elbow, outward * 0.75f - Vector3.up * 0.25f, pitch);
            Sole(wrist, outward * 0.30f);
        }

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

        private void Sole(Transform parent, Vector3 offset)
        {
            var go = new GameObject("SoleMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = new Vector3(0.26f, 0.06f, 0.26f);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>();
        }
    }
}
