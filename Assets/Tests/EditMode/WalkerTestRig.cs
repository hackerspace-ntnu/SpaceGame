using SpaceGame.Locomotion;
using UnityEngine;

/// Proportions of the real walking station, shared by every limb test so they cannot drift apart.
///
/// These are the middle leg of the six, in world units (the model is instanced at 1.2): the thigh
/// rises up and out, the shin drops down and out — a reverse knee — and the foot hangs straight
/// down. Three pitch segments, which leaves the IK two free links and so the analytic path.
public static class WalkerTestRig
{
    public const float Upper = 6.0465f * 1.2f;
    public const float Lower = 10.9417f * 1.2f;
    public const float Foot = 2.2f * 1.2f;

    public static readonly Vector3 HipPoint = new Vector3(0f, 9.2f * 1.2f, 0f);

    public static WalkerLimbGeometry Geometry()
    {
        float restHip = Mathf.Atan2(3.4f, 5.0f);        // up and outboard
        float restKnee = Mathf.Atan2(-10.4f, 3.4f);     // down and outboard: a reverse knee
        float restAnkle = -Mathf.PI * 0.5f;             // straight down

        var g = new WalkerLimbGeometry
        {
            HasYaw = true,
            YawAxisBody = Vector3.up,
            RestFwdBody = Vector3.right,
            Pitch = new[]
            {
                Segment(Upper, restHip),
                Segment(Lower, restKnee),
                Segment(Foot, restAnkle),
            },
        };
        g.BendSign = Mathf.Sign(
            Mathf.Cos(restHip) * Mathf.Sin(restKnee) - Mathf.Sin(restHip) * Mathf.Cos(restKnee));
        return g;
    }

    /// A limb of `count` equal segments hanging below the hip, for the tests that are about the
    /// chain's LENGTH rather than about this particular leg.
    public static WalkerLimbGeometry Chain(int count, float segmentLength = 4f)
    {
        var pitch = new WalkerLimbSegment[count];
        for (int i = 0; i < count; i++)
        {
            // Fanned slightly so the rest pose spans a plane and the bend direction is defined; a
            // chain modelled dead straight has no elbow for a solve to be seeded from.
            float angle = -Mathf.PI * 0.5f + (i - (count - 1) * 0.5f) * 0.25f;
            pitch[i] = Segment(segmentLength, angle);
        }

        var g = new WalkerLimbGeometry
        {
            HasYaw = true,
            YawAxisBody = Vector3.up,
            RestFwdBody = Vector3.right,
            Pitch = pitch,
        };
        g.BendSign = count >= 2
            ? Mathf.Sign(Mathf.Cos(pitch[0].RestAngle) * Mathf.Sin(pitch[1].RestAngle) -
                         Mathf.Sin(pitch[0].RestAngle) * Mathf.Cos(pitch[1].RestAngle))
            : 1f;
        if (Mathf.Approximately(g.BendSign, 0f)) g.BendSign = 1f;
        return g;
    }

    private static WalkerLimbSegment Segment(float length, float restAngle)
        => new WalkerLimbSegment
        {
            Length = length,
            RestAngle = restAngle,
            RestLocal = Quaternion.identity,
            AxleLocal = Vector3.forward,
        };

    public static WalkerLimbSolver.Frame Frame(Vector3 hip) => new WalkerLimbSolver.Frame
    {
        Hip = hip,
        YawAxis = Vector3.up,
        RestFwd = Vector3.right,
    };

    public static WalkerLimbSolver.Frame Frame() => Frame(HipPoint);

    /// The rest foothold, derived from the geometry rather than typed in, so the tests stay
    /// correct if the proportions above are ever updated.
    public static Vector3 RestFoot()
    {
        WalkerLimbGeometry g = Geometry();
        var r = new WalkerLimbSolver.Result
        {
            Yaw = 0f,
            Pitch = new[] { g.RestHipAngle, g.RestKneeAngle, g.RestAnkleAngle },
        };
        return WalkerLimbSolver.SoleFromResult(Frame(HipPoint), g, r);
    }
}
