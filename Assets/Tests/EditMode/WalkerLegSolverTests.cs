using NUnit.Framework;
using SpaceGame.Walker;
using UnityEngine;

public class WalkerLegSolverTests
{
    // Proportions of the real rig, in world units (the model is instanced at 1.2). The middle
    // leg: the thigh rises up and out, the shin drops down and out, the foot hangs straight down.
    private const float Upper = 6.0465f * 1.2f;
    private const float Lower = 10.9417f * 1.2f;
    private const float Foot = 2.2f * 1.2f;

    private static readonly Vector3 HipPoint = new Vector3(0f, 9.2f * 1.2f, 0f);

    private static WalkerLegGeometry Geometry()
    {
        float restHip = Mathf.Atan2(3.4f, 5.0f);        // up and outboard
        float restKnee = Mathf.Atan2(-10.4f, 3.4f);     // down and outboard: a reverse knee
        float restAnkle = -Mathf.PI * 0.5f;             // straight down

        var g = new WalkerLegGeometry
        {
            UpperLength = Upper,
            LowerLength = Lower,
            FootLength = Foot,
            RestHipAngle = restHip,
            RestKneeAngle = restKnee,
            RestAnkleAngle = restAnkle,
            YawAxisBody = Vector3.up,
            RestFwdBody = Vector3.right,
        };
        g.BendSign = Mathf.Sign(
            Mathf.Cos(restHip) * Mathf.Sin(restKnee) - Mathf.Sin(restHip) * Mathf.Cos(restKnee));
        return g;
    }

    private static WalkerLegSolver.Frame Frame(Vector3 hip) => new WalkerLegSolver.Frame
    {
        Hip = hip,
        YawAxis = Vector3.up,
        RestFwd = Vector3.right,
    };

    /// The rest foothold, derived from the geometry rather than typed in, so the tests stay
    /// correct if the proportions above are ever updated.
    private static Vector3 RestFoot()
    {
        WalkerLegGeometry g = Geometry();
        var r = new WalkerLegSolver.Result
        {
            Yaw = 0f,
            HipAngle = g.RestHipAngle,
            KneeAngle = g.RestKneeAngle,
            AnkleAngle = g.RestAnkleAngle,
        };
        return WalkerLegSolver.SoleFromResult(Frame(HipPoint), g, r);
    }

    // ─────────── the invariant the whole design rests on ───────────

    [Test]
    public void PlantedFoot_IsHeldExactly_WhileTheHullMovesInAnyDirection()
    {
        // This is the property the old three-parallel-hinge rig could not have. A planar leg can
        // only reach points in its own vertical plane, so any hull motion across that plane left
        // the foot unreachable and the solver silently discarded the difference -- measured at
        // 0.57 units of foot slide per unit travelled, on four of the six legs. With a yaw axle
        // the foot is reachable from anywhere in an annulus, so it is simply held.
        WalkerLegGeometry g = Geometry();
        var limits = WalkerLegSolver.Limits.Default;
        Vector3 foot = RestFoot();

        foreach (Vector3 travel in new[]
        {
            new Vector3(2f, 0f, 0f),        // along the leg's own plane
            new Vector3(0f, 0f, 2f),        // straight across it: the old failure case
            new Vector3(-1.5f, 0f, 1.5f),
            new Vector3(1f, 0.8f, -1.2f),   // and with the hull rising over a crest
        })
        {
            WalkerLegSolver.Frame f = Frame(HipPoint + travel);
            WalkerLegSolver.Result r = WalkerLegSolver.Solve(f, g, limits, foot, Vector3.up);
            Vector3 achieved = WalkerLegSolver.SoleFromResult(f, g, r);

            Assert.IsFalse(r.Clamped, $"travel {travel} should be within the leg's travel");
            Assert.Less(Vector3.Distance(achieved, foot), 1e-3f,
                $"the foot must not slide when the hull moves by {travel}");
        }
    }

