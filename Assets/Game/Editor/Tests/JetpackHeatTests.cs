using NUnit.Framework;
using SpaceGame.Gear.Jetpack;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The heat budget — the jetpack's only resource, and the only thing that limits a flight.
    ///
    /// <para>
    /// These are the durations the design was specified in ("six seconds of held thrust, and
    /// letting go is what buys more"), so they are asserted as durations rather than as rates. A
    /// rate that has been retuned is a tuning change; a duration that has moved is a design
    /// change, and this is where that shows up.
    /// </para>
    /// <para>
    /// In <c>Editor/Tests</c> rather than <c>Tests/EditMode</c>: these touch types in
    /// Assembly-CSharp, and that asmdef cannot reference it.
    /// </para>
    /// </summary>
    public class JetpackHeatTests
    {
        private const float Step = 1f / 50f;

        private static JetpackConfig Config() => new JetpackConfig();

        /// <summary>Run at one throttle until the motors cut, and report how long that took.</summary>
        private static float SecondsUntilOverheat(JetThrottle throttle, JetpackConfig cfg,
                                                  float liftFactor = 1f)
        {
            var heat = JetpackHeat.Cold;

            for (int i = 0; i < 10000; i++)
            {
                heat = JetpackHeat.Step(heat, throttle, cfg, Step, liftFactor);
                if (heat.Overheated) return (i + 1) * Step;
            }

            return float.PositiveInfinity;
        }

        [Test]
        public void HeldThrustLastsSixSeconds()
        {
            Assert.That(SecondsUntilOverheat(JetThrottle.Thrust, Config()),
                        Is.EqualTo(6f).Within(0.1f));
        }

        /// <summary>
        /// Holding Space is the only source of heat. Nothing else the pilot can ask for adds any,
        /// so a flight can never overheat without the key being held — which is what makes the
        /// gauge readable as "how long I have been climbing".
        /// </summary>
        [Test]
        public void OnlyThrustEverOverheats()
        {
            Assert.That(SecondsUntilOverheat(JetThrottle.Descend, Config()),
                        Is.EqualTo(float.PositiveInfinity));
            Assert.That(SecondsUntilOverheat(JetThrottle.Cut, Config()),
                        Is.EqualTo(float.PositiveInfinity));
        }

        /// <summary>
        /// The economy in one assertion: burning and falling outlasts holding the button. If this
        /// ever fails there is no reason to cut out and the jetpack is a single held key.
        ///
        /// <para>
        /// The duty cycle is a second of thrust to four of falling, because thrust costs 16.67/s
        /// and a fall refunds 5/s — anything richer than about one in three-and-a-third fills the
        /// gauge in the end however it is spread (<c>GDC-L1-SYS-0008</c>: the flight is a source
        /// and a sink, and only their RATES decide whether it is sustainable).
        /// </para>
        /// </summary>
        [Test]
        public void BurstingOutlastsHoldingThrust()
        {
            JetpackConfig cfg = Config();
            var heat = JetpackHeat.Cold;

            float flown = 0f;
            float thrusting = 0f;
            bool burning = true;
            float phase = 0f;

            while (!heat.Overheated && flown < 120f)
            {
                // One second on, four seconds off.
                phase += Step;
                float span = burning ? 1f : 4f;
                if (phase >= span) { phase = 0f; burning = !burning; }

                JetThrottle throttle = burning ? JetThrottle.Thrust : JetThrottle.Descend;
                heat = JetpackHeat.Step(heat, throttle, cfg, Step);

                flown += Step;
                if (burning) thrusting += Step;
            }

            // The pack never overheats on this duty cycle at all — a second of thrust adds 16.67
            // and four seconds of falling take 20 back — so the run ends on the 120 s guard. That
            // is the point: a pilot who lets go has an unbounded flight and one who never does has
            // six seconds. It holds on the FALL rather than on a cut, which is what makes the
            // rhythm reachable with the one key the pilot has.
            Assert.That(flown, Is.GreaterThan(6f), "bursting must outlast a held burn");
            Assert.That(thrusting, Is.GreaterThan(6f),
                        "and must buy more THRUST than a held burn, not just more airtime");
        }

        /// <summary>
        /// Letting go has to REFUND heat, not merely stop spending it. With the crouch cut gone
        /// this is the only recovery a flying pilot can ask for, so a fall that merely held the
        /// gauge still would make every flight a one-way six seconds.
        /// </summary>
        [Test]
        public void LettingGoIsWhatCools()
        {
            JetpackConfig cfg = Config();

            var hot = new JetpackHeat { Value = 50f };

            Assert.That(JetpackHeat.Step(hot, JetThrottle.Descend, cfg, 1f).Value,
                        Is.LessThan(50f), "falling must cool");
            Assert.That(JetpackHeat.Step(hot, JetThrottle.Thrust, cfg, 1f).Value,
                        Is.GreaterThan(50f), "thrusting must cost");
            Assert.That(JetpackHeat.Step(hot, JetThrottle.Cut, cfg, 1f).Value,
                        Is.LessThan(JetpackHeat.Step(hot, JetThrottle.Descend, cfg, 1f).Value),
                        "dead motors cool faster than lit ones");
        }

        /// <summary>
        /// The latch, which is what makes an overheat a fall rather than a stutter at the top of
        /// the gauge. A bare threshold would relight one frame's worth of cooling later.
        /// </summary>
        [Test]
        public void OverheatLatchesUntilTheRelightPoint()
        {
            JetpackConfig cfg = Config();
            var heat = new JetpackHeat { Value = cfg.OverheatAt, Overheated = true };

            Assert.That(heat.Allows(JetThrottle.Thrust), Is.False);
            Assert.That(heat.Allows(JetThrottle.Descend), Is.False);
            Assert.That(heat.Allows(JetThrottle.Cut), Is.True, "a cut is always allowed");

            // Cool to just above the relight point: still locked out.
            while (heat.Value > cfg.RelightAt + 1f)
                heat = JetpackHeat.Step(heat, JetThrottle.Cut, cfg, Step);

            Assert.That(heat.Overheated, Is.True, "still latched above the relight point");

            while (heat.Overheated && heat.Value > 0f)
                heat = JetpackHeat.Step(heat, JetThrottle.Cut, cfg, Step);

            Assert.That(heat.Overheated, Is.False);
            Assert.That(heat.Value, Is.EqualTo(cfg.RelightAt).Within(0.5f),
                        "it relights at the relight point, not at zero");
        }

        /// <summary>
        /// An overheated pack flies nothing, whatever the pilot is holding. Resolve is the one
        /// place that correction happens, so the flight can never disagree with the gauge.
        /// </summary>
        [Test]
        public void ResolveForcesCutWhileOverheated()
        {
            var heat = new JetpackHeat { Value = 100f, Overheated = true };

            Assert.That(heat.Resolve(JetThrottle.Thrust), Is.EqualTo(JetThrottle.Cut));
            Assert.That(heat.Resolve(JetThrottle.Descend), Is.EqualTo(JetThrottle.Cut));
        }

        [Test]
        public void HeatNeverLeavesItsScale()
        {
            JetpackConfig cfg = Config();
            var heat = JetpackHeat.Cold;

            for (int i = 0; i < 2000; i++)
            {
                heat = JetpackHeat.Step(heat, JetThrottle.Thrust, cfg, Step);
                Assert.That(heat.Value, Is.InRange(0f, cfg.OverheatAt));
            }

            for (int i = 0; i < 2000; i++)
            {
                heat = JetpackHeat.Step(heat, JetThrottle.Cut, cfg, Step);
                Assert.That(heat.Value, Is.InRange(0f, cfg.OverheatAt));
            }
        }

        /// <summary>
        /// <b>A lift is paid for in burn time.</b> The extra thrust a passenger buys is billed
        /// through the same factor that granted it, so hauling somebody shortens the flight in
        /// exact proportion. Split the two and the lift is free, which is the one way this feature
        /// could quietly become the best reason to carry a rope.
        /// </summary>
        [Test]
        public void ALiftIsBilledInProportionToTheThrustItBuys()
        {
            JetpackConfig cfg = Config();
            const float Lift = 1.4f;

            float alone = SecondsUntilOverheat(JetThrottle.Thrust, cfg);
            float loaded = SecondsUntilOverheat(JetThrottle.Thrust, cfg, Lift);

            Assert.That(loaded, Is.EqualTo(alone / Lift).Within(2f * Step));
        }

        /// <summary>
        /// Cooling is the pack shedding heat, and a rope hanging off the pilot does not change how
        /// fast it does that. Scaling the cool rates too would make a heavy load cool faster than
        /// a light one, which is the opposite of the rule.
        /// </summary>
        [Test]
        public void ALoadDoesNotChangeCooling()
        {
            JetpackConfig cfg = Config();
            var hot = new JetpackHeat { Value = 50f, Overheated = false };

            JetpackHeat loaded = JetpackHeat.Step(hot, JetThrottle.Descend, cfg, Step, 1.4f);
            JetpackHeat alone = JetpackHeat.Step(hot, JetThrottle.Descend, cfg, Step);

            Assert.That(loaded.Value, Is.EqualTo(alone.Value).Within(0.0001f));
        }
    }
}
