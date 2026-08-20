using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class WalkerLimbSolverTests
    {
        // The rig these are measured against lives in WalkerTestRig, shared with the other leg tests
        // so the proportions cannot drift apart between files.
        private const float Upper = WalkerTestRig.Upper;
        private const float Lower = WalkerTestRig.Lower;

        private static readonly Vector3 HipPoint = WalkerTestRig.HipPoint;

        private static WalkerLimbGeometry Geometry() => WalkerTestRig.Geometry();
        private static WalkerLimbSolver.Frame Frame(Vector3 hip) => WalkerTestRig.Frame(hip);
        private static Vector3 RestFoot() => WalkerTestRig.RestFoot();

        // ─────────── the invariant the whole design rests on ───────────

        [Test]
        public void PlantedFoot_IsHeldExactly_WhileTheHullMovesInAnyDirection()
        {
            // This is the property the old three-parallel-hinge rig could not have. A planar leg can
            // only reach points in its own vertical plane, so any hull motion across that plane left
            // the foot unreachable and the solver silently discarded the difference -- measured at
            // 0.57 units of foot slide per unit travelled, on four of the six legs. With a yaw axle
            // the foot is reachable from anywhere in an annulus, so it is simply held.
            WalkerLimbGeometry g = Geometry();
            var limits = WalkerLimbSolver.Limits.Default;
            Vector3 foot = RestFoot();

            foreach (Vector3 travel in new[]
            {
                new Vector3(2f, 0f, 0f),        // along the leg's own plane
                new Vector3(0f, 0f, 2f),        // straight across it: the old failure case
                new Vector3(-1.5f, 0f, 1.5f),
                new Vector3(1f, 0.8f, -1.2f),   // and with the hull rising over a crest
            })
            {
                WalkerLimbSolver.Frame f = Frame(HipPoint + travel);
                WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(f, g, limits, foot, Vector3.up);
                Vector3 achieved = WalkerLimbSolver.SoleFromResult(f, g, r);

                Assert.IsFalse(r.Clamped, $"travel {travel} should be within the leg's travel");
                Assert.Less(Vector3.Distance(achieved, foot), 1e-3f,
                    $"the foot must not slide when the hull moves by {travel}");
            }
        }

        [Test]
        public void Solve_RoundTripsThroughForwardKinematics()
        {
            WalkerLimbGeometry g = Geometry();
            var limits = WalkerLimbSolver.Limits.Default;
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            foreach (Vector3 target in new[]
            {
                RestFoot(),
                RestFoot() + new Vector3(1.5f, 0.5f, 2f),
                RestFoot() + new Vector3(-2f, 1.2f, -1f),
            })
            {
                WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(f, g, limits, target, Vector3.up);
                Vector3 achieved = WalkerLimbSolver.SoleFromResult(f, g, r);
                Assert.Less(Vector3.Distance(achieved, target), 1e-3f, $"target {target}");
            }
        }

        // ─────────── the yaw axle ───────────

        [Test]
        public void Yaw_TurnsTheLegTowardATargetOffItsRestPlane()
        {
            WalkerLimbGeometry g = Geometry();
            WalkerLimbSolver.Frame f = Frame(HipPoint);
            Vector3 offPlane = RestFoot() + new Vector3(0f, 0f, 4f);

            WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, offPlane, Vector3.up);

            Assert.Greater(Mathf.Abs(r.Yaw), 5f, "a target off the rest plane must be answered by yawing");
            Assert.Less(Vector3.Distance(WalkerLimbSolver.SoleFromResult(f, g, r), offPlane), 1e-3f);
        }

        [Test]
        public void Yaw_IsZero_ForATargetOnTheRestPlane()
        {
            WalkerLimbGeometry g = Geometry();
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            // Straight out along RestFwd, pulled in a little so it is comfortably within reach.
            Vector3 inPlane = HipPoint + Vector3.right * 9f - Vector3.up * 10f;
            WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, inPlane, Vector3.up);

            Assert.AreEqual(0f, r.Yaw, 1e-3f);
        }

        /// The one that made a biped's legs swing sideways.
        ///
        /// A hinge plane is a PLANE, not a half-plane: turning it 180 degrees gives the same plane
        /// back, and the 2D solve inside it takes a negative in-plane coordinate quite happily. Stage
        /// 1 used to measure the yaw as the full azimuth from RestFwd, so a foothold BEHIND the hip
        /// read as "turn the leg 180 degrees" rather than "reach backwards". A splayed-leg machine
        /// never notices -- its targets stay in a narrow cone around the outboard rest direction --
        /// but a leg that hangs straight down has its foothold sweep from ahead of the hip to behind
        /// it every single stride, so it crossed that boundary twice per step. The yaw limit then cut
        /// the 180 down to its stop, the out-of-plane residual was dropped as unreachable, and the
        /// foot was thrown a third of a metre out to the side.
        [Test]
        public void Yaw_ReachesBehindTheHip_RatherThanTurningTheLegAroundToFaceIt()
        {
            // Three equal segments hanging under the hip: a leg, not an outrigger.
            WalkerLimbGeometry g = WalkerTestRig.Chain(3);
            Vector3 hip = new Vector3(0f, 12f, 0f);
            WalkerLimbSolver.Frame f = WalkerTestRig.Frame(hip);
            Vector3 lateral = Vector3.Cross(Vector3.up, f.RestFwd).normalized;

            // Behind the hip, dead in the leg's own plane. Nothing about this target is off-plane, so
            // the yaw has nothing to answer and the pitch chain alone should reach it.
            Vector3 target = hip + f.RestFwd * -4f + Vector3.up * -10f;

            WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, target, Vector3.up);
            Vector3 achieved = WalkerLimbSolver.SoleFromResult(f, g, r);

            Assert.Less(Mathf.Abs(r.Yaw), 1f,
                        "a foothold in the leg's own plane must not turn the coxa, however far back " +
                        "it is; the yaw came out at " + r.Yaw.ToString("F1") + " degrees");
            Assert.Less(Mathf.Abs(Vector3.Dot(achieved - target, lateral)), 1e-3f,
                        "the foot was placed " +
                        Vector3.Dot(achieved - target, lateral).ToString("F3") +
                        " units to the SIDE of a target that is straight behind the hip");
        }

        /// The same property swept through a whole stride, which is what the gait actually asks for.
        /// The interesting part is the crossing: as the foothold passes under the hip the azimuth
        /// swings through 90 degrees and the old solve flipped the coxa from one stop to the other.
        [Test]
        public void Yaw_StaysPutWhileTheFootholdSweepsFromAheadOfTheHipToBehindIt()
        {
            WalkerLimbGeometry g = WalkerTestRig.Chain(3);
            Vector3 hip = new Vector3(0f, 12f, 0f);
            WalkerLimbSolver.Frame f = WalkerTestRig.Frame(hip);
            Vector3 lateral = Vector3.Cross(Vector3.up, f.RestFwd).normalized;

            for (float along = 4f; along >= -4f; along -= 0.5f)
            {
                Vector3 target = hip + f.RestFwd * along + Vector3.up * -10f;
                WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                    f, g, WalkerLimbSolver.Limits.Default, target, Vector3.up);
                Vector3 achieved = WalkerLimbSolver.SoleFromResult(f, g, r);

                Assert.Less(Mathf.Abs(r.Yaw), 1f, "coxa turned at along=" + along);
                Assert.Less(Mathf.Abs(Vector3.Dot(achieved - target, lateral)), 1e-3f,
                            "foot thrown sideways at along=" + along);
            }
        }

        // ─────────── joint limits ───────────

        [Test]
        public void JointLimits_AreNeverExceeded_HoweverExtremeTheTarget()
        {
            // Nothing prevented unnatural poses before this: only the knee's bend direction was
            // constrained, so the linkage would happily hyperextend to chase a foothold.
            WalkerLimbGeometry g = Geometry();
            var limits = new WalkerLimbSolver.Limits { Yaw = 30f, Hip = 25f, Knee = 35f, Ankle = 20f };
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            foreach (Vector3 target in new[]
            {
                HipPoint + new Vector3(40f, -2f, 30f),      // far outside reach, far off the plane
                HipPoint + new Vector3(-30f, 5f, -25f),     // behind and above
                HipPoint + new Vector3(0.2f, -0.1f, 0f),    // folded right up under the hip
            })
            {
                WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(f, g, limits, target, Vector3.up);

                Assert.LessOrEqual(Mathf.Abs(r.Yaw), limits.Yaw + 1e-3f, $"yaw, target {target}");

                float hip = Mathf.DeltaAngle(0f, (r.HipAngle - g.RestHipAngle) * Mathf.Rad2Deg);
                float knee = Mathf.DeltaAngle(
                    0f, (r.KneeAngle - r.HipAngle - (g.RestKneeAngle - g.RestHipAngle)) * Mathf.Rad2Deg);
                float ankle = Mathf.DeltaAngle(
                    0f, (r.AnkleAngle - r.KneeAngle - (g.RestAnkleAngle - g.RestKneeAngle)) * Mathf.Rad2Deg);

                Assert.LessOrEqual(Mathf.Abs(hip), limits.Hip + 1e-3f, $"hip, target {target}");
                Assert.LessOrEqual(Mathf.Abs(knee), limits.Knee + 1e-3f, $"knee, target {target}");
                Assert.LessOrEqual(Mathf.Abs(ankle), limits.Ankle + 1e-3f, $"ankle, target {target}");
            }
        }

        [Test]
        public void UnreachableTarget_ReportsClamped_SoTheGaitCanStepEarly()
        {
            WalkerLimbGeometry g = Geometry();
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            WalkerLimbSolver.Result far = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, HipPoint + new Vector3(60f, -5f, 0f), Vector3.up);
            Assert.IsTrue(far.Clamped, "a foothold beyond reach must announce itself");
            Assert.Greater(far.ReachFraction, 1f);

            WalkerLimbSolver.Result near = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, RestFoot(), Vector3.up);
            Assert.IsFalse(near.Clamped, "the rest pose must not");
            Assert.Less(near.ReachFraction, 1f);
        }

        [Test]
        public void BendSign_KeepsTheKneeBendingTheWayTheMachineWasBuilt()
        {
            WalkerLimbGeometry g = Geometry();
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            // Sweep the foot through its whole stride; the knee must never pop to the mirror solution.
            for (float x = 6f; x <= 13f; x += 0.5f)
            {
                Vector3 target = HipPoint + new Vector3(x, -10f, 0f);
                WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                    f, g, WalkerLimbSolver.Limits.Default, target, Vector3.up);

                float cross = Mathf.Cos(r.HipAngle) * Mathf.Sin(r.KneeAngle)
                            - Mathf.Sin(r.HipAngle) * Mathf.Cos(r.KneeAngle);
                Assert.AreEqual(Mathf.Sign(g.BendSign), Mathf.Sign(cross), $"knee popped at x={x}");
            }
        }

        // ─────────── the sole's roll joint ───────────

        [Test]
        public void Roll_MeetsASlopeTheLegIsWalkingAcross()
        {
            // A stack of parallel pitch hinges can meet a slope it walks UP but not one it walks
            // ACROSS -- measured on the rig at half of all stance frames more than 8 degrees out of
            // true. The roll hinge is what answers the part of the normal lying off the leg's plane.
            WalkerLimbGeometry g = Geometry();
            g.HasRoll = true;
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            // A slope tilted about the leg's own forward axis is pure cross-slope: no amount of
            // pitching can address it.
            Vector3 crossSlope = Quaternion.AngleAxis(18f, Vector3.right) * Vector3.up;
            WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, RestFoot(), crossSlope);

            Assert.AreEqual(18f, Mathf.Abs(r.Roll), 0.5f, "the sole should take the cross-slope angle");
        }

        [Test]
        public void Roll_IsZero_OnGroundTheLegCanAlreadyMeetByPitching()
        {
            WalkerLimbGeometry g = Geometry();
            g.HasRoll = true;
            WalkerLimbSolver.Frame f = Frame(HipPoint);

            // Tilting about the leg's lateral axle is in-plane: the ankle pitch handles it alone.
            Vector3 alongSlope = Quaternion.AngleAxis(18f, Vector3.forward) * Vector3.up;
            WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, RestFoot(), alongSlope);

            Assert.AreEqual(0f, r.Roll, 0.5f);
        }

        [Test]
        public void Roll_IsBounded_AndAbsentWhenTheRigHasNoRollJoint()
        {
            WalkerLimbSolver.Frame f = Frame(HipPoint);
            Vector3 steep = Quaternion.AngleAxis(70f, Vector3.right) * Vector3.up;
            var limits = WalkerLimbSolver.Limits.Default;

            WalkerLimbGeometry withRoll = Geometry();
            withRoll.HasRoll = true;
            Assert.LessOrEqual(
                Mathf.Abs(WalkerLimbSolver.Solve(f, withRoll, limits, RestFoot(), steep).Roll),
                limits.Roll + 1e-3f);

            // An older model with no Foot_* bone must still solve, just without the roll.
            WalkerLimbGeometry noRoll = Geometry();
            Assert.AreEqual(0f, WalkerLimbSolver.Solve(f, noRoll, limits, RestFoot(), steep).Roll, 1e-6f);
        }

        [Test]
        public void Roll_DoesNotDisturbWhereTheSoleWasPlaced()
        {
            // The roll pivot sits on the contact point, so tilting the sole must not drag it. If this
            // ever fails, the joint is pivoting at the ankle and every step will scuff sideways.
            WalkerLimbGeometry g = Geometry();
            g.HasRoll = true;
            WalkerLimbSolver.Frame f = Frame(HipPoint);
            Vector3 target = RestFoot();

            WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, target,
                Quaternion.AngleAxis(20f, Vector3.right) * Vector3.up);

            Assert.AreNotEqual(0f, r.Roll, "the test slope should have produced some roll");
            Assert.Less(Vector3.Distance(WalkerLimbSolver.SoleFromResult(f, g, r), target), 1e-3f);
        }

        [Test]
        public void GroundNormal_PitchesTheFootOntoTheSlope()
        {
            WalkerLimbGeometry g = Geometry();
            WalkerLimbSolver.Frame f = Frame(HipPoint);
            Vector3 target = RestFoot();

            WalkerLimbSolver.Result flat = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, target, Vector3.up);
            WalkerLimbSolver.Result slope = WalkerLimbSolver.Solve(
                f, g, WalkerLimbSolver.Limits.Default, target,
                Quaternion.AngleAxis(20f, Vector3.forward) * Vector3.up);

            Assert.AreNotEqual(flat.AnkleAngle, slope.AnkleAngle,
                "the foot must take the slope's angle, not stab through it");
            // and the sole still lands exactly where it was asked to
            Assert.Less(Vector3.Distance(WalkerLimbSolver.SoleFromResult(f, g, slope), target), 1e-3f);
        }

        // ─────────── two-link core ───────────

        [Test]
        public void SolveTwoLink_PlacesTheEndPointOnTheTarget()
        {
            var target = new Vector2(9f, -11f);
            WalkerLimbSolver.SolveTwoLink(Upper, Lower, target, 1f,
                                         out float a1, out float a2, out bool clamped);

            Vector2 end = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * Upper
                        + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * Lower;

            Assert.IsFalse(clamped);
            Assert.Less(Vector2.Distance(end, target), 1e-3f);
        }

        [Test]
        public void SolveTwoLink_ClampsToTheBoundary_RatherThanProducingNonsense()
        {
            var target = new Vector2(500f, 0f);
            WalkerLimbSolver.SolveTwoLink(Upper, Lower, target, 1f,
                                         out float a1, out float a2, out bool clamped);

            Vector2 end = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * Upper
                        + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * Lower;

            Assert.IsTrue(clamped);
            Assert.AreEqual(Upper + Lower, end.magnitude, 1e-2f, "the leg should be straight out");
            Assert.IsFalse(float.IsNaN(a1) || float.IsNaN(a2));
        }
    }
}