    [Test]
    public void Solve_RoundTripsThroughForwardKinematics()
    {
        WalkerLegGeometry g = Geometry();
        var limits = WalkerLegSolver.Limits.Default;
        WalkerLegSolver.Frame f = Frame(HipPoint);

        foreach (Vector3 target in new[]
        {
            RestFoot(),
            RestFoot() + new Vector3(1.5f, 0.5f, 2f),
            RestFoot() + new Vector3(-2f, 1.2f, -1f),
        })
        {
            WalkerLegSolver.Result r = WalkerLegSolver.Solve(f, g, limits, target, Vector3.up);
            Vector3 achieved = WalkerLegSolver.SoleFromResult(f, g, r);
            Assert.Less(Vector3.Distance(achieved, target), 1e-3f, $"target {target}");
        }
    }

    // ─────────── the yaw axle ───────────

    [Test]
    public void Yaw_TurnsTheLegTowardATargetOffItsRestPlane()
    {
        WalkerLegGeometry g = Geometry();
        WalkerLegSolver.Frame f = Frame(HipPoint);
        Vector3 offPlane = RestFoot() + new Vector3(0f, 0f, 4f);

        WalkerLegSolver.Result r = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, offPlane, Vector3.up);

        Assert.Greater(Mathf.Abs(r.Yaw), 5f, "a target off the rest plane must be answered by yawing");
        Assert.Less(Vector3.Distance(WalkerLegSolver.SoleFromResult(f, g, r), offPlane), 1e-3f);
    }

    [Test]
    public void Yaw_IsZero_ForATargetOnTheRestPlane()
    {
        WalkerLegGeometry g = Geometry();
        WalkerLegSolver.Frame f = Frame(HipPoint);

        // Straight out along RestFwd, pulled in a little so it is comfortably within reach.
        Vector3 inPlane = HipPoint + Vector3.right * 9f - Vector3.up * 10f;
        WalkerLegSolver.Result r = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, inPlane, Vector3.up);

        Assert.AreEqual(0f, r.Yaw, 1e-3f);
    }

    // ─────────── joint limits ───────────

    [Test]
    public void JointLimits_AreNeverExceeded_HoweverExtremeTheTarget()
    {
        // Nothing prevented unnatural poses before this: only the knee's bend direction was
        // constrained, so the linkage would happily hyperextend to chase a foothold.
        WalkerLegGeometry g = Geometry();
        var limits = new WalkerLegSolver.Limits { Yaw = 30f, Hip = 25f, Knee = 35f, Ankle = 20f };
        WalkerLegSolver.Frame f = Frame(HipPoint);

        foreach (Vector3 target in new[]
        {
            HipPoint + new Vector3(40f, -2f, 30f),      // far outside reach, far off the plane
            HipPoint + new Vector3(-30f, 5f, -25f),     // behind and above
            HipPoint + new Vector3(0.2f, -0.1f, 0f),    // folded right up under the hip
        })
        {
            WalkerLegSolver.Result r = WalkerLegSolver.Solve(f, g, limits, target, Vector3.up);

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
        WalkerLegGeometry g = Geometry();
        WalkerLegSolver.Frame f = Frame(HipPoint);

        WalkerLegSolver.Result far = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, HipPoint + new Vector3(60f, -5f, 0f), Vector3.up);
        Assert.IsTrue(far.Clamped, "a foothold beyond reach must announce itself");
        Assert.Greater(far.ReachFraction, 1f);

        WalkerLegSolver.Result near = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, RestFoot(), Vector3.up);
        Assert.IsFalse(near.Clamped, "the rest pose must not");
        Assert.Less(near.ReachFraction, 1f);
    }

    [Test]
    public void BendSign_KeepsTheKneeBendingTheWayTheMachineWasBuilt()
    {
        WalkerLegGeometry g = Geometry();
        WalkerLegSolver.Frame f = Frame(HipPoint);

        // Sweep the foot through its whole stride; the knee must never pop to the mirror solution.
        for (float x = 6f; x <= 13f; x += 0.5f)
        {
            Vector3 target = HipPoint + new Vector3(x, -10f, 0f);
            WalkerLegSolver.Result r = WalkerLegSolver.Solve(
                f, g, WalkerLegSolver.Limits.Default, target, Vector3.up);

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
        WalkerLegGeometry g = Geometry();
        g.HasRoll = true;
        WalkerLegSolver.Frame f = Frame(HipPoint);

        // A slope tilted about the leg's own forward axis is pure cross-slope: no amount of
        // pitching can address it.
        Vector3 crossSlope = Quaternion.AngleAxis(18f, Vector3.right) * Vector3.up;
        WalkerLegSolver.Result r = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, RestFoot(), crossSlope);

        Assert.AreEqual(18f, Mathf.Abs(r.Roll), 0.5f, "the sole should take the cross-slope angle");
    }

    [Test]
    public void Roll_IsZero_OnGroundTheLegCanAlreadyMeetByPitching()
    {
        WalkerLegGeometry g = Geometry();
        g.HasRoll = true;
        WalkerLegSolver.Frame f = Frame(HipPoint);

        // Tilting about the leg's lateral axle is in-plane: the ankle pitch handles it alone.
        Vector3 alongSlope = Quaternion.AngleAxis(18f, Vector3.forward) * Vector3.up;
        WalkerLegSolver.Result r = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, RestFoot(), alongSlope);

        Assert.AreEqual(0f, r.Roll, 0.5f);
    }

    [Test]
    public void Roll_IsBounded_AndAbsentWhenTheRigHasNoRollJoint()
    {
        WalkerLegSolver.Frame f = Frame(HipPoint);
        Vector3 steep = Quaternion.AngleAxis(70f, Vector3.right) * Vector3.up;
        var limits = WalkerLegSolver.Limits.Default;

        WalkerLegGeometry withRoll = Geometry();
        withRoll.HasRoll = true;
        Assert.LessOrEqual(
            Mathf.Abs(WalkerLegSolver.Solve(f, withRoll, limits, RestFoot(), steep).Roll),
            limits.Roll + 1e-3f);

        // An older model with no Foot_* bone must still solve, just without the roll.
        WalkerLegGeometry noRoll = Geometry();
        Assert.AreEqual(0f, WalkerLegSolver.Solve(f, noRoll, limits, RestFoot(), steep).Roll, 1e-6f);
    }

    [Test]
    public void Roll_DoesNotDisturbWhereTheSoleWasPlaced()
    {
        // The roll pivot sits on the contact point, so tilting the sole must not drag it. If this
        // ever fails, the joint is pivoting at the ankle and every step will scuff sideways.
        WalkerLegGeometry g = Geometry();
        g.HasRoll = true;
        WalkerLegSolver.Frame f = Frame(HipPoint);
        Vector3 target = RestFoot();

        WalkerLegSolver.Result r = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, target,
            Quaternion.AngleAxis(20f, Vector3.right) * Vector3.up);

        Assert.AreNotEqual(0f, r.Roll, "the test slope should have produced some roll");
        Assert.Less(Vector3.Distance(WalkerLegSolver.SoleFromResult(f, g, r), target), 1e-3f);
    }

    [Test]
    public void GroundNormal_PitchesTheFootOntoTheSlope()
    {
        WalkerLegGeometry g = Geometry();
        WalkerLegSolver.Frame f = Frame(HipPoint);
        Vector3 target = RestFoot();

        WalkerLegSolver.Result flat = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, target, Vector3.up);
        WalkerLegSolver.Result slope = WalkerLegSolver.Solve(
            f, g, WalkerLegSolver.Limits.Default, target,
            Quaternion.AngleAxis(20f, Vector3.forward) * Vector3.up);

        Assert.AreNotEqual(flat.AnkleAngle, slope.AnkleAngle,
            "the foot must take the slope's angle, not stab through it");
        // and the sole still lands exactly where it was asked to
        Assert.Less(Vector3.Distance(WalkerLegSolver.SoleFromResult(f, g, slope), target), 1e-3f);
    }

    // ─────────── two-link core ───────────

    [Test]
    public void SolveTwoLink_PlacesTheEndPointOnTheTarget()
    {
        var target = new Vector2(9f, -11f);
        WalkerLegSolver.SolveTwoLink(Upper, Lower, target, 1f,
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
        WalkerLegSolver.SolveTwoLink(Upper, Lower, target, 1f,
                                     out float a1, out float a2, out bool clamped);

        Vector2 end = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * Upper
                    + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * Lower;

        Assert.IsTrue(clamped);
        Assert.AreEqual(Upper + Lower, end.magnitude, 1e-2f, "the leg should be straight out");
        Assert.IsFalse(float.IsNaN(a1) || float.IsNaN(a2));
    }
}
