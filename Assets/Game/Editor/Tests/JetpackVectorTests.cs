using NUnit.Framework;
using SpaceGame.Gear.Jetpack;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The steering — and specifically the rate limit, which is the jetpack's whole learning curve.
    ///
    /// <para>
    /// The load-bearing assertion in this file is <see cref="ThrustFollowsTheNozzleNotTheKey"/>.
    /// Everything the design promises about flying arcs rather than corners collapses the moment
    /// somebody "simplifies" the flight by feeding it the command directly, and that change would
    /// look perfectly reasonable in a diff.
    /// </para>
    /// </summary>
    public class JetpackVectorTests
    {
        private static JetpackConfig Config() => new JetpackConfig();

        [Test]
        public void NoInputPointsStraightDown()
        {
            JetNozzle command = JetpackVector.Command(Vector2.zero, 0f, Config());

            Assert.That(command.Magnitude, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(command.LocalThrust.y, Is.EqualTo(1f).Within(1e-4f),
                        "with no input the thrust is straight up");
        }

        [Test]
        public void ForwardRakesTheNozzleForward()
        {
            JetNozzle command = JetpackVector.Command(Vector2.up, 0f, Config());

            Assert.That(command.Pitch, Is.GreaterThan(0f));
            Assert.That(command.Roll, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(command.LocalThrust.z, Is.GreaterThan(0f), "and pushes the pilot forward");
            Assert.That(command.LocalThrust.y, Is.GreaterThan(0f), "while still holding them up");
        }

        [Test]
        public void StrafeRightPushesRight()
        {
            JetNozzle command = JetpackVector.Command(Vector2.right, 0f, Config());

            Assert.That(command.Roll, Is.GreaterThan(0f));
            Assert.That(command.LocalThrust.x, Is.GreaterThan(0f));
        }

        [Test]
        public void DeflectionIsClampedToTheConfigsLimit()
        {
            JetpackConfig cfg = Config();

            // A diagonal at full stick would exceed the limit on each axis separately if the two
            // were clamped independently.
            JetNozzle command = JetpackVector.Command(new Vector2(1f, 1f), 90f, cfg);

            Assert.That(command.Magnitude, Is.LessThanOrEqualTo(cfg.MaxDeflectionDegrees + 1e-3f));
        }

        /// <summary>
        /// Looking down commits harder to the same key, looking up backs off. This is what makes
        /// the same W feel different depending on where the pilot is pointed.
        /// </summary>
        [Test]
        public void LookPitchScalesTheSameKey()
        {
            JetpackConfig cfg = Config();

            float level = JetpackVector.Command(Vector2.up, 0f, cfg).Pitch;
            float down = JetpackVector.Command(Vector2.up, 60f, cfg).Pitch;
            float up = JetpackVector.Command(Vector2.up, -60f, cfg).Pitch;

            Assert.That(down, Is.GreaterThan(level), "looking down flattens the flight out");
            Assert.That(up, Is.LessThan(level), "looking up steepens the climb");
            Assert.That(up, Is.GreaterThanOrEqualTo(0f),
                        "and never reverses — a key that pushed you backwards because of where " +
                        "your head was would be unreadable");
        }

        /// <summary>
        /// Looking about with no key held must not move the pack. If the look were an added term
        /// rather than a multiplier, a hands-off levitate would drift wherever the player glanced
        /// and the machine could not be parked.
        /// </summary>
        [Test]
        public void LookAloneCommandsNothing()
        {
            JetpackConfig cfg = Config();

            Assert.That(JetpackVector.Command(Vector2.zero, 80f, cfg).Magnitude,
                        Is.EqualTo(0f).Within(1e-4f));
            Assert.That(JetpackVector.Command(Vector2.zero, -80f, cfg).Magnitude,
                        Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void NozzlesSwingAtTheConfiguredRate()
        {
            JetpackConfig cfg = Config();
            JetNozzle command = JetpackVector.Command(Vector2.up, 0f, cfg);

            JetNozzle after = JetpackVector.Advance(JetNozzle.Vertical, command, cfg, 0.1f);

            Assert.That(after.Magnitude,
                        Is.EqualTo(cfg.VectorRateDegreesPerSecond * 0.1f).Within(0.01f));
        }

        /// <summary>
        /// Diagonal and straight commands must swing at the same speed. Moving the two angles
        /// independently would send a diagonal 1.41x faster for no reason a player could see.
        /// </summary>
        [Test]
        public void DiagonalSwingsNoFasterThanStraight()
        {
            JetpackConfig cfg = Config();

            JetNozzle straight = JetpackVector.Advance(
                JetNozzle.Vertical, JetpackVector.Command(Vector2.up, 0f, cfg), cfg, 0.05f);

            JetNozzle diagonal = JetpackVector.Advance(
                JetNozzle.Vertical,
                JetpackVector.Command(new Vector2(1f, 1f).normalized, 0f, cfg), cfg, 0.05f);

            Assert.That(diagonal.Magnitude, Is.EqualTo(straight.Magnitude).Within(0.01f));
        }

        /// <summary>
        /// THE assertion. On the frame of the press the nozzles have barely moved, so the thrust
        /// is still essentially straight up — a jetpack that answered the key directly would push
        /// the player forward on frame one and have no learning curve at all.
        /// </summary>
        [Test]
        public void ThrustFollowsTheNozzleNotTheKey()
        {
            JetpackConfig cfg = Config();
            JetNozzle command = JetpackVector.Command(Vector2.up, 0f, cfg);

            JetNozzle oneFrame = JetpackVector.Advance(JetNozzle.Vertical, command, cfg, 1f / 50f);

            Assert.That(oneFrame.Magnitude, Is.LessThan(command.Magnitude * 0.25f),
                        "one frame after the press the nozzles are nowhere near the command");

            Vector3 thrust = oneFrame.LocalThrust;
            Assert.That(thrust.y, Is.GreaterThan(0.98f),
                        "so the push is still essentially straight up");
        }

        [Test]
        public void NozzlesReachTheCommandEventually()
        {
            JetpackConfig cfg = Config();
            JetNozzle command = JetpackVector.Command(Vector2.up, 0f, cfg);
            JetNozzle nozzle = JetNozzle.Vertical;

            for (int i = 0; i < 200; i++)
                nozzle = JetpackVector.Advance(nozzle, command, cfg, 1f / 50f);

            Assert.That(nozzle.Pitch, Is.EqualTo(command.Pitch).Within(0.01f));
        }

        /// <summary>
        /// The wire round trip. The deflection crosses the network as the rotation it is, and a
        /// peer's pods must point exactly where the owner's do.
        /// </summary>
        [Test]
        public void NozzleSurvivesARotationRoundTrip()
        {
            var sent = new JetNozzle { Pitch = 23.5f, Roll = -17.25f };
            JetNozzle received = JetNozzle.FromRotation(sent.Rotation);

            Assert.That(received.Pitch, Is.EqualTo(sent.Pitch).Within(0.05f));
            Assert.That(received.Roll, Is.EqualTo(sent.Roll).Within(0.05f));
        }

        /// <summary>
        /// The nozzles are in the WEARER's frame, so turning the body turns the thrust. That is
        /// the whole yaw model — there is no rudder.
        /// </summary>
        [Test]
        public void HeadingTurnsTheThrust()
        {
            var nozzle = new JetNozzle { Pitch = 30f };

            Vector3 north = JetpackVector.WorldThrust(nozzle, 0f);
            Vector3 east = JetpackVector.WorldThrust(nozzle, 90f);

            Assert.That(north.z, Is.GreaterThan(0.1f));
            Assert.That(east.x, Is.GreaterThan(0.1f));
            Assert.That(east.y, Is.EqualTo(north.y).Within(1e-4f),
                        "turning must not change how hard it lifts");
        }
    }
}
