// WHEN each foot leaves the ground, and WHERE it comes down.
//
// The clock itself is WalkerGait, shared with the six-legged station and advanced by distance
// travelled rather than time, so a stationary bird does not step and a slow one takes slow steps.
// Each leg owns a fixed slice of the cycle; `MetachronalOffset(i, 2)` gives the 0 / 0.5 alternation
// a biped wants with no special case.
//
// What lives here is everything the station does differently or not at all: the swing arc a bird
// uses, the foothold search that has to answer for a machine covering more ground per swing than
// its own leg is long, and the load transfer that makes the two feet trade the body's weight
// rather than swap it.
using SpaceGame.Walker;
using UnityEngine;

public partial class OstrichLocomotion
{
    /// A foot must have carried its share for this many swings before it is allowed to step early,
    /// however badly it is over-extended.
    ///
    /// The step-early rule is the only thing in the gait that is not on the clock, so it is also
    /// the only thing that can pull the two legs into step with each other. Once that happens the
    /// bird has both feet in the air half the time and is hopping rather than walking, so it is
    /// held to an emergency measure for genuinely broken ground.
    private const float MinStanceFraction = 1.0f;

    /// Fresh footholds are pulled inside this fraction of the horizontal reach, so a leg that has
    /// just planted is not already at the edge of what it can hold.
    ///
    /// Lower than the walking station's 0.85 on purpose. The station commits a foothold and its
    /// hull creeps; a bird covers most of a leg-length while the foot is in the air, so a
    /// foothold that was comfortable at lift-off is at full stretch on landing. The extra margin
    /// is what stops every step arriving over-extended and triggering the step-early rule.
    private const float FootholdReachFraction = 0.72f;

    /// Fraction of a cycle a foot spends taking the weight on, and the same again handing it back.
    /// This is what makes the lean a curve instead of a square wave, and so what lets it be used
    /// unfiltered.
    private const float LoadTransferFraction = 0.15f;

    private WalkerGait gait;

    private void UpdateGait(float dt)
    {
        float duty = CurrentDuty;
        Vector3 up = Vector3.up;
        Vector3 linear = Quaternion.AngleAxis(currentYaw, Vector3.up) * Vector3.forward * commandedSpeed;
        float yawRate = commandedYawRate * Mathf.Deg2Rad;

        float stance = WalkerGait.StanceDuration(Pace, strideLength);
        float swing = Pace > 1e-3f
            ? Mathf.Min(WalkerGait.SwingDuration(Pace, strideLength, duty), stepDuration * 6f)
            : stepDuration;

        foreach (LegState leg in legs)
        {
            // The phase decides when a swing STARTS; the swing's own timer owns its progress.
            bool inSlice = WalkerGait.IsSwinging(gait.Phase, leg.PhaseOffset, duty, out _);
            bool sliceOpened = inSlice && !leg.WasInSlice;
            leg.WasInSlice = inSlice;

            if (leg.Swinging)
            {
                leg.SwingT += dt / Mathf.Max(leg.SwingSpan, 1e-3f);
                if (leg.SwingT >= 1f)
                {
                    leg.Foot = leg.SwingTo;
                    leg.Swinging = false;
                }
                else
                {
                    leg.Foot = SwingPoint(leg.SwingFrom, leg.SwingTo, leg.SwingT, leg.SwingLift);
                }
                continue;
            }

            // A foot the linkage can no longer honour is stepped early. On the station this was
            // gated on keeping three feet down; there is no such gate here, because a biped has no
            // count of planted feet it can promise in the first place.
            //
            // It IS gated on the foot having been down a little while. Without that, a leg that
            // lands over-extended re-steps on the very next frame, and the bird spends its whole
            // life in the air taking hundreds of tiny steps instead of walking.
            leg.StanceTime += dt;
            bool forced = leg.Unreachable && leg.StanceTime > swing * MinStanceFraction;
            if (!sliceOpened && !forced) continue;
            leg.StanceTime = 0f;

            Vector3 home = body.TransformPoint(leg.HomeLocal);
            Vector3 drift = WalkerGait.FootDrift(
                leg.Rig.Hip.position - body.position, linear, yawRate, up);

            leg.SwingFrom = leg.Foot;
            leg.SwingTo = ResolveFoothold(leg, home, drift, stance, swing);
            leg.SwingLift = SwingLiftFor(leg.SwingFrom, leg.SwingTo);
            leg.SwingSpan = swing;
            leg.SwingT = 0f;
            leg.Swinging = true;
            leg.Unreachable = false;
        }

        UpdateLoad(stance + swing);
    }

