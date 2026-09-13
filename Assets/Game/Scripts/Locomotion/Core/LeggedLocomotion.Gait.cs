// WHEN each foot leaves the ground, and WHERE it comes down.
//
// The clock is WalkerGait, advanced by DISTANCE TRAVELLED rather than by time. Three things fall
// out of that and together they are most of what makes the machine read as a machine: a stationary
// walker does not step, a slow walker takes slow steps, and top speed is derived rather than chosen
// -- a foot must return to its start within its stance, so the fastest honest speed is
// stride / stance-duration.
//
// Each leg owns a slice of the cycle and swings during that slice. There is nothing to negotiate
// between legs, so there is no deadlock to detect and no escape hatch to trip. The ONE adaptive
// rule is the step-early request, which is what makes broken ground work -- the clock alone cannot
// know that terrain has pulled a foothold out of reach -- and it is the gait pattern's call.
//
// Turning needs no special case. A foot drifts backwards through the body frame at -(v + w x r),
// which already accounts for rotation, so a leg on the outside of a turn is handed a longer stride
// than one on the inside without anything here knowing about turns.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public abstract partial class LeggedLocomotion
    {
        /// Fresh footholds are pulled inside this fraction of the horizontal reach, so a leg that
        /// has just planted is not already at the edge of what it can hold.
        ///
        /// A machine that covers most of a leg-length while the foot is in the air lands on a
        /// foothold that was comfortable at lift-off and is at full stretch on arrival. This margin
        /// is what stops every step arriving over-extended and triggering the step-early rule.
        protected virtual float FootholdReachFraction => 0.72f;

        /// Fraction of a cycle a foot spends taking the weight on, and the same again handing it
        /// back. This is what makes the lean a curve instead of a square wave, and so what lets it
        /// be used unfiltered.
        private const float LoadTransferFraction = 0.15f;

        private WalkerGait gait;

        private void UpdateGait(float dt)
        {
            float runBlend = RunBlend;
            float duty = gaitPattern.Duty(runBlend);
            Vector3 up = Vector3.up;
            // The world velocity AdvancePath integrated this frame, not a speed re-derived from the
            // heading. On a machine travelling across its own nose the two are different vectors,
            // and a foot drifted against the wrong one is dragged sideways all stance.
            Vector3 linear = commandedWorldVelocity;
            float yawRate = CommandedYawRate * Mathf.Deg2Rad;
            float pace = Pace;

            int planted = 0;
            foreach (LegState leg in legs) if (!leg.Swinging) planted++;

            foreach (LegState leg in legs)
            {
                // Offsets are re-read every frame rather than frozen at Initialise, because a gait
                // pattern may blend them with speed -- a walk becoming a trot moves them
                // continuously, so a foot in the air is never stranded by a table being swapped.
                leg.PhaseOffset = gaitPattern.PhaseOffset(leg.Measure.Index, runBlend);

                float stride = leg.Measure.StrideLength;
                float stance = WalkerGait.StanceDuration(pace, stride);

                // A slow walk takes long steps in time, which is right, but the relationship
                // diverges as the machine approaches a standstill -- and a stationary machine can
                // still be forced to step by terrain moving under it. Bound the swing so those
                // steps stay watchable.
                float swing = pace > 1e-3f
                    ? Mathf.Min(WalkerGait.SwingDuration(pace, stride, duty), stepDuration * 6f)
                    : stepDuration;

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
                        planted++;
                    }
                    else
                    {
                        leg.Foot = footStyle.SwingPoint(
                            leg.SwingFrom, leg.SwingTo, leg.SwingT, leg.SwingLift);
                    }
                    continue;
                }

                leg.StanceTime += dt;

                bool forced = gaitPattern.MayStepEarly(new StepEarlyRequest
                {
                    LegIndex = leg.Measure.Index,
                    PlantedCount = planted,
                    LegCount = legs.Count,
                    StanceTime = leg.StanceTime,
                    SwingDuration = swing,
                    Unreachable = leg.Unreachable,
                });

                if (!sliceOpened && !forced) continue;
                leg.StanceTime = 0f;

                Vector3 home = body.TransformPoint(leg.Measure.HomeLocal);
                Vector3 drift = WalkerGait.FootDrift(
                    leg.Rig.Anchor.position - body.position, linear, yawRate, up);

                leg.SwingFrom = leg.Foot;
                leg.SwingTo = ResolveFoothold(leg, home, drift, stance, swing);
                leg.SwingLift = SwingLiftFor(leg, leg.SwingFrom, leg.SwingTo);
                // Frozen at lift-off. Recomputing it every frame from the current pace lets a step
                // that began at speed stretch when the rider slows, so the foot is still in the air
                // when its slice reopens, misses that slot and limps for a stride.
                leg.SwingSpan = swing;
                leg.SwingT = 0f;
                leg.Swinging = true;
                leg.Unreachable = false;
                planted--;
            }

            UpdateLoad(dt);
        }

        /// How much of the body each foot is carrying. A planted foot takes the load on over the
        /// first slice of its stance and hands it back over the last, so through double support the
        /// weight rolls from the trailing foot onto the leading one instead of jumping between them.
        ///
        /// Time-to-lift comes from the clock rather than from watching for the lift: a leg swings
        /// when its slice opens, so that moment is known in advance and the foot can start unloading
        /// before it. A machine that is not walking has no lift-off coming and simply stands.
        private void UpdateLoad(float dt)
        {
            float pace = Pace;
            float duty = CurrentDuty;
            float cycle = WalkerGait.StanceDuration(pace, cycleStride) +
                          (pace > 1e-3f
                              ? WalkerGait.SwingDuration(pace, cycleStride, duty)
                              : stepDuration);
            float ramp = Mathf.Max(LoadTransferFraction * cycle, 1e-4f);

            int swinging = 0;
            foreach (LegState leg in legs)
            {
                if (leg.Swinging) swinging++;
                float toLift = pace > 1e-3f
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

        /// How high this step has to be lifted: enough to clear whatever is actually under the
        /// path, not a fixed arc that walks through rocks.
        ///
        /// A fixed height is only ever right on level ground. The swing is a straight line between
        /// two footholds, so anything standing between them gets walked through -- and on broken
        /// ground that is most of them.
        private float SwingLiftFor(LegState leg, Vector3 from, Vector3 to)
        {
            float needed = ground.HighestAlong(from, to, 4)
                           - Mathf.Max(from.y, to.y) + obstacleClearance;
            return Mathf.Max(leg.Measure.StepHeight, needed);
        }

        /// Where this leg plants next. The clamp itself is WalkerFoothold -- the one correct copy.
        private Vector3 ResolveFoothold(LegState leg, Vector3 home, Vector3 drift,
                                        float stance, float swing)
        {
            // The body's pose when this swing lands: where it will have travelled to, and how far
            // round it will have turned. Both the aim and the reach clamp are measured against it,
            // and they have to agree -- a foothold aimed at the arc but clamped against a hip
            // predicted along the tangent is clipped by a budget that is measuring something else.
            Vector3 travel = commandedWorldVelocity * swing;
            Quaternion turn = Quaternion.AngleAxis(CommandedYawRate * swing, Vector3.up);

            Vector3 probe = WalkerGait.Foothold(home, body.position, travel, turn, drift, stance,
                                                leg.Measure.StrideLength);

            // Clamp against the height the foot will LAND at, not the height `home` sits at.
            //
            // `home` is the rest foothold carried in body space, so a stride model that stands the
            // machine down -- HipBudgetStride does, YawArcStride does not -- leaves it a whole
            // stand-down BELOW the ground the foot actually meets. WalkerFoothold.Clamp measures its
            // rise from the hip to the probe, so that error goes straight into the budget as an
            // overstated rise; and the budget is a square root, so it does not degrade gracefully.
            // On a rig whose REST hip height is close to its linkage length the root goes imaginary
            // and the whole budget collapses onto its degenerate floor. Every step then lands barely
            // ahead of the hip and spends its entire stance dragging out past what the leg can hold,
            // which the body answers by diving to keep the trailing leg attached -- read from the
            // outside as a machine walking with its feet in the ground, bouncing once per stride.
            // The conjurer is 1% over that line and was doing exactly this; the ostrich is 9% under
            // it and was merely taking shorter steps than its own numbers asked for.
            //
            // The foot this leg is standing on is ON the ground, so its height is the honest
            // estimate of where the next one goes. Exact on the flat, and one step stale on a slope
            // -- the same staleness the drift and the swing estimate already carry.
            probe.y = leg.Foot.y;

            Vector3 hipAtTouchdown = WalkerFoothold.HipAtTouchdown(
                leg.Rig.Anchor.position, body.position, travel, turn);

            Vector3 aim = WalkerFoothold.Clamp(
                probe, hipAtTouchdown, leg.Rig.Geometry.MaxReach, FootholdReachFraction);

            if (SampleGround(leg, aim, out Vector3 point, out Vector3 normal))
            {
                leg.GroundNormal = normal;
                return point;
            }

            // Nothing under us: hold the current foothold. A ray that finds no ground is
            // deliberately not a void -- in a streamed world it means the chunk has not loaded.
            return leg.Foot;
        }
    }
}
