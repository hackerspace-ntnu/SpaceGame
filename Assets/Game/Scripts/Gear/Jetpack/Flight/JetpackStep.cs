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
        /// so there is no state in which the machine obeys different physics. Two of the three
        /// give no push at all: a released key and an overheat both fall.
        /// </para>
        /// </summary>
        /// <param name="velocity">World velocity now, m/s.</param>
        /// <param name="throttle">What the motors are doing, already corrected for an overheat by
        /// <see cref="JetpackHeat.Resolve"/>.</param>
        /// <param name="nozzle">Where the nozzles have actually got to — never the command.</param>
        /// <param name="headingDegrees">The body's yaw. Nozzles are in the wearer's frame.</param>
        /// <param name="liftFactor">How much harder the motors are working for what is roped to
        /// the pilot — <see cref="JetpackLift.Factor"/>. 1 for a pilot flying alone, which is what
        /// the default leaves the model as it was.</param>
        public static Vector3 Step(Vector3 velocity, JetThrottle throttle, JetNozzle nozzle,
                                   float headingDegrees, JetpackConfig cfg, float dt,
                                   float liftFactor = 1f)
        {
            if (cfg == null || dt <= 0f) return velocity;

            Vector3 direction = JetpackVector.WorldThrust(nozzle, headingDegrees);

            velocity += direction * Magnitude(throttle, cfg, liftFactor) * dt;
            velocity += Vector3.down * (cfg.Gravity * dt);

            return Damped(velocity, cfg, dt);
        }

        /// <summary>
        /// How hard the motors push this step, m/s².
        ///
        /// <para>
        /// <b>Only a held key pushes.</b> Thrust is the one throttle with any push behind it —
        /// releasing Space idles the motors and the pilot falls at this world's full gravity, the
        /// same arithmetic an overheat gets. There is no descent servo and no hover: coming down
        /// is a fall the pilot arrests by lighting the motors again, which is what makes altitude
        /// worth something and a burn worth aiming.
        /// </para>
        /// <para>
        /// So the two ways of not thrusting differ in the heat budget alone — a release cools and
        /// can be undone on the next frame, an overheat cools faster and cannot be undone at all
        /// until the latch clears.
        /// </para>
        /// <para>
        /// The lift factor multiplies the push and NOTHING else here, which is why a load leaves
        /// the fall alone: a pack hauling a passenger drops exactly as fast as an empty one the
        /// moment the key comes up. The same factor is billed as heat by
        /// <see cref="JetpackHeat.Step"/>, and that pairing is the price of the lift.
        /// </para>
        /// </summary>
        private static float Magnitude(JetThrottle throttle, JetpackConfig cfg, float liftFactor) =>
            throttle == JetThrottle.Thrust
                ? cfg.ThrustAcceleration * Mathf.Max(1f, liftFactor)
                : 0f;

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
