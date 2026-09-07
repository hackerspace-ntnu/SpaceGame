using NUnit.Framework;
using SpaceGame.Gear.Jetpack;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The flight model: the two properties that must never break, and the emergent one the design
    /// leans on.
    ///
    /// <para>
    /// Never break: a cut pack falls, letting go of Space does NOT, and no sequence of inputs
    /// mints energy. Emergent: sinking at full rake costs extra altitude, because the descent
    /// servo solves along the nozzle axis and is capped — nobody wrote that rule, and a
    /// "simplification" that solved for vertical thrust directly would delete it without failing
    /// anything else.
    /// </para>
    /// </summary>
    public class JetpackStepTests
    {
        private const float Step = 1f / 50f;

        private static JetpackConfig Config() => new JetpackConfig();

        private static Vector3 Run(JetThrottle throttle, JetNozzle nozzle, Vector3 velocity,
                                   float seconds, JetpackConfig cfg, float heading = 0f)
        {
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int i = 0; i < steps; i++)
                velocity = JetpackStep.Step(velocity, throttle, nozzle, heading, cfg, Step);

            return velocity;
        }

        [Test]
        public void CutIsAFall()
        {
            Vector3 after = Run(JetThrottle.Cut, JetNozzle.Vertical, Vector3.zero, 1f, Config());

            Assert.That(after.y, Is.LessThan(-15f), "a second of cut is most of a second of gravity");
            Assert.That(after.y, Is.GreaterThan(-19f), "and no more than gravity, minus a little drag");
        }

        [Test]
        public void FullThrustClimbs()
        {
            Vector3 after = Run(JetThrottle.Thrust, JetNozzle.Vertical, Vector3.zero, 1f, Config());

            Assert.That(after.y, Is.GreaterThan(5f),
                        "thrust must beat this world's 18 m/s² by enough to be worth lighting");
        }

        /// <summary>
        /// Letting go settles onto the sink rate from either side — a climb or a dive — which is
        /// the descent servo doing its job. Without the damping term whatever vertical speed the
        /// pilot arrived with is kept forever.
        /// </summary>
        [Test]
        public void DescendSettlesOntoItsSinkRate()
        {
            JetpackConfig cfg = Config();

            Vector3 fromClimb = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.up * 8f, 4f, cfg);
            Vector3 fromDive = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.down * 8f, 4f, cfg);

            Assert.That(fromClimb.y, Is.EqualTo(-cfg.DescentSpeed).Within(1f));
            Assert.That(fromDive.y, Is.EqualTo(-cfg.DescentSpeed).Within(1f));
        }

        /// <summary>
        /// The request in one assertion: hands off the key you come down, but nothing like a fall.
        /// A second of free fall in this world is 18 m/s; a second of letting go is the sink rate.
        /// If these two ever converge, releasing Space has become the punishment an overheat is
        /// supposed to be.
        /// </summary>
        [Test]
        public void LettingGoIsASinkAndNotAFall()
        {
            JetpackConfig cfg = Config();

            Vector3 released = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.zero, 3f, cfg);
            Vector3 cut = Run(JetThrottle.Cut, JetNozzle.Vertical, Vector3.zero, 3f, cfg);

            Assert.That(released.y, Is.LessThan(0f), "letting go must come DOWN");
            Assert.That(released.y, Is.GreaterThan(cut.y * 0.5f),
                        "but at nothing like the speed of dead motors");
        }

        /// <summary>
        /// The emergent rule. Sinking with the nozzles hard over asks for more thrust than the
        /// servo is allowed, so the pack drops faster than its own sink rate — which is what
        /// stops a descent being free horizontal flight in any direction the pilot likes.
        /// </summary>
        [Test]
        public void SinkingAtFullRakeCostsExtraAltitude()
        {
            JetpackConfig cfg = Config();
            var raked = new JetNozzle { Pitch = cfg.MaxDeflectionDegrees };

            Vector3 level = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.zero, 3f, cfg);
            Vector3 after = Run(JetThrottle.Descend, raked, Vector3.zero, 3f, cfg);

            Assert.That(after.y, Is.LessThan(level.y),
                        "a raked descent must fall faster than a level one");
            Assert.That(after.z, Is.GreaterThan(1f), "while still drifting the way it is pointed");
        }

        [Test]
        public void HorizontalSpeedReachesATerminal()
        {
            JetpackConfig cfg = Config();
            var raked = new JetNozzle { Pitch = cfg.MaxDeflectionDegrees };

            Vector3 ten = Run(JetThrottle.Thrust, raked, Vector3.zero, 10f, cfg);
            Vector3 twenty = Run(JetThrottle.Thrust, raked, ten, 10f, cfg);

            Assert.That(twenty.z - ten.z, Is.LessThan(1f),
                        "drag must settle the speed rather than let it grow without bound");
            Assert.That(twenty.z, Is.GreaterThan(8f), "and the terminal has to be worth flying to");
        }

        /// <summary>
        /// No energy from nowhere. Waggling the nozzles is the jetpack's version of the wingsuit's
        /// "mint speed by waggling the mouse", and it has to be worth nothing.
        /// </summary>
        [Test]
        public void WagglingTheNozzlesMintsNothing()
        {
            JetpackConfig cfg = Config();
            var velocity = new Vector3(0f, 0f, 12f);

            float before = velocity.magnitude;

            for (int i = 0; i < 400; i++)
            {
                var nozzle = new JetNozzle
                {
                    Pitch = Mathf.Sin(i * 0.7f) * cfg.MaxDeflectionDegrees,
                    Roll = Mathf.Cos(i * 0.9f) * cfg.MaxDeflectionDegrees,
                };

                velocity = JetpackStep.Step(velocity, JetThrottle.Cut, nozzle, 0f, cfg, Step);
            }

            // Cut means no thrust at all, whatever the nozzles are doing, so the only things that
            // may have touched this are gravity and drag. Horizontally that can only ever remove.
            Assert.That(new Vector2(velocity.x, velocity.z).magnitude, Is.LessThan(before));
        }

        /// <summary>
        /// The drag is exponential, so a big step behaves like several small ones. A linear
        /// <c>v -= v·k·dt</c> reverses the velocity at a large enough step, which is how a physics
        /// hitch fires a body backwards.
        /// </summary>
        [Test]
        public void ABigStepNeverReversesTheVelocity()
        {
            JetpackConfig cfg = Config();
            cfg.HorizontalDrag = 40f;

            Vector3 after = JetpackStep.Step(new Vector3(0f, 0f, 20f), JetThrottle.Cut,
                                             JetNozzle.Vertical, 0f, cfg, 0.5f);

            Assert.That(after.z, Is.GreaterThanOrEqualTo(0f));
        }

        /// <summary>
        /// Heading is applied to the thrust, not to the velocity that is already there — so
        /// turning changes where the push goes and leaves the existing motion alone.
        /// </summary>
        [Test]
        public void HeadingAimsTheThrustOnly()
        {
            JetpackConfig cfg = Config();
            var raked = new JetNozzle { Pitch = 30f };

            Vector3 north = Run(JetThrottle.Thrust, raked, Vector3.zero, 1f, cfg, heading: 0f);
            Vector3 east = Run(JetThrottle.Thrust, raked, Vector3.zero, 1f, cfg, heading: 90f);

            Assert.That(north.z, Is.GreaterThan(1f));
            Assert.That(east.x, Is.EqualTo(north.z).Within(0.01f));
            Assert.That(east.y, Is.EqualTo(north.y).Within(0.01f));
        }
    }
}
