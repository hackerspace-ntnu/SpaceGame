// Posing the legs onto the footholds the gait chose.
//
// The inverse kinematics itself is WalkerLegSolver, which is analytic: yaw the leg's plane onto the
// target, solve the two-link chain inside it, aim the foot at the ground. There is no iteration and
// nothing to converge, so nothing here can lag -- given a target, the pose is exact this frame.
//
// So this file is only the two things the solver has to be TOLD: where the foot is going, and which
// way the sole should be facing when it gets there. The second is where a bird stops looking like a
// machine on stilts.
using SpaceGame.Walker;
using UnityEngine;

public partial class OstrichLocomotion
{
    private void SolveLegs()
    {
        var limits = new WalkerLegSolver.Limits
        {
            Yaw = yawRange, Hip = hipRange, Knee = kneeRange, Ankle = ankleRange, Roll = rollRange,
        };

        int unreachable = 0;
        float worst = 0f;

        foreach (LegState leg in legs)
        {
            WalkerLegGeometry g = leg.Rig.Geometry;
            var frame = new WalkerLegSolver.Frame
            {
                Hip = leg.Rig.Hip.position,
                YawAxis = body.TransformDirection(g.YawAxisBody).normalized,
                RestFwd = body.TransformDirection(g.RestFwdBody).normalized,
            };

            WalkerLegSolver.Result r =
                WalkerLegSolver.Solve(frame, g, limits, leg.Foot, SoleNormal(leg, frame));
            WalkerLegSolver.Apply(leg.Rig, r);

            // Out of REACH, not merely clamped. Result.Clamped is raised whenever any single joint
            // is sitting on a limit, which on a bird is most of the time and says nothing about
            // whether the foot can be honoured -- reading it as unreachable had the step-early
            // rule firing on two thirds of all frames.
            leg.Unreachable = r.ReachFraction > 1f && !leg.Swinging;

            if (leg.Unreachable) unreachable++;
            worst = Mathf.Max(worst, r.ReachFraction);
        }

        diagnostics.UnreachableLegs = unreachable;
        diagnostics.WorstReachFraction = worst;
    }

    /// The normal the sole is laid against, pitched about the leg's own lateral axle. Positive is
    /// toe down, heel up.
    ///
    /// The solver lays the foot flat on whatever normal it is handed, which is right in the middle
    /// of a stance and wrong at every other moment of the cycle. Asking for a tilted normal rather
    /// than driving the ankle directly means the articulation costs nothing and can break nothing:
    /// the solver places the CONTACT POINT at the foothold and works the ankle out from there, so
    /// the foot pivots about the point it is standing on and a planted foot still cannot slip.
    ///
    /// One curve covers the whole cycle and it is continuous across both handovers, which is what
    /// stops the foot flicking as it lifts or lands:
    ///
    ///   stance -- toe down as the weight arrives and again as it leaves, flat while it is carried
    ///   swing  -- leaves at that same toe-down, lifts the toes to clear the ground it is crossing,
    ///             and returns to it to reach for the landing
    private Vector3 SoleNormal(LegState leg, in WalkerLegSolver.Frame frame)
    {
        float pitch = leg.Swinging
            ? Mathf.Lerp(toeOffAngle, -swingToeAngle, Mathf.Sin(Mathf.PI * leg.SwingT))
            : toeOffAngle * (1f - leg.Load);

        // The axle every pitch joint turns about: across the leg's own plane. Rotating the normal
        // about it keeps the tilt fore-and-aft, which is the only direction the ankle can answer.
        Vector3 axle = Vector3.Cross(frame.YawAxis, frame.RestFwd);
        return axle.sqrMagnitude > 1e-8f
            ? Quaternion.AngleAxis(pitch, axle) * leg.GroundNormal
            : leg.GroundNormal;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !Application.isPlaying || !ready) return;
        foreach (LegState leg in legs)
        {
            Gizmos.color = leg.Swinging ? Color.yellow : (leg.Unreachable ? Color.red : Color.green);
            Gizmos.DrawSphere(leg.Foot, 0.05f);

            Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
            Gizmos.DrawLine(leg.Rig.Hip.position, leg.Foot);

            Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
            Gizmos.DrawWireSphere(body.TransformPoint(leg.HomeLocal), strideLength * 0.5f);
        }
    }
}
