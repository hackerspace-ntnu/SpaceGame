// The policies, tested as what they are: plain classes with no scene and no rig.
//
// That is the whole return on splitting them out. Every fault asserted here was found the expensive
// way -- by watching a machine walk badly and working backwards -- and each one is now a line of
// arithmetic that a test can pin down in microseconds.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class HipBudgetStrideTests
    {
        /// The ostrich's real proportions: a 1.79 m linkage carrying its hip at 1.40 m.
        private static LegMeasurement Bird() => new LegMeasurement
        {
            MaxReach = 1.79f,
            RestHipHeight = 1.63f,
            LegReach = 1.70f,
        };

        [Test]
        public void StandsTheMachineDownByTheAuthoredFraction()
        {
            var stride = new HipBudgetStride(0.86f);
            LegMeasurement m = Bird();

            Assert.AreEqual(1.63f * 0.86f, stride.WorkingHipHeight(m), 1e-4f);
        }

        [Test]
        public void BudgetIsWhatIsLeftOfTheLegAfterTheHipHeightIsPaidFor()
        {
            var stride = new HipBudgetStride(0.86f, 0.72f);
            LegMeasurement m = Bird();
            m.WorkingHipHeight = stride.WorkingHipHeight(m);

            float expected = 2f * Mathf.Sqrt(1.79f * 1.79f - m.WorkingHipHeight * m.WorkingHipHeight) * 0.72f;
            Assert.AreEqual(expected, stride.StrideLength(m), 1e-4f);
        }

        /// The bug this whole file exists for. Spending the margin on the LEG'S LENGTH and only then
        /// taking the hip height out of what is left is a different sum, and on a bird a catastrophic
        /// one: 0.72 x 1.79 = 1.29 m is LESS than the 1.40 m hip height, the square root goes imaginary
        /// and the budget collapses onto its degenerate floor.
        [Test]
        public void MarginIsNotAppliedToTheLegLengthBeforeTheHipHeightIsTakenOut()
        {
            var stride = new HipBudgetStride(0.86f, 0.72f);
            LegMeasurement m = Bird();
            m.WorkingHipHeight = stride.WorkingHipHeight(m);

            float correct = stride.StrideLength(m);

            // What the wrong order produces, written out so the difference is visible rather than
            // asserted against a magic number: shorten the leg FIRST, then take the hip height out.
            float shortened = 1.79f * 0.72f;
            float underTheRoot = shortened * shortened - m.WorkingHipHeight * m.WorkingHipHeight;
            float wrong = 2f * Mathf.Sqrt(Mathf.Max(shortened * 0.15f, underTheRoot));

            Assert.Less(underTheRoot, 0f,
                "the wrong order should send the square root imaginary; if it does not, this test no " +
                "longer reproduces the fault it is guarding");
            Assert.Greater(correct, wrong * 1.5f,
                "the stride collapsed onto its degenerate floor; the margin is on the wrong quantity");
            Assert.Greater(correct, 1.5f, "a 1.79 m leg standing at 1.40 m has real stride left");
        }

        [Test]
        public void DegenerateFloorKeepsTheRootRealWhenTheHipRidesTooHigh()
        {
            var stride = new HipBudgetStride(1.0f, 0.72f);
            LegMeasurement m = new LegMeasurement { MaxReach = 1.0f, RestHipHeight = 2.0f };
            m.WorkingHipHeight = stride.WorkingHipHeight(m);

            float result = stride.StrideLength(m);
            Assert.IsFalse(float.IsNaN(result), "the budget went imaginary instead of hitting its floor");
            Assert.Greater(result, 0f);
        }
    }

    public class YawArcStrideTests
    {
        [Test]
        public void StrideIsTheChordOfTheCoxaArc()
        {
            var stride = new YawArcStride(40f, 0.85f);
            var m = new LegMeasurement { RestFootRadius = 9f, RestHipHeight = 6f };

            Assert.AreEqual(2f * 9f * Mathf.Sin(40f * 0.85f * Mathf.Deg2Rad), stride.StrideLength(m), 1e-4f);
        }

        [Test]
        public void AStationRidesAtTheHeightItWasModelledAt()
        {
            var stride = new YawArcStride(40f);
            var m = new LegMeasurement { RestHipHeight = 6f };

            Assert.AreEqual(6f, stride.WorkingHipHeight(m), 1e-4f);
        }

        /// Why the bird cannot use this model. Its foot sits almost directly under its hip, so the arc
        /// it sweeps is a few centimetres however long the leg is.
        [Test]
        public void ANearZeroFootRadiusYieldsANearZeroStride()
        {
            var stride = new YawArcStride(45f);
            var m = new LegMeasurement { RestFootRadius = 0.03f, RestHipHeight = 1.6f, MaxReach = 1.79f };

            Assert.Less(stride.StrideLength(m), 0.05f);
        }
    }

    public class WalkerFootholdTests
    {
        /// The clamp is HORIZONTAL. Pulling the 3D vector in instead drags an out-of-range foothold
        /// back along the line to the hip, which LIFTS it off the ground -- the foot then plants in
        /// mid-air, the body rides up to meet it, and the machine levitates.
        [Test]
        public void ClampLeavesTheProbeHeightExactlyWhereItWas()
        {
            var hip = new Vector3(0f, 1.4f, 0f);
            var probe = new Vector3(6f, 0f, 0f);          // far out of reach, on the ground

            Vector3 aim = WalkerFoothold.Clamp(probe, hip, 1.79f, 0.72f);

            Assert.AreEqual(probe.y, aim.y, 1e-5f, "the clamp changed the foothold's height");
            Assert.Less(aim.magnitude, probe.magnitude, "the foothold was not pulled in at all");
        }

        [Test]
        public void AReachableProbeIsLeftAlone()
        {
            var hip = new Vector3(0f, 1.4f, 0f);
            var probe = new Vector3(0.3f, 0f, 0f);

            Vector3 aim = WalkerFoothold.Clamp(probe, hip, 1.79f, 0.72f);

            Assert.Less(Vector3.Distance(aim, probe), 1e-4f);
        }

        /// The margin belongs on the HORIZONTAL RESULT, not on the leg's length.
        [Test]
        public void BudgetIsTheHorizontalRemainderScaledByTheMargin()
        {
            float budget = WalkerFoothold.HorizontalBudget(1.79f, 1.4f, 0f, 0.72f);
            float expected = Mathf.Sqrt(1.79f * 1.79f - 1.4f * 1.4f) * 0.72f;

            Assert.AreEqual(expected, budget, 1e-4f);
            Assert.Greater(budget, 0.7f,
                "a bird whose hip rides at 1.40 m out of 1.79 m still has most of a metre to step into");
        }

        /// The hip's position AT TOUCHDOWN, not at lift-off. Clamping against the current hip drags
        /// every step short, so the leg lands already over-extended and is stepped again next frame.
        [Test]
        public void HipAtTouchdownLeadsTheSwingByTheDistanceTheBodyWillCover()
        {
            Vector3 now = Vector3.zero;
            Vector3 later = WalkerFoothold.HipAtTouchdown(now, new Vector3(0f, 0f, 8f), 0.3f);

            Assert.AreEqual(2.4f, later.z, 1e-4f);
        }

        /// The same lead, on a machine going sideways. This is the whole reason the hip's travel is a
        /// VECTOR rather than a heading and a speed: a crab's nose points north while it goes east, so
        /// there is no yaw the old signature could have been handed that would put the hip in the right
        /// place.
        [Test]
        public void HipAtTouchdownLeadsSidewaysTravelSideways()
        {
            Vector3 later = WalkerFoothold.HipAtTouchdown(Vector3.zero, new Vector3(8f, 0f, 0f), 0.3f);

            Assert.AreEqual(2.4f, later.x, 1e-4f);
            Assert.AreEqual(0f, later.z, 1e-4f);
        }

        [Test]
        public void BudgetNeverGoesImaginary()
        {
            float budget = WalkerFoothold.HorizontalBudget(1.0f, 5f, 0f, 0.72f);

            Assert.IsFalse(float.IsNaN(budget));
            Assert.Greater(budget, 0f);
        }
    }

    public class GaitPatternTests
    {
        private static List<LegMeasurement> Quadruped()
        {
            // front-left, front-right, rear-left, rear-right
            return new List<LegMeasurement>
            {
                Leg(0, -1f, 1f), Leg(1, 1f, 1f), Leg(2, -1f, -1f), Leg(3, 1f, -1f),
            };
        }

        private static LegMeasurement Leg(int index, float x, float z) => new LegMeasurement
        {
            Index = index,
            HomeLocal = new Vector3(x, 0f, z),
            StrideLength = 1f,
            LegReach = 1f,
        };

        // ─────────── trot ───────────

        [Test]
        public void TrotPutsDiagonalPairsOnTheSamePhase()
        {
            var trot = new TrotGait(0.4f, 0.6f);
            trot.Bind(Quadruped());

            float frontLeft = trot.PhaseOffset(0, 1f);
            float frontRight = trot.PhaseOffset(1, 1f);
            float rearLeft = trot.PhaseOffset(2, 1f);
            float rearRight = trot.PhaseOffset(3, 1f);

            Assert.AreEqual(frontLeft, rearRight, 1e-4f, "front-left and rear-right are a diagonal");
            Assert.AreEqual(frontRight, rearLeft, 1e-4f, "front-right and rear-left are a diagonal");
            Assert.AreEqual(0.5f, Mathf.Abs(Mathf.DeltaAngle(frontLeft * 360f, frontRight * 360f)) / 360f,
                            1e-4f, "the two diagonals must be half a cycle apart");
        }

        /// Offsets are a continuous function of runBlend, never a table swapped at a threshold.
        /// Swapping teleports a foot that is in the air; blending walks it over.
        [Test]
        public void TrotOffsetsStayContinuousAcrossTheWalkToTrotBlend()
        {
            var trot = new TrotGait(0.4f, 0.6f);
            trot.Bind(Quadruped());

            for (int leg = 0; leg < 4; leg++)
            {
                float previous = trot.PhaseOffset(leg, 0f);
                for (int step = 1; step <= 50; step++)
                {
                    float current = trot.PhaseOffset(leg, step / 50f);
                    float jump = Mathf.Abs(Mathf.Repeat(current - previous + 0.5f, 1f) - 0.5f);
                    Assert.Less(jump, 0.05f,
                        $"leg {leg} jumped {jump:F3} of a cycle at runBlend {step / 50f:F2}");
                    previous = current;
                }
            }
        }

        // ─────────── ripple ───────────

        [Test]
        public void RippleSpreadsLegsEvenly([Values(3, 4, 6, 8)] int legCount)
        {
            var legs = new List<LegMeasurement>();
            for (int i = 0; i < legCount; i++)
            {
                float angle = i * Mathf.PI * 2f / legCount;
                legs.Add(Leg(i, Mathf.Cos(angle), Mathf.Sin(angle)));
            }

            var ripple = new RippleGait(1, 3);
            ripple.Bind(legs);

            var seen = new HashSet<int>();
            for (int i = 0; i < legCount; i++)
            {
                float offset = ripple.PhaseOffset(i, 0f);
                int slot = Mathf.RoundToInt(offset * legCount);
                Assert.AreEqual(offset * legCount, slot, 1e-3f, "offsets must land on even slots");
                Assert.IsTrue(seen.Add(slot % legCount), "two legs were given the same slot");
            }
        }

        /// Invariant I1. A gate that can never be satisfied is not a gate, it is a stall: a three-legged
        /// machine asked to keep three planted would refuse every request and leave a stranded leg
        /// stranded forever.
        [Test]
        public void RippleStepEarlyGateNeverBlocksWhenItCannotBeSatisfied()
        {
            var ripple = new RippleGait(1, 3);
            ripple.Bind(new List<LegMeasurement> { Leg(0, -1f, 0f), Leg(1, 1f, 0f), Leg(2, 0f, 1f) });

            bool allowed = ripple.MayStepEarly(new StepEarlyRequest
            {
                LegIndex = 0, PlantedCount = 3, LegCount = 3, Unreachable = true,
                StanceTime = 1f, SwingDuration = 0.5f,
            });

            Assert.IsTrue(allowed, "the min-planted gate stalled a machine that could never satisfy it");
        }

        [Test]
        public void RippleStepEarlyGateHoldsSupportWhenItCan()
        {
            var ripple = new RippleGait(1, 3);
            var legs = new List<LegMeasurement>();
            for (int i = 0; i < 6; i++) legs.Add(Leg(i, i < 3 ? -1f : 1f, i));
            ripple.Bind(legs);

            var atTheLimit = new StepEarlyRequest
            {
                LegIndex = 0, PlantedCount = 3, LegCount = 6, Unreachable = true,
                StanceTime = 1f, SwingDuration = 0.5f,
            };
            Assert.IsFalse(ripple.MayStepEarly(atTheLimit), "stepping would drop support to two feet");

            var withRoom = atTheLimit;
            withRoom.PlantedCount = 5;
            Assert.IsTrue(ripple.MayStepEarly(withRoom));
        }

        [Test]
        public void RippleDutyIsNeverZeroEvenBeforeItIsBound()
        {
            // A duty of zero makes the derived top speed zero, which makes SetTwist clamp everything to
            // zero, which stops a clock advanced by distance travelled. Invariant I1.
            var ripple = new RippleGait(2, 3);
            Assert.Greater(ripple.Duty(0f), 0f);
            Assert.Greater(ripple.Duty(1f), 0f);
        }

        // ─────────── alternating ───────────

        [Test]
        public void AlternatingPutsTheTwoLegsOnOppositeHalves()
        {
            var gait = new AlternatingGait(0.44f, 0.62f);
            gait.Bind(new List<LegMeasurement> { Leg(0, -1f, 0f), Leg(1, 1f, 0f) });

            Assert.AreEqual(0f, gait.PhaseOffset(0, 0f), 1e-4f);
            Assert.AreEqual(0.5f, gait.PhaseOffset(1, 0f), 1e-4f);
        }

        [Test]
        public void AlternatingDutyBlendsWalkToRunMonotonically()
        {
            var gait = new AlternatingGait(0.44f, 0.62f);
            gait.Bind(new List<LegMeasurement> { Leg(0, -1f, 0f), Leg(1, 1f, 0f) });

            Assert.AreEqual(0.44f, gait.Duty(0f), 1e-4f);
            Assert.AreEqual(0.62f, gait.Duty(1f), 1e-4f);

            float previous = gait.Duty(0f);
            for (int i = 1; i <= 20; i++)
            {
                float duty = gait.Duty(i / 20f);
                Assert.GreaterOrEqual(duty, previous - 1e-5f, "duty went backwards as speed rose");
                previous = duty;
            }

            Assert.Less(gait.Duty(0f), 0.5f, "a walk must keep a foot down for part of the cycle");
            Assert.Greater(gait.Duty(1f), 0.5f, "a run must have a flight phase");
        }

        [Test]
        public void AlternatingWillNotStepALegThatHasJustLanded()
        {
            var gait = new AlternatingGait(0.44f, 0.62f);
            gait.Bind(new List<LegMeasurement> { Leg(0, -1f, 0f), Leg(1, 1f, 0f) });

            var fresh = new StepEarlyRequest
            {
                LegIndex = 0, Unreachable = true, StanceTime = 0.01f, SwingDuration = 0.3f,
                PlantedCount = 1, LegCount = 2,
            };
            Assert.IsFalse(gait.MayStepEarly(fresh),
                "a leg that re-steps the frame it lands turns the walk into a thrash");

            var settled = fresh;
            settled.StanceTime = 0.5f;
            Assert.IsTrue(gait.MayStepEarly(settled));
        }
    }

    public class BodyMotionTests
    {
        private static BodyFrame Frame(float phase, float duty) => new BodyFrame
        {
            CommandedSpeed = 4f,
            MaxYawRate = 120f,
            RunBlend = 0.5f,
            Effort = 1f,
            Phase = phase,
            Duty = duty,
            RideHeight = 1f,
            LegReach = 2f,
        };

        private static SupportState Support(float height) => new SupportState
        {
            SupportHeight = height, CarryingCount = 1, Load = 1f, LeanX = 0f,
            GroundY = height, HasGround = true,
        };

        /// A cross-slope support, for the tests that are about following it.
        private static SupportState Sloped(float height, float rise)
        {
            var feet = new[]
            {
                new Vector3(-1f, 0f, 1f), new Vector3(1f, rise, 1f),
                new Vector3(-1f, 0f, -1f), new Vector3(1f, rise, -1f),
            };
            WalkerSupportPlane.TryFit(feet, 4, out WalkerSupportPlane plane);

            SupportState s = Support(height);
            s.GroundedCount = 4;
            s.Plane = plane;
            return s;
        }

        [Test]
        public void LevelDeckStaysLevelWhenAskedTo()
        {
            var motion = new LevelDeckBody(slopeFollow: 0f);
            var pose = new BodyPose { Yaw = 37f, Attitude = Quaternion.identity };

            motion.Pose(Frame(0.3f, 0.25f), Sloped(2.5f, 1f), 1f / 60f, ref pose);

            Vector3 up = pose.Attitude * Vector3.up;
            Assert.AreEqual(1f, Vector3.Dot(up, Vector3.up), 1e-4f, "the deck is not level");
            Assert.AreEqual(37f, pose.Attitude.eulerAngles.y, 1e-2f, "the heading was not preserved");
            Assert.AreEqual(Vector3.zero, pose.DisplayOffset);
            Assert.AreEqual(3.5f, pose.PathPos.y, 1e-4f, "support height plus ride height");
        }

        /// The fix for the cross-slope fault. A deck pinned dead level puts every hip at one height
        /// while the feet are at several, so the downhill legs run out of reach and hang in the air.
        [Test]
        public void LevelDeckFollowsPartOfTheSlope()
        {
            var motion = new LevelDeckBody(slopeFollow: 0.6f, maxTilt: 45f, tiltSmooth: 0f);
            var pose = new BodyPose { Yaw = 0f, Attitude = Quaternion.identity };
            SupportState sloped = Sloped(2.5f, 1f);

            motion.Pose(Frame(0.3f, 0.25f), sloped, 1f / 60f, ref pose);

            float slope = Vector3.Angle(sloped.Plane.Normal, Vector3.up);
            float tilt = Vector3.Angle(pose.Attitude * Vector3.up, Vector3.up);

            Assert.Greater(tilt, 1f, "the deck ignored the slope entirely");
            Assert.AreEqual(slope * 0.6f, tilt, 1f, "the deck should take 60% of the ground's tilt");
            Assert.Less(tilt, slope, "a crewed deck must stay flatter than the hillside it is on");
        }

        [Test]
        public void LevelDeckTiltIsCapped()
        {
            var motion = new LevelDeckBody(slopeFollow: 1f, maxTilt: 10f, tiltSmooth: 0f);
            var pose = new BodyPose { Yaw = 0f, Attitude = Quaternion.identity };

            motion.Pose(Frame(0.3f, 0.25f), Sloped(2.5f, 4f), 1f / 60f, ref pose);

            Assert.AreEqual(10f, Vector3.Angle(pose.Attitude * Vector3.up, Vector3.up), 0.5f);
        }

        /// A biped has no plane to fit, so none of this reaches it.
        [Test]
        public void ADeckWithNoSupportPlaneStaysLevel()
        {
            var motion = new LevelDeckBody(slopeFollow: 1f, maxTilt: 45f, tiltSmooth: 0f);
            var pose = new BodyPose { Yaw = 0f, Attitude = Quaternion.identity };

            motion.Pose(Frame(0.3f, 0.25f), Support(2.5f), 1f / 60f, ref pose);

            Assert.AreEqual(0f, Vector3.Angle(pose.Attitude * Vector3.up, Vector3.up), 1e-3f);
        }

        /// The bobbing body follows the ground too, and keeps its gait motion ON TOP of it: a machine
        /// crossing a hillside still leans into its turns, about the slope rather than the horizontal.
        ///
        /// Measured as the DIFFERENCE the slope makes rather than as a total tilt, because the two
        /// rotations are about the same axis here and partly cancel -- a body leaning 20 degrees into a
        /// turn on a 26.6-degree cross-slope that falls the other way ends up 6.6 degrees off level,
        /// which is correct and tells you nothing about whether either part is present.
        [Test]
        public void BobbingBodyFollowsTheSlopeAndKeepsItsOwnLean()
        {
            SupportState sloped = Sloped(0f, 1f);
            float slope = Vector3.Angle(sloped.Plane.Normal, Vector3.up);

            BodyFrame turning = Frame(0.3f, 0.44f);
            turning.CommandedYawRate = 120f;      // full rate against MaxYawRate, so the lean saturates

            Quaternion ignoring = Settle(
                new BobbingBody(0f, 0f, 0f, 20f, 1000f, slopeFollow: 0f), turning, sloped);
            Quaternion following = Settle(
                new BobbingBody(0f, 0f, 0f, 20f, 1000f, slopeFollow: 1f, maxTilt: 45f), turning, sloped);

            Assert.AreEqual(20f, Vector3.Angle(ignoring * Vector3.up, Vector3.up), 0.5f,
                "the turn's own lean should be there whether or not the ground is being followed");

            Assert.AreEqual(slope, Quaternion.Angle(ignoring, following), 0.5f,
                "following the ground should turn the body by exactly the slope, on top of its lean");
        }

        /// Two frames at a smoothing rate that snaps, so the attitude has arrived.
        private static Quaternion Settle(IBodyMotion motion, in BodyFrame f, in SupportState s)
        {
            var pose = new BodyPose { Yaw = 0f, Attitude = Quaternion.identity };
            motion.Pose(f, s, 1f, ref pose);
            motion.Pose(f, s, 1f, ref pose);
            return pose.Attitude;
        }

        /// The bob runs at TWICE the stride frequency: the body dips onto each footfall and rises
        /// through mid-stance. One cycle of the gait must therefore contain two of the bob.
        [Test]
        public void BobRunsAtTwiceTheStrideFrequency()
        {
            var motion = new BobbingBody(0.055f, 0f, 0f, 0f);
            var pose = new BodyPose();

            int minima = 0;
            float previous = 0f, beforeThat = 0f;
            const int samples = 360;

            for (int i = 0; i <= samples; i++)
            {
                motion.Pose(Frame(i / (float)samples, 0.44f), Support(0f), 1f / 60f, ref pose);
                float y = pose.DisplayOffset.y;
                if (i >= 2 && previous < beforeThat && previous < y) minima++;
                beforeThat = previous;
                previous = y;
            }

            Assert.AreEqual(2, minima, "the bob should reach its lowest point twice per gait cycle");
        }

        [Test]
        public void BobIsSizedAgainstLegReachAndScaledByEffort()
        {
            var motion = new BobbingBody(0.05f, 0f, 0f, 0f);
            var pose = new BodyPose();

            BodyFrame working = Frame(0.5f, 0.44f);
            motion.Pose(working, Support(0f), 1f / 60f, ref pose);
            float moving = Mathf.Abs(pose.DisplayOffset.y);

            BodyFrame standing = working;
            standing.Effort = 0f;
            motion.Pose(standing, Support(0f), 1f / 60f, ref pose);

            Assert.Greater(moving, 0f, "a walking machine should bob");
            Assert.AreEqual(0f, pose.DisplayOffset.y, 1e-6f,
                "a standing machine must be still, not idling up and down on the spot");
            Assert.LessOrEqual(moving, 0.05f * 2f + 1e-4f, "the bob is sized against leg reach");
        }

        /// Taken from the LOAD rather than a headcount, which is what makes it a curve that can be used
        /// unfiltered -- and from where the feet actually are, so it cannot lean the wrong way if the
        /// rig's left and right come in swapped.
        [Test]
        public void SwayLeansTowardTheLoadedFoot()
        {
            var motion = new BobbingBody(0f, 0.5f, 0f, 0f);
            var pose = new BodyPose();

            SupportState right = Support(0f);
            right.LeanX = 0.4f;
            motion.Pose(Frame(0.25f, 0.44f), right, 1f / 60f, ref pose);
            float toRight = pose.DisplayOffset.x;

            SupportState left = Support(0f);
            left.LeanX = -0.4f;
            motion.Pose(Frame(0.25f, 0.44f), left, 1f / 60f, ref pose);
            float toLeft = pose.DisplayOffset.x;

            Assert.Greater(toRight, 0f);
            Assert.Less(toLeft, 0f);
        }

        /// A run's flight phase has no load to read the lean from. Holding the last one is what stops
        /// the body snapping upright for the fraction of a second both feet are in the air.
        [Test]
        public void SwayIsHeldThroughAFlightPhaseRatherThanSnappingBack()
        {
            var motion = new BobbingBody(0f, 0.5f, 0f, 0f);
            var pose = new BodyPose();

            SupportState loaded = Support(0f);
            loaded.LeanX = 0.4f;
            motion.Pose(Frame(0.25f, 0.44f), loaded, 1f / 60f, ref pose);
            float held = pose.DisplayOffset.x;

            var airborne = new SupportState { CarryingCount = 0, Load = 0f, HasGround = true };
            motion.Pose(Frame(0.3f, 0.44f), airborne, 1f / 60f, ref pose);

            Assert.AreEqual(held, pose.DisplayOffset.x, 1e-5f);
        }
    }

    public class FootStyleTests
    {
        /// The arc's apex sits at 29% of the swing, not the middle. A sine peaking at mid-swing comes
        /// down at its full vertical speed and the foot stabs at the ground; running the clock through
        /// t(2-t) brings the descent to a stop exactly at touchdown, so the foot is PLACED.
        [Test]
        public void ArticulatedSoleReachesItsApexAtTwentyNinePercent()
        {
            var sole = new ArticulatedSole(18f, 12f);
            var from = Vector3.zero;
            var to = new Vector3(1f, 0f, 0f);

            float bestT = 0f, bestY = float.MinValue;
            for (int i = 0; i <= 1000; i++)
            {
                float t = i / 1000f;
                float y = sole.SwingPoint(from, to, t, 1f).y;
                if (y > bestY) { bestY = y; bestT = t; }
            }

            Assert.AreEqual(0.29f, bestT, 0.02f);
        }

        [Test]
        public void ArticulatedSoleArrivesWithNoVerticalSpeed()
        {
            var sole = new ArticulatedSole(18f, 12f);
            var from = Vector3.zero;
            var to = new Vector3(1f, 0f, 0f);

            float justBefore = sole.SwingPoint(from, to, 0.999f, 1f).y;
            float atTouchdown = sole.SwingPoint(from, to, 1f, 1f).y;

            Assert.AreEqual(0f, atTouchdown, 1e-4f, "the foot must land on its foothold");
            Assert.Less(Mathf.Abs(justBefore - atTouchdown), 1e-3f,
                "the foot is still descending at touchdown; it will stab at the ground");
        }

        [Test]
        public void ArticulatedSoleStartsAndEndsOnItsFootholds()
        {
            var sole = new ArticulatedSole(18f, 12f);
            var from = new Vector3(-1f, 0.2f, 0f);
            var to = new Vector3(1f, 0.5f, 0f);

            Assert.Less(Vector3.Distance(sole.SwingPoint(from, to, 0f, 1f), from), 1e-4f);
            Assert.Less(Vector3.Distance(sole.SwingPoint(from, to, 1f, 1f), to), 1e-4f);
        }

        /// The sole curve is continuous across BOTH handovers -- lift-off and touchdown -- which is what
        /// stops the foot flicking as it leaves or lands.
        [Test]
        public void ArticulatedSoleNormalIsContinuousAcrossBothHandovers()
        {
            var sole = new ArticulatedSole(18f, 12f);
            var frame = new WalkerLimbSolver.Frame
            {
                Hip = Vector3.zero, YawAxis = Vector3.up, RestFwd = Vector3.right,
            };

            var leavingStance = new LegState { GroundNormal = Vector3.up, Swinging = false, Load = 0f };
            var justLifted = new LegState { GroundNormal = Vector3.up, Swinging = true, SwingT = 0f };
            Assert.Less(Vector3.Angle(sole.SoleNormal(leavingStance, frame),
                                      sole.SoleNormal(justLifted, frame)), 1e-2f, "lift-off");

            var aboutToLand = new LegState { GroundNormal = Vector3.up, Swinging = true, SwingT = 1f };
            var justLanded = new LegState { GroundNormal = Vector3.up, Swinging = false, Load = 0f };
            Assert.Less(Vector3.Angle(sole.SoleNormal(aboutToLand, frame),
                                      sole.SoleNormal(justLanded, frame)), 1e-2f, "touchdown");
        }

        [Test]
        public void ArticulatedSoleLiesFlatWhereTheWeightIs()
        {
            var sole = new ArticulatedSole(18f, 12f);
            var frame = new WalkerLimbSolver.Frame
            {
                Hip = Vector3.zero, YawAxis = Vector3.up, RestFwd = Vector3.right,
            };
            var carrying = new LegState { GroundNormal = Vector3.up, Swinging = false, Load = 1f };

            Assert.Less(Vector3.Angle(sole.SoleNormal(carrying, frame), Vector3.up), 1e-3f);
        }

        [Test]
        public void FlatSoleApexStaysAtMidSwing()
        {
            var sole = new FlatSole();
            var from = Vector3.zero;
            var to = new Vector3(1f, 0f, 0f);

            float bestT = 0f, bestY = float.MinValue;
            for (int i = 0; i <= 1000; i++)
            {
                float t = i / 1000f;
                float y = sole.SwingPoint(from, to, t, 1f).y;
                if (y > bestY) { bestY = y; bestT = t; }
            }

            Assert.AreEqual(0.5f, bestT, 1e-2f, "WalkerGaitTests pins this apex; the station shares it");
        }

        [Test]
        public void FlatSoleSetsTheSoleOnTheGroundNormal()
        {
            var sole = new FlatSole();
            var frame = new WalkerLimbSolver.Frame
            {
                Hip = Vector3.zero, YawAxis = Vector3.up, RestFwd = Vector3.right,
            };
            Vector3 slope = Quaternion.AngleAxis(15f, Vector3.forward) * Vector3.up;

            Assert.AreEqual(slope, sole.SoleNormal(new LegState { GroundNormal = slope }, frame));
        }
    }
}
