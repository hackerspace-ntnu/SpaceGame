using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The satellite dish's motor: what the operator feels through the stick and what keeps the dish's
    /// beams out of the shack roof. Each is the server's tick in miniature — <see cref="DishSlew.Step"/>
    /// is the only code that moves the dish.
    /// </summary>
    public class DishControlTests
    {
        private const float Dt = 1f / 60f;

        private static readonly SlewMotor Elevation = new() { MaxSpeed = 4f, Acceleration = 3f, Min = 15f, Max = 85f };
        private static readonly SlewMotor Azimuth = new() { MaxSpeed = 6f, Acceleration = 4f, Wraps = true };

        private static SlewAxis Run(SlewAxis axis, float command, float seconds, in SlewMotor motor, ref float extreme, bool tracksMax)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                axis = DishSlew.Step(axis, command, Dt, motor);
                extreme = tracksMax ? Mathf.Max(extreme, axis.Angle) : Mathf.Min(extreme, axis.Angle);
            }
            return axis;
        }

        /// <summary>
        /// Held against a limit, the dish comes to rest ON it, never past it — below about 14 degrees
        /// the hanging beams strike the shack, so an overshoot of even a frame is a dish in the roof.
        /// </summary>
        [Test]
        public void HeldAgainstALimit_TheDishBrakesOntoItAndNeverPast()
        {
            float highest = float.MinValue;
            SlewAxis up = Run(new SlewAxis(80f), 1f, 30f, Elevation, ref highest, tracksMax: true);
            Assert.AreEqual(85f, up.Angle, 1e-3f);
            Assert.AreEqual(0f, up.Velocity, 1e-4f, "...and stands still there");
            Assert.LessOrEqual(highest, 85f);

            float lowest = float.MaxValue;
            SlewAxis down = Run(new SlewAxis(20f), -1f, 30f, Elevation, ref lowest, tracksMax: false);
            Assert.AreEqual(15f, down.Angle, 1e-3f);
            Assert.GreaterOrEqual(lowest, 15f);
        }

        /// <summary>The azimuth turns a full circle and on round, without stopping at 0 or 360.</summary>
        [Test]
        public void TheAzimuthWrapsThroughAFullTurn()
        {
            float unused = 0f;
            SlewAxis axis = Run(new SlewAxis(350f), 1f, 10f, Azimuth, ref unused, tracksMax: true);

            Assert.That(axis.Angle, Is.InRange(0f, 360f));
            Assert.Less(axis.Angle, 350f, "It wrapped past 360 rather than stopping there.");
            Assert.AreEqual(Azimuth.MaxSpeed, axis.Velocity, 1e-3f, "...at full speed, unbraked by any limit.");
        }

        /// <summary>
        /// A heavy motor: it spools up instead of jumping to speed, and coasts to a stop when the stick
        /// is released instead of halting dead — the weight the controls are meant to have.
        /// </summary>
        [Test]
        public void TheMotorSpoolsUpAndCoastsToAStop()
        {
            SlewAxis axis = new(180f);
            axis = DishSlew.Step(axis, 1f, 0.1f, Azimuth);
            Assert.Greater(axis.Velocity, 0f);
            Assert.Less(axis.Velocity, Azimuth.MaxSpeed, "Full speed on the first frame is a jump, not a motor.");

            float unused = 0f;
            axis = Run(axis, 1f, 5f, Azimuth, ref unused, tracksMax: true);
            Assert.AreEqual(Azimuth.MaxSpeed, axis.Velocity, 1e-3f);

            float released = axis.Angle;
            axis = DishSlew.Step(axis, 0f, Dt, Azimuth);
            Assert.Greater(axis.Velocity, 0f, "Released, it coasts rather than stopping dead.");

            axis = Run(axis, 0f, 5f, Azimuth, ref unused, tracksMax: true);
            Assert.AreEqual(0f, axis.Velocity, 1e-4f);
            Assert.Greater(Mathf.DeltaAngle(released, axis.Angle), 0f);
        }
    }
}
