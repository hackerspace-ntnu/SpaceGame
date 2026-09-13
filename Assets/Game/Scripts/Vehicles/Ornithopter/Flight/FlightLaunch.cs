using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// How the motion a pilot already had becomes the flight state a wing starts on.
    ///
    /// <para>
    /// Extracted from <c>WingPackItem</c> when the wingsuit needed the identical arithmetic. It
    /// belongs with the flight model rather than with either item: the question "what is this
    /// pilot's motion worth to a wing" is the same question whether the wing is a 10 m airframe
    /// spawned under them or a membrane strapped to their arms, and an item asking another item
    /// for the answer would be the wrong shape entirely.
    /// </para>
    /// </summary>
    public static class FlightLaunch
    {
        /// <summary>What the pilot's motion is worth to the launch: a direction, a speed, a climb.</summary>
        public readonly struct LaunchCarry
        {
            /// <summary>Flattened unit heading the wing starts on.</summary>
            public readonly Vector3 Forward;

            /// <summary>Airspeed carried in, m/s, before any floor the wing imposes.</summary>
            public readonly float Speed;

            /// <summary>Flight path carried in, degrees, + climbing. Unclamped.</summary>
            public readonly float ClimbDegrees;

            public LaunchCarry(Vector3 forward, float speed, float climbDegrees)
            {
                Forward = forward;
                Speed = speed;
                ClimbDegrees = climbDegrees;
            }
        }

        /// <summary>
        /// Turn how the pilot was moving into how the wing starts flying.
        ///
        /// <para>
        /// The heading comes from where the pilot is GOING when they are going anywhere fast enough
        /// for that to be an answer, and from where they are LOOKING otherwise. Facing alone was the
        /// old rule and it quietly minted energy once a grapple swing could reach the launch: a
        /// pilot arcing sideways at 25 m/s while looking forward had the whole 25 projected onto
        /// their nose, which made twisting the camera mid-arc worth more than flying well. Reading
        /// the direction off the same vector the speed is measured from means the launch can only
        /// ever hand back motion the pilot actually had.
        /// </para>
        /// <para>
        /// Below <paramref name="headingFromSpeed"/> there is no direction in the velocity worth
        /// reading — a pilot stepping off a ledge is falling and nothing else — so facing is the
        /// only intent there is, and the wing's own launch floor supplies the speed anyway.
        /// </para>
        /// </summary>
        public static LaunchCarry CarryFrom(Vector3 velocity, Vector3 facing, float speedCarry,
                                            float headingFromSpeed)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 forward = flat.magnitude >= headingFromSpeed ? flat : Flatten(facing);

            float speed = velocity.magnitude;
            float climb = speed > 0.01f
                ? Mathf.Asin(Mathf.Clamp(velocity.y / speed, -1f, 1f)) * Mathf.Rad2Deg
                : 0f;

            return new LaunchCarry(Flatten(forward), speed * speedCarry, climb);
        }

        /// <summary>The compass bearing of a direction, in the degrees the flight model measures
        /// heading in: from +Z toward +X.</summary>
        public static float HeadingOf(Vector3 direction) =>
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>A direction laid flat, with a fallback for one pointing straight up or down.</summary>
        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;
        }
    }
}
