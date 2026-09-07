using UnityEngine;

namespace SpaceGame.Gear.Jetpack
{
    /// <summary>
    /// One physics step of a jetpack flight: velocity in, velocity out.
    ///
    /// <para>
    /// Pure, so the whole flight model can be exercised without a Rigidbody, a player or a clock —
    /// and so the two properties that must never break can be asserted directly: a cut pack falls
    /// at gravity, and no sequence of inputs mints energy.
    /// </para>
    /// <para>
    /// It integrates its own gravity. <c>JetpackFlight</c> turns Unity's off for the duration, for
    /// the reason the wingsuit documents: one source of weight, and this world's g is 18 rather
    /// than 9.81.
    /// </para>
    /// </summary>
    public static class JetpackStep
    {
        /// <summary>
        /// Advance the body's velocity.
        ///
        /// <para>
        /// The three throttles differ only in how much push the nozzles are given, and that is the
        /// point — the direction, the gravity and the drag are the same arithmetic in all three,
        /// so there is no state in which the machine obeys different physics.
        /// </para>
        /// </summary>
        /// <param name="velocity">World velocity now, m/s.</param>
        /// <param name="throttle">What the motors are doing, already corrected for an overheat by
        /// <see cref="JetpackHeat.Resolve"/>.</param>
        /// <param name="nozzle">Where the nozzles have actually got to — never the command.</param>
        /// <param name="headingDegrees">The body's yaw. Nozzles are in the wearer's frame.</param>
        public static Vector3 Step(Vector3 velocity, JetThrottle throttle, JetNozzle nozzle,
                                   float headingDegrees, JetpackConfig cfg, float dt)
        {
            if (cfg == null || dt <= 0f) return velocity;

            Vector3 direction = JetpackVector.WorldThrust(nozzle, headingDegrees);

            velocity += direction * Magnitude(velocity, throttle, direction, cfg) * dt;
            velocity += Vector3.down * (cfg.Gravity * dt);

            return Damped(velocity, cfg, dt);
        }

        /// <summary>
        /// How hard the motors push this step, m/s².
        ///
        /// <para>
        /// <b>A descent is a servo, not a setting.</b> It solves for the push that would cancel
        /// gravity along the direction the nozzles happen to be pointing, plus a term that pulls
        /// the vertical speed toward <c>DescentSpeed</c> DOWN — then caps the answer at
        /// <c>HoverAuthority</c>. So letting go of Space is a lift-off in reverse, not a drop: the
        /// pack settles onto its sink rate from a climb or a dive alike, and lands from it.
        /// </para>
        /// <para>
        /// The cap is what makes coming down at full rake cost extra altitude: the vertical share
        /// of a nozzle 40° over is only 77% of it, so holding a direction while sinking needs a
        /// third more thrust than the servo is allowed, and the pack drops faster than it asked
        /// to. Nobody had to write that rule; it falls out of solving along the real axis, and it
        /// is what stops a descent from being free horizontal flight.
        /// </para>
        /// </summary>
        private static float Magnitude(Vector3 velocity, JetThrottle throttle, Vector3 direction,
                                       JetpackConfig cfg)
        {
            switch (throttle)
            {
                case JetThrottle.Thrust:
                    return cfg.ThrustAcceleration;

                case JetThrottle.Descend:
                    // Guarded because the nozzles can in principle be handed a direction with no
                    // vertical share at all, and dividing by it would be an infinite hover.
                    float share = Mathf.Max(direction.y, 0.2f);
                    float error = velocity.y + cfg.DescentSpeed;   // zero at the wanted sink
                    float wanted = (cfg.Gravity - error * cfg.HoverDamping) / share;

                    return Mathf.Clamp(wanted, 0f, cfg.HoverAuthority * cfg.Gravity);

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Air resistance, split into two because the two axes are different design problems.
        ///
        /// <para>
        /// Horizontal drag is what gives the jetpack a top speed instead of an ever-growing one;
        /// it settles where thrust and drag balance. Vertical drag is much lower on purpose — a
        /// fall should stay a fall — and exists only so that a long cut cannot reach a speed no
        /// landing survives before the pilot has had a chance to relight.
        /// </para>
        /// <para>
        /// Exponential rather than linear (<c>Exp(-k·dt)</c>), so the result does not depend on
        /// the step length. A linear <c>v -= v·k·dt</c> reverses the velocity outright at a large
        /// enough step, which is how a physics hitch turns into a body fired backwards.
        /// </para>
        /// </summary>
        private static Vector3 Damped(Vector3 velocity, JetpackConfig cfg, float dt)
        {
            float horizontal = Mathf.Exp(-cfg.HorizontalDrag * dt);
            float vertical = Mathf.Exp(-cfg.VerticalDrag * dt);

            return new Vector3(velocity.x * horizontal,
                               velocity.y * vertical,
                               velocity.z * horizontal);
        }
    }
}
