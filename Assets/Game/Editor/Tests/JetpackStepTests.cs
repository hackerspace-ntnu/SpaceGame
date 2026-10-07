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
    /// Never break: a pack that is not thrusting falls at this world's gravity — a released key
    /// and an overheat alike — and no sequence of inputs mints energy. The first is the whole of
    /// "let go and you fall": the pilot buys altitude with heat and pays it back on the way down,
    /// and any hover put back under a released key deletes that bargain without failing anything
    /// else.
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
        /// Letting go of Space is a FALL, not a settle. The motors idle, nothing catches the
        /// pilot, and the only way down that is not a fall is a burn they aim themselves.
        /// </summary>
        [Test]
        public void LettingGoFallsExactlyLikeAnOverheat()
        {
            JetpackConfig cfg = Config();

            Vector3 released = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.zero, 3f, cfg);
            Vector3 cut = Run(JetThrottle.Cut, JetNozzle.Vertical, Vector3.zero, 3f, cfg);

            Assert.That(released.y, Is.EqualTo(cut.y).Within(0.001f),
                        "a released key must be the same fall dead motors are");
            Assert.That(released.y, Is.LessThan(-15f), "and it must be a real fall");
        }

        /// <summary>
        /// A descent is arrested by thrust and by nothing else. Without this the pack could be
        /// parked in the air by letting go, which is the servo this design deleted.
        /// </summary>
        [Test]
        public void ReleasingKeepsFallingFasterAndFaster()
        {
            JetpackConfig cfg = Config();

            Vector3 first = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.zero, 1f, cfg);
            Vector3 second = Run(JetThrottle.Descend, JetNozzle.Vertical, first, 1f, cfg);

            Assert.That(second.y, Is.LessThan(first.y - 10f),
                        "nothing may hold a sink rate while the key is up");
        }

        /// <summary>
        /// Where the nozzles point is worth nothing without thrust behind them, so a pilot cannot
        /// steer a fall by raking the pods over.
        /// </summary>
        [Test]
        public void RakingTheNozzlesDoesNothingWhileFalling()
        {
            JetpackConfig cfg = Config();
            var raked = new JetNozzle { Pitch = cfg.MaxDeflectionDegrees };

            Vector3 level = Run(JetThrottle.Descend, JetNozzle.Vertical, Vector3.zero, 3f, cfg);
            Vector3 over = Run(JetThrottle.Descend, raked, Vector3.zero, 3f, cfg);

            Assert.That(over.y, Is.EqualTo(level.y).Within(0.001f));
            Assert.That(over.z, Is.EqualTo(0f).Within(0.001f), "and it buys no drift either");
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

        // ── Lifting a load ────────────────────────────────────────────────────
        //
        // The player prefab's own mass, so these read as the case they are actually about: one
        // astronaut on a rope under another one.

        private const float PlayerMass = 80f;

        /// <summary>
        /// <b>The feature.</b> A rope shares an acceleration out by mass, so without the lift rule
        /// an equal-weight passenger turns this pack's +12 m/s² climb into a −3 m/s² sink. The
        /// pair has to go UP, and slowly — briskly would mean a passenger costs nothing.
        /// </summary>
        [Test]
        public void AnEqualWeightPassengerRisesSlowly()
        {
            float climb = JetpackLift.PairClimb(PlayerMass, PlayerMass, Config());

            Assert.That(climb, Is.GreaterThan(1f), "a leashed player must leave the ground");
            Assert.That(climb, Is.LessThan(6f), "and must not rise like a pilot flying alone");
        }

        /// <summary>
        /// A pilot with nothing on the rope flies exactly the pack that was tuned. The lift rule
        /// is allowed to change what happens under load and nothing else — this is the assertion
        /// that stops it being a general buff.
        /// </summary>
        [Test]
        public void FlyingAloneIsUntouched()
        {
            JetpackConfig cfg = Config();

            Assert.That(JetpackLift.Factor(0f, cfg), Is.EqualTo(1f));
            Assert.That(JetpackLift.PairClimb(PlayerMass, 0f, cfg),
                        Is.EqualTo(cfg.ThrustAcceleration - cfg.Gravity).Within(0.001f));
        }

        /// <summary>
        /// The pack lifts a person, not a hull. Past <c>MaxLiftRatio</c> the assist stops growing
        /// while the load's real weight stays in the physics, so the arithmetic turns back into a
        /// sink on its own and nothing has to classify what is on the end of the rope.
        /// </summary>
        [Test]
        public void SomethingFarHeavierStaysOnTheGround()
        {
            Assert.That(JetpackLift.PairClimb(PlayerMass, 1000f, Config()), Is.LessThan(0f));
        }

        /// <summary>
        /// And the clamp is what does it: a rope onto something enormous must not read as an
        /// enormous power boost, or tying yourself to the lander would be the fastest pack in the
        /// game.
        /// </summary>
        [Test]
        public void TheAssistIsBoundedByMaxLiftRatio()
        {
            JetpackConfig cfg = Config();

            Assert.That(JetpackLift.Factor(100f, cfg),
                        Is.EqualTo(JetpackLift.Factor(cfg.MaxLiftRatio, cfg)).Within(0.0001f));
        }

        /// <summary>
        /// A load buys thrust and nothing else. Letting go under a passenger has to fall exactly
        /// like letting go alone, or the rope would have quietly become a parachute.
        /// </summary>
        [Test]
        public void ALoadDoesNotSlowTheFall()
        {
            JetpackConfig cfg = Config();

            Vector3 loaded = JetpackStep.Step(Vector3.zero, JetThrottle.Descend, JetNozzle.Vertical,
                                              0f, cfg, Step, liftFactor: 1.4f);
            Vector3 alone = JetpackStep.Step(Vector3.zero, JetThrottle.Descend, JetNozzle.Vertical,
                                             0f, cfg, Step);

            Assert.That(loaded.y, Is.EqualTo(alone.y).Within(0.0001f));
        }
    }
}
