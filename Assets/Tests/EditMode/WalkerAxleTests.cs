using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

public class WalkerAxleTests
{
    // ─────────── measuring a pin ───────────

    [Test]
    public void LongestExtent_FindsAPinsAxisWhicheverWayItWasAuthored()
    {
        // A pin is a cylinder: long along its axis, thin across it. Unity's primitive cylinder is
        // long in local Y, but the artist may have modelled it along any axis, so the measurement
        // must not assume one.
        var alongY = new Bounds(Vector3.zero, new Vector3(0.2f, 2f, 0.2f));
        var alongX = new Bounds(Vector3.zero, new Vector3(2f, 0.2f, 0.2f));
        var alongZ = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 2f));

        Assert.AreEqual(1f,
            Mathf.Abs(Vector3.Dot(WalkerAxle.LongestExtent(Matrix4x4.identity, alongY), Vector3.up)), 1e-4f);
        Assert.AreEqual(1f,
            Mathf.Abs(Vector3.Dot(WalkerAxle.LongestExtent(Matrix4x4.identity, alongX), Vector3.right)), 1e-4f);
        Assert.AreEqual(1f,
            Mathf.Abs(Vector3.Dot(WalkerAxle.LongestExtent(Matrix4x4.identity, alongZ), Vector3.forward)), 1e-4f);
    }

    [Test]
    public void LongestExtent_CarriesTheMeshesOwnRotationAndScale()
    {
        // rig_walker scales its pin meshes (2,2,2) and rotates them with the splayed leg, so the
        // matrix has to be honoured rather than the raw bounds read off.
        var alongY = new Bounds(Vector3.zero, new Vector3(0.2f, 2f, 0.2f));
        Matrix4x4 m = Matrix4x4.TRS(
            new Vector3(3f, -1f, 7f),           // position must not matter: this is a direction
            Quaternion.AngleAxis(90f, Vector3.forward),
            Vector3.one * 2f);

        Vector3 axis = WalkerAxle.LongestExtent(m, alongY);

        // +Y rotated 90° about +Z lands on -X.
        Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(axis, Vector3.right)), 1e-4f);
    }

    [Test]
    public void LongestExtent_NonUniformScaleCanChangeWhichAxisIsLongest()
    {
        // Squashing the long axis hard enough genuinely makes another axis the longest. The
        // measurement should follow the geometry, not the authoring intent.
        var alongY = new Bounds(Vector3.zero, new Vector3(0.2f, 2f, 0.2f));
        Matrix4x4 m = Matrix4x4.Scale(new Vector3(1f, 0.01f, 1f));

        // Y collapses to 0.02 while X and Z stay at 0.2, so the hinge now reads across the pin.
        Vector3 axis = WalkerAxle.LongestExtent(m, alongY);
        Assert.Less(Mathf.Abs(Vector3.Dot(axis, Vector3.up)), 0.01f);
    }

    // ─────────── fallback ───────────

    [Test]
    public void FromRestPose_RecoversThePlaneNormalOfABentLeg()
    {
        // A leg bent in the YZ plane has its hinge along X — the same answer the pins give.
        Vector3 upper = new Vector3(0f, 3.4f, 4.1f);
        Vector3 lower = new Vector3(0f, -12.5f, 3.2f);

        Vector3 axle = WalkerAxle.FromRestPose(upper, lower, Vector3.up);

        Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(axle, Vector3.right)), 1e-3f);
    }

    [Test]
    public void FromRestPose_StraightLegStillYieldsAPerpendicularHinge()
    {
        // Colinear segments span no plane, so any perpendicular axis is a legal hinge. What must
        // not happen is a zero or NaN axle, which would make the solver produce garbage.
        Vector3 upper = new Vector3(0f, -5f, 0f);
        Vector3 lower = new Vector3(0f, -5f, 0f);

        Vector3 axle = WalkerAxle.FromRestPose(upper, lower, Vector3.up);

        Assert.AreEqual(1f, axle.magnitude, 1e-3f);
        Assert.AreEqual(0f, Vector3.Dot(axle, upper.normalized), 1e-3f);
    }

    // ─────────── conventions ───────────

    [Test]
    public void AreParallel_TreatsBothSensesOfAHingeAsTheSameLine()
    {
        var a = new Vector3(0.819f, 0f, -0.574f).normalized;
        Assert.IsTrue(WalkerAxle.AreParallel(a, a));
        Assert.IsTrue(WalkerAxle.AreParallel(a, -a), "a hinge is a line, not a direction");
        Assert.IsFalse(WalkerAxle.AreParallel(a, Vector3.up));
    }

    [Test]
    public void AreParallel_RejectsTheHipKneeMismatchItExistsToCatch()
    {
        // Measured off rig_walker in joint-local space, the hip and knee pins of leg N1 look 43°
        // apart; in BODY space they are identical. If a future re-export really did break that,
        // the leg stops being a planar linkage and the builder has to say so.
        var hip = new Vector3(-0.024f, 0f, -1f).normalized;
        var knee = new Vector3(0.682f, 0f, 0.731f).normalized;

        Assert.IsFalse(WalkerAxle.AreParallel(hip, knee));
    }

    [Test]
    public void MatchSense_AlignsAJointsPinWithTheLegsReference()
    {
        var reference = new Vector3(0.819f, 0f, -0.574f).normalized;

        Assert.AreEqual(reference, WalkerAxle.MatchSense(reference, reference));
        Assert.AreEqual(reference, WalkerAxle.MatchSense(-reference, reference));
    }

    [Test]
    public void PlaneForward_BuildsTheRightHandedFrameTheSolverAssumes()
    {
        // WalkerLimbSolver measures angles as atan2(up, fwd) and applies them as rotations about the
        // axle. That only agrees on a sign if cross(fwd, up) == axle. Every leg angle depends on it.
        foreach (float yaw in new[] { 0f, 35f, -35f, 90f, 143f })
        {
            Vector3 axle = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
            Vector3 fwd = WalkerAxle.PlaneForward(axle, Vector3.up);

            Assert.AreEqual(0f, Vector3.Dot(fwd, axle), 1e-4f, $"fwd must lie in the plane ({yaw}°)");
            Assert.AreEqual(0f, Vector3.Dot(fwd, Vector3.up), 1e-4f, $"fwd must be horizontal ({yaw}°)");
            Assert.Less(Vector3.Distance(Vector3.Cross(fwd, Vector3.up), axle), 1e-4f,
                $"cross(fwd, up) must equal the axle ({yaw}°)");
        }
    }

    [Test]
    public void PlaneForward_RotationAboutTheAxleIncreasesThePlaneAngle()
    {
        // The sign convention the solver relies on, checked directly: turning a segment by +θ about
        // the axle must raise atan2(up, fwd) by θ. Get this backwards and every leg bends the wrong way.
        Vector3 axle = Quaternion.AngleAxis(35f, Vector3.up) * Vector3.right;
        Vector3 fwd = WalkerAxle.PlaneForward(axle, Vector3.up);

        Vector3 turned = Quaternion.AngleAxis(20f, axle) * fwd;
        float angle = Mathf.Atan2(Vector3.Dot(turned, Vector3.up), Vector3.Dot(turned, fwd)) * Mathf.Rad2Deg;

        Assert.AreEqual(20f, angle, 1e-2f);
    }
}
