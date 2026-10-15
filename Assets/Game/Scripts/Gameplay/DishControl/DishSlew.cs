using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The dish's motor, as arithmetic: a commanded direction becomes a velocity that ramps toward the
    /// motor's top speed, and a limited axis brakes early enough to come to rest ON its limit rather than
    /// hitting it. Pure, so the server's tick and the tests run the same code.
    /// </summary>
    public static class DishSlew
    {
        public const float FullTurn = 360f;

        /// <summary>Advance one axis by <paramref name="dt"/> seconds under a command of -1..1.</summary>
        public static SlewAxis Step(SlewAxis axis, float command, float dt, in SlewMotor motor)
        {
            if (dt <= 0f) return axis;

            float wanted = Mathf.Clamp(command, -1f, 1f) * motor.MaxSpeed;

            if (!motor.Wraps)
            {
                // The fastest speed from which the motor can still stop before the limit: v^2 = 2 a d.
                float upCap = Mathf.Sqrt(2f * motor.Acceleration * Mathf.Max(0f, motor.Max - axis.Angle));
                float downCap = Mathf.Sqrt(2f * motor.Acceleration * Mathf.Max(0f, axis.Angle - motor.Min));
                wanted = Mathf.Clamp(wanted, -downCap, upCap);
            }

            axis.Velocity = Mathf.MoveTowards(axis.Velocity, wanted, motor.Acceleration * dt);
            axis.Angle = Clamp(axis.Angle + axis.Velocity * dt, motor);

            if (!motor.Wraps && (axis.Angle >= motor.Max && axis.Velocity > 0f ||
                                 axis.Angle <= motor.Min && axis.Velocity < 0f))
                axis.Velocity = 0f;

            return axis;
        }

        /// <summary>Where <paramref name="angle"/> is allowed to be on this axis.</summary>
        public static float Clamp(float angle, in SlewMotor motor) =>
            motor.Wraps ? Mathf.Repeat(angle, FullTurn) : Mathf.Clamp(angle, motor.Min, motor.Max);
    }
}
