using NUnit.Framework;
using SpaceGame.Gear.Jetpack;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The heat budget — the jetpack's only resource, and the only thing that limits a flight.
    ///
    /// <para>
    /// These are the durations the design was specified in ("15 seconds of held thrust, and
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
        private static float SecondsUntilOverheat(JetThrottle throttle, JetpackConfig cfg)
        {
            var heat = JetpackHeat.Cold;

            for (int i = 0; i < 10000; i++)
            {
                heat = JetpackHeat.Step(heat, throttle, cfg, Step);
                if (heat.Overheated) return (i + 1) * Step;
            }

            return float.PositiveInfinity;
        }

        [Test]
        public void HeldThrustLastsFifteenSeconds()
        {
            Assert.That(SecondsUntilOverheat(JetThrottle.Thrust, Config()),
                        Is.EqualTo(15f).Within(0.1f));
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
        /// The economy in one assertion: burning and coasting outlasts holding the button. If this
        /// ever fails there is no reason to cut out and the jetpack is a single held key.
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
                // One second on, one second off.
                phase += Step;
                if (phase >= 1f) { phase = 0f; burning = !burning; }

                JetThrottle throttle = burning ? JetThrottle.Thrust : JetThrottle.Descend;
                heat = JetpackHeat.Step(heat, throttle, cfg, Step);

                flown += Step;
                if (burning) thrusting += Step;
            }

            // The pack never overheats on this duty cycle at all — thrust adds 6.67/s for a second
            // and the descent takes 5/s back — so the run ends on the 120 s guard. That is the
            // point: a pilot who lets go has an unbounded flight and one who never does has
            // fifteen seconds. It holds on the DESCENT rather than on a cut, which is what makes
            // the rhythm reachable with the one key the pilot has.
            Assert.That(flown, Is.GreaterThan(15f), "bursting must outlast a held burn");
            Assert.That(thrusting, Is.GreaterThan(15f),
                        "and must buy more THRUST than a held burn, not just more airtime");
        }

        /// <summary>
        /// Letting go has to REFUND heat, not merely stop spending it. With the crouch cut gone
        /// this is the only recovery a flying pilot can ask for, so a descent that merely held
        /// the gauge still would make every flight a one-way fifteen seconds.
        /// </summary>
        [Test]
        public void LettingGoIsWhatCools()
        {
            JetpackConfig cfg = Config();

            var hot = new JetpackHeat { Value = 50f };

            Assert.That(JetpackHeat.Step(hot, JetThrottle.Descend, cfg, 1f).Value,
                        Is.LessThan(50f), "sinking must cool");
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
    }
}
