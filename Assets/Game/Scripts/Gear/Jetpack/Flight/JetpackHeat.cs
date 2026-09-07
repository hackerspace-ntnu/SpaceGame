using UnityEngine;

namespace SpaceGame.Gear.Jetpack
{
    /// <summary>What the motors are doing this step. The three ways heat can move.</summary>
    public enum JetThrottle
    {
        /// <summary>
        /// Motors off, nozzles dark. Free fall at this world's full gravity, and it cools fastest.
        /// The pilot cannot ask for this: it is what an overheat forces and what a stowed pack
        /// sits at.
        /// </summary>
        Cut = 0,

        /// <summary>
        /// Space released. The motors idle against gravity to a steady sink — the pack comes down
        /// at <c>DescentSpeed</c> rather than falling — the nozzles still burn, and heat comes off.
        /// This is the whole of "let go to come down".
        /// </summary>
        Descend = 1,

        /// <summary>Space held. Full push, and the only state that adds heat.</summary>
        Thrust = 2,
    }

    /// <summary>
    /// The heat budget, as a pure value type.
    ///
    /// <para>
    /// <b>Heat is the jetpack's only resource, and one button spends it or refunds it.</b>
    /// Holding Space is the only source — 15 seconds of it from cold — and letting go is the
    /// sink, cooling while the pack sinks with its nozzles still lit. So a long flight is a
    /// rhythm of climbing and coming down rather than a single held button, and the rhythm is
    /// paced by ONE key (<c>GDC-L1-UX-0005</c>, <c>GDC-L1-SYS-0008</c>: the sink has to be
    /// somewhere the pilot actually spends time, and with the crouch cut gone the descent is the
    /// only place left).
    /// </para>
    /// <para>
    /// Overheat is a LATCH, not a threshold. Reaching the top cuts the motors and sets
    /// <see cref="Overheated"/>, and nothing relights until heat has fallen to
    /// <c>RelightAt</c> — so the punish is a measured fall rather than a stutter at the top of the
    /// gauge, which is what a bare threshold gives you (thrust, overheat, cool one frame's worth,
    /// thrust again).
    /// </para>
    /// <para>
    /// Pure and struct-valued so the whole economy is testable without a Rigidbody, a player or a
    /// clock. <see cref="JetpackHeatTests"/> pins the three durations.
    /// </para>
    /// </summary>
    public struct JetpackHeat
    {
        /// <summary>Heat now, on the config's own scale (0 .. <c>OverheatAt</c>).</summary>
        public float Value;

        /// <summary>
        /// True from the moment heat reached the top until it has fallen back to
        /// <c>RelightAt</c>. While set, <see cref="Allows"/> refuses every throttle but
        /// <see cref="JetThrottle.Cut"/>.
        /// </summary>
        public bool Overheated;

        /// <summary>A cold pack, ready to fly.</summary>
        public static JetpackHeat Cold => new JetpackHeat { Value = 0f, Overheated = false };

        /// <summary>Heat as a 0..1 fraction of the overheat point, for gauges and shaders.</summary>
        public readonly float Fraction(JetpackConfig cfg) =>
            cfg == null || cfg.OverheatAt <= 0f ? 0f : Mathf.Clamp01(Value / cfg.OverheatAt);

        /// <summary>
        /// May the motors run at all right now? False for the whole of an overheat, which is what
        /// makes the latch a punish rather than a stutter.
        /// </summary>
        public readonly bool Allows(JetThrottle wanted) =>
            wanted == JetThrottle.Cut || !Overheated;

        /// <summary>
        /// What the pilot asked for, corrected by what the pack will actually do. Call this before
        /// stepping the flight, and fly the answer — an overheated pack is cut whatever the
        /// player is holding.
        /// </summary>
        public readonly JetThrottle Resolve(JetThrottle wanted) =>
            Allows(wanted) ? wanted : JetThrottle.Cut;

        /// <summary>
        /// Advance one step at the given throttle. Returns the new heat; the caller decides what
        /// to do with an overheat that has just latched.
        ///
        /// <para>
        /// Order matters: heat is added first and the latch is tested against the result, so
        /// reaching the top on a step also cuts on that step. Testing first would give one free
        /// frame of thrust past the limit, which is small but is exactly the kind of edge a
        /// player finds and then relies on.
        /// </para>
        /// </summary>
        public static JetpackHeat Step(JetpackHeat heat, JetThrottle throttle, JetpackConfig cfg,
                                       float dt)
        {
            if (cfg == null || dt <= 0f) return heat;

            float rate = throttle switch
            {
                JetThrottle.Thrust => cfg.ThrustHeatPerSecond,
                JetThrottle.Descend => -cfg.DescendCoolPerSecond,
                _ => -cfg.CoolPerSecond,
            };

            heat.Value = Mathf.Clamp(heat.Value + rate * dt, 0f, cfg.OverheatAt);

            if (!heat.Overheated && heat.Value >= cfg.OverheatAt)
                heat.Overheated = true;
            else if (heat.Overheated && heat.Value <= cfg.RelightAt)
                heat.Overheated = false;

            return heat;
        }
    }
}
