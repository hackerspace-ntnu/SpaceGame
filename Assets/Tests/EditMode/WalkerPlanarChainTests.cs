// The chain solve, at every link count the architecture claims to support.
//
// The load-bearing assertion is the first one: at two free links this must be the EXISTING analytic
// solve, bit for bit. Both shipping machines take that path, so anything else here changing how they
// walk would be a regression rather than a refactor.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

public class WalkerPlanarChainTests
{
    private static WalkerLimbSolver.Limits Wide(int count)
        => WalkerLimbSolver.Limits.Uniform(180f, 180f, 180f, count);

    /// Invariant I2. The two-free-link path is the existing analytic solve and nothing else.
    [Test]
    public void TwoFreeLinksMatchTheAnalyticSolveExactly()
    {
        WalkerLimbGeometry g = WalkerTestRig.Geometry();
        var angles = new float[3];

        foreach (Vector2 target in new[]
        {
            new Vector2(9f, -11f),
            new Vector2(-4f, -14f),
            new Vector2(14f, -2f),
            new Vector2(500f, 0f),          // far out of reach
        })
        {
            WalkerPlanarChain.Solve(g, Wide(3), 2, target, angles, out bool chainClamped);

            WalkerLimbSolver.SolveTwoLink(g.Pitch[0].Length, g.Pitch[1].Length, target, g.BendSign,
                                          out float a1, out float a2, out bool directClamped);

            Assert.AreEqual(a1, angles[0], 0f, $"first angle, target {target}");
            Assert.AreEqual(a2, angles[1], 0f, $"second angle, target {target}");
            Assert.AreEqual(directClamped, chainClamped, $"clamped flag, target {target}");
        }
    }

    // ─────────── one free link: the stubby two-joint leg ───────────

    [Test]
    public void OneFreeLinkAimsAtTheTarget()
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(2, 4f);
        var angles = new float[2];

        // Exactly one link-length away, so the aim is exact.
        var target = new Vector2(4f * Mathf.Cos(-0.7f), 4f * Mathf.Sin(-0.7f));
        WalkerPlanarChain.Solve(g, Wide(2), 1, target, angles, out bool clamped);