    /// How much of the body each foot is carrying. A planted foot takes the load on over the first
    /// slice of its stance and hands it back over the last, so through double support the weight
    /// rolls from the trailing foot onto the leading one instead of jumping between them.
    ///
    /// Time-to-lift comes from the clock rather than from watching for the lift: a leg swings when
    /// its slice opens, so that moment is known in advance and the foot can start unloading before
    /// it. A bird that is not walking has no lift-off coming and simply stands on both.
    private void UpdateLoad(float cycle)
    {
        float ramp = Mathf.Max(LoadTransferFraction * cycle, 1e-4f);
        int swinging = 0;

        foreach (LegState leg in legs)
        {
            if (leg.Swinging) swinging++;
            float toLift = Pace > 1e-3f
                ? Mathf.Repeat(leg.PhaseOffset - gait.Phase, 1f) * cycle
                : float.MaxValue;
            leg.Load = leg.Swinging
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.Min(leg.StanceTime, toLift) / ramp);
        }

        diagnostics.SwingingLegs = swinging;
        diagnostics.StanceLegs = legs.Count - swinging;
        diagnostics.Airborne = swinging == legs.Count;
    }

    /// Where a swinging foot is, `t` running 0 to 1 across the swing.
    ///
    /// The horizontal travel is eased at both ends, which is what makes a foot plant rather than
    /// skid: it arrives at the ground with no speed of its own, and a planted foot is fixed in the
    /// world. The LIFT is where this differs from the walking station's plain sine arc, which peaks
    /// at mid-swing and comes down at its full vertical speed, so the foot stabs at the ground.
    /// Running the arc's clock through t(2-t) puts the apex at 29% and brings the descent to a stop
    /// exactly at touchdown -- the foot snaps up off the ground behind the bird, hangs, and is
    /// placed rather than dropped. It is also what an ostrich does.
    ///
    /// WalkerGait.SwingPoint is deliberately left alone: WalkerGaitTests pins its apex to mid-swing
    /// and the station shares it.
    private static Vector3 SwingPoint(Vector3 from, Vector3 to, float t, float lift)
        => Vector3.Lerp(from, to, t * t * (3f - 2f * t))
           + Vector3.up * (Mathf.Sin(Mathf.PI * t * (2f - t)) * lift);

    /// How high this step has to be lifted: enough to clear whatever is actually under the path,
    /// not a fixed arc that walks through rocks.
    private float SwingLiftFor(Vector3 from, Vector3 to)
    {
        float needed = ground.HighestAlong(from, to, 4) - Mathf.Max(from.y, to.y) + obstacleClearance;
        return Mathf.Max(stepHeight, needed);
    }

    private Vector3 ResolveFoothold(LegState leg, Vector3 home, Vector3 drift, float stance, float swing)
    {
        Vector3 probe = WalkerGait.Foothold(home, drift, stance, swing, strideLength);

        // Clamp against where the hip WILL BE when the foot lands, not where it is now.
        //
        // The foothold is aimed a swing's worth of travel ahead, which on a bird that covers more
        // ground per swing than its own leg is long puts it far out of reach of the hip at
        // lift-off. Clamping against the current hip drags every step short; the leg then lands
        // already over-extended, reports unreachable, and gets stepped again on the next frame --
        // which is a thrash, not a gait.
        Vector3 hipAtTouchdown = leg.Rig.Hip.position
            + Quaternion.AngleAxis(currentYaw, Vector3.up) * Vector3.forward * (commandedSpeed * swing);

        // Bound the step IN THE HORIZONTAL PLANE and only then look for the ground under it.
        //
        // Clamping the 3D vector instead drags an out-of-range foothold along the line back to the
        // hip, which lifts it off the ground; the foot then plants in mid-air a metre up, the body
        // rides up to meet it, and the bird levitates. How far out the leg can reach depends on
        // how much of its length the hip height has already spent.
        //
        // The margin belongs on the HORIZONTAL result, not on the leg's length. Spending it first
        // and then taking the hip height out of what is left is a different sum entirely, and on a
        // bird it is a catastrophic one: the hip rides at 1.40 m out of a 1.79 m leg, so 72% of the
        // leg is 1.29 m, less than the height, the square root goes imaginary and the whole budget
        // collapses onto its degenerate floor. Every step then landed a quarter of a metre ahead of
        // the hip instead of eight tenths -- under the bird rather than out in front of it -- and
        // spent the entire stance dragging backwards to 1.6 m behind, which no 1.79 m leg can hold.
        // That is the trailing, over-stretched leg, and it was arithmetic, not tuning.
        float limit = leg.Rig.Geometry.MaxReach;
        float rise = Mathf.Max(0f, hipAtTouchdown.y - probe.y);
        float horizontal = Mathf.Sqrt(Mathf.Max(limit * limit * 0.04f, limit * limit - rise * rise))
                           * FootholdReachFraction;

        Vector3 flat = probe - hipAtTouchdown;
        flat.y = 0f;
        if (flat.magnitude > horizontal) flat = flat.normalized * horizontal;

        Vector3 aim = new Vector3(hipAtTouchdown.x + flat.x, probe.y, hipAtTouchdown.z + flat.z);

        if (SampleGround(aim, out Vector3 point, out Vector3 normal))
        {
            leg.GroundNormal = normal;
            return point;
        }
        return leg.Foot;        // nothing under us: hold the current foothold
    }
}