        Vector2 end = WalkerPlanarChain.JointPosition(g, angles, 1);
        Assert.Less(Vector2.Distance(end, target), 1e-3f);
        Assert.IsFalse(clamped);
    }

    [Test]
    public void OneFreeLinkReportsClampedWhenTheTargetIsNotAtItsLength()
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(2, 4f);
        var angles = new float[2];

        WalkerPlanarChain.Solve(g, Wide(2), 1, new Vector2(40f, 0f), angles, out bool clamped);

        Assert.IsTrue(clamped, "a target the link cannot span must announce itself");
        Assert.AreEqual(0f, angles[0], 1e-4f, "the direction is still right: straight along +fwd");
    }

    // ─────────── three or more: CCD ───────────

    [Test]
    public void ThreeOrMoreFreeLinksConvergeInsideTheIterationBudget(
        [Values(4, 5, 6)] int segments)
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(segments, 3f);
        var angles = new float[segments];
        int free = segments - 1;

        // Well inside the chain's total length, so it is genuinely reachable.
        foreach (Vector2 target in new[]
        {
            new Vector2(2f, -6f),
            new Vector2(-3f, -5f),
            new Vector2(5f, -2f),
            new Vector2(0f, -8f),
        })
        {
            WalkerPlanarChain.Solve(g, Wide(segments), free, target, angles, out bool clamped);

            Vector2 end = WalkerPlanarChain.JointPosition(g, angles, free);
            Assert.Less(Vector2.Distance(end, target), 1e-3f,
                $"{free} free links did not reach {target}");
            Assert.IsFalse(clamped, $"target {target} is inside the chain's reach");
        }
    }

    [Test]
    public void CcdRespectsEveryJointLimit()
    {
        const int segments = 5;
        WalkerLimbGeometry g = WalkerTestRig.Chain(segments, 3f);
        var angles = new float[segments];
        int free = segments - 1;

        WalkerLimbSolver.Limits tight = WalkerLimbSolver.Limits.Uniform(40f, 20f, 30f, segments);

        foreach (Vector2 target in new[]
        {
            new Vector2(40f, 10f),          // far outside reach, so every joint is pushed
            new Vector2(-30f, -4f),
            new Vector2(0.2f, -0.1f),       // folded right up under the root
        })
        {
            WalkerPlanarChain.Solve(g, tight, free, target, angles, out _);

            for (int i = 0; i < free; i++)
            {
                float parent = i == 0 ? 0f : angles[i - 1];
                float restParent = i == 0 ? 0f : g.Pitch[i - 1].RestAngle;
                float restRelative = g.Pitch[i].RestAngle - restParent;
                float relative = Mathf.DeltaAngle(
                    0f, (angles[i] - parent - restRelative) * Mathf.Rad2Deg);

                Assert.LessOrEqual(Mathf.Abs(relative), 20f + 1e-2f,
                    $"joint {i} is {relative:F2} deg from rest, target {target}");
            }
        }
    }

    /// The region plain CCD could not solve. A target directly beneath the root at about one
    /// link-length needs the chain folded back through a straight-line singularity, where the fold
    /// direction is ambiguous and an iterative sweep stalls -- it was more than a whole link out
    /// here. The arc seed solves the range and the bearing directly, so the singularity is never
    /// approached from the wrong side.
    [Test]
    public void AFoldedTargetUnderTheRootIsStillSolved([Values(4, 5, 6)] int segments)
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(segments, 3f);
        var angles = new float[segments];
        int free = segments - 1;

        for (float fraction = 0.1f; fraction <= 0.5f; fraction += 0.05f)
        {
            var target = new Vector2(0f, -free * 3f * fraction);

            WalkerPlanarChain.Solve(g, Wide(segments), free, target, angles, out _);

            Vector2 end = WalkerPlanarChain.JointPosition(g, angles, free);
            Assert.Less(Vector2.Distance(end, target), 1e-3f,
                $"{free} free links missed a folded target at {fraction:F2} of span");
        }
    }

    [Test]
    public void UnequalSegmentLengthsStillLand()
    {
        var pitch = new[]
        {
            Segment(5f, -1.2f), Segment(3f, -1.4f), Segment(2f, -1.6f), Segment(4f, -1.8f),
        };
        var g = new WalkerLimbGeometry
        {
            HasYaw = true, YawAxisBody = Vector3.up, RestFwdBody = Vector3.right,
            Pitch = pitch, BendSign = 1f,
        };
        var angles = new float[4];

        foreach (Vector2 target in new[]
        {
            new Vector2(3f, -9f), new Vector2(-5f, -6f), new Vector2(8f, -3f), new Vector2(0f, -4f),
        })
        {
            WalkerPlanarChain.Solve(g, Wide(4), 4, target, angles, out _);
            Vector2 end = WalkerPlanarChain.JointPosition(g, angles, 4);
            Assert.Less(Vector2.Distance(end, target), 1e-3f, $"target {target}");
        }
    }

    private static WalkerLimbSegment Segment(float length, float restAngle)
        => new WalkerLimbSegment
        {
            Length = length, RestAngle = restAngle,
            RestLocal = Quaternion.identity, AxleLocal = Vector3.forward,
        };

    [Test]
    public void CcdIsDeterministic()
    {
        const int segments = 5;
        WalkerLimbGeometry g = WalkerTestRig.Chain(segments, 3f);
        var first = new float[segments];
        var second = new float[segments];
        var target = new Vector2(3f, -5f);

        WalkerPlanarChain.Solve(g, Wide(segments), segments - 1, target, first, out _);
        WalkerPlanarChain.Solve(g, Wide(segments), segments - 1, target, second, out _);

        for (int i = 0; i < segments - 1; i++)
            Assert.AreEqual(first[i], second[i], 0f, $"joint {i} differed between identical solves");
    }

    [Test]
    public void CcdReportsClampedWhenTheTargetIsBeyondTheChain()
    {
        const int segments = 5;
        WalkerLimbGeometry g = WalkerTestRig.Chain(segments, 3f);
        var angles = new float[segments];

        WalkerPlanarChain.Solve(g, Wide(segments), segments - 1, new Vector2(500f, 0f), angles,
                                out bool clamped);

        Assert.IsTrue(clamped);
        for (int i = 0; i < segments - 1; i++)
            Assert.IsFalse(float.IsNaN(angles[i]), $"joint {i} came back NaN");
    }

    // ─────────── the whole limb, at every shape ───────────

    /// A stub leg cannot hit an arbitrary point and is not expected to. Its last segment's
    /// DIRECTION is prescribed by the ground normal, which uses up one of its two in-plane degrees
    /// of freedom, so the contact points it can reach are a curve rather than a region. What it must
    /// do is place the joint it CAN place exactly, and report the shortfall honestly.
    [Test]
    public void AStubLegPlacesTheJointItCanAndReportsTheRest()
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(2, 4f);
        WalkerLimbSolver.Frame f = WalkerTestRig.Frame(Vector3.zero);
        WalkerLimbSolver.Limits limits = WalkerLimbSolver.Limits.Uniform(60f, 170f, 60f, 2);

        var target = new Vector3(1.2f, -6.5f, 0f);
        WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(f, g, limits, target, Vector3.up);

        // The first joint's own segment is exactly its modelled length, whatever the target was.
        WalkerLimbPose.Joints j = WalkerLimbPose.Resolve(f, g, r);
        Assert.AreEqual(4f, Vector3.Distance(j.Points[0], j.Points[1]), 1e-3f);
        Assert.AreEqual(4f, Vector3.Distance(j.Points[1], j.Points[2]), 1e-3f);

        Assert.Greater(r.ReachFraction, 0f);
        Assert.IsFalse(float.IsNaN(r.Pitch[0]));
        Assert.IsFalse(float.IsNaN(r.Pitch[1]));
    }

    [Test]
    public void SolveReachesItsTargetAtEveryLimbLength([Values(3, 4, 5, 6)] int segments)
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(segments, 3f);
        WalkerLimbSolver.Frame f = WalkerTestRig.Frame(Vector3.zero);
        WalkerLimbSolver.Limits limits = WalkerLimbSolver.Limits.Uniform(60f, 170f, 60f, segments);

        // Straight down and a little out: comfortably inside a chain of `segments` x 3 units.
        var target = new Vector3(1.5f, -segments * 1.6f, 0.8f);

        WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(f, g, limits, target, Vector3.up);
        Vector3 achieved = WalkerLimbSolver.SoleFromResult(f, g, r);

        Assert.AreEqual(segments, r.PitchCount, "one angle per pitch joint");
        Assert.Less(Vector3.Distance(achieved, target), 0.05f,
            $"a {segments}-segment limb missed its target by " +
            Vector3.Distance(achieved, target).ToString("F4"));
    }

    /// A limb with no yaw joint is planar: stage 1 is a no-op and the out-of-plane component is
    /// simply unreachable. Invariant I5 -- the gait is told, and does not promise zero slip.
    [Test]
    public void APlanarLimbSolvesInsideItsOwnPlaneAndDoesNotYaw()
    {
        WalkerLimbGeometry g = WalkerTestRig.Chain(3, 4f);
        g.HasYaw = false;
        WalkerLimbSolver.Frame f = WalkerTestRig.Frame(Vector3.zero);

        var offPlane = new Vector3(2f, -8f, 3f);
        WalkerLimbSolver.Result r = WalkerLimbSolver.Solve(
            f, g, WalkerLimbSolver.Limits.Uniform(40f, 170f, 40f, 3), offPlane, Vector3.up);

        Assert.AreEqual(0f, r.Yaw, 1e-6f, "a limb with no yaw joint cannot yaw");

        Vector3 achieved = WalkerLimbSolver.SoleFromResult(f, g, r);
        Assert.AreEqual(0f, achieved.z, 1e-3f, "the solve left the limb's plane");
    }

    /// Nothing in the per-frame path may allocate: the solver reuses the caller's array.
    [Test]
    public void TheRefSolveReusesTheCallersAngleArray()
    {
        WalkerLimbGeometry g = WalkerTestRig.Geometry();
        WalkerLimbSolver.Frame f = WalkerTestRig.Frame();
        var result = new WalkerLimbSolver.Result { Pitch = new float[3] };
        float[] original = result.Pitch;

        for (int i = 0; i < 8; i++)
        {
            WalkerLimbSolver.Solve(f, g, WalkerLimbSolver.Limits.Default,
                                   WalkerTestRig.RestFoot() + new Vector3(i * 0.1f, 0f, 0f),
                                   Vector3.up, ref result);
            Assert.AreSame(original, result.Pitch, "the solve replaced the caller's buffer");
        }
    }
}
