// The shape of the can's spray, as pure geometry.
//
// Separated from the artifact because it is the one part of the item with no Unity state in it at
// all: given a direction, an index and a cone, it says which way one dab goes. That makes it
// readable on its own and testable without a hand, a hold stream or a session.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where each dab of the slick can's fan goes.
    ///
    /// <para>
    /// The can lays ONE dab per hold tick and walks this pattern a step at a time, rather than
    /// casting the whole fan every tick. Both paint the same band — the coat field grows and
    /// refreshes a patch a later dab lands on, so a swept fan and a fired fan converge on the same
    /// footprint — but every dab is a message, and one per tick is the rate every other continuous
    /// item in this game already costs.
    /// </para>
    /// <para>
    /// The samples are placed on the cone the way a sunflower places seeds: the radius grows as the
    /// square root of the index so the dabs cover the disc evenly rather than crowding its middle,
    /// and each is spun a golden angle on from the last so that CONSECUTIVE dabs land on opposite
    /// sides of the fan. That second property is what a walked pattern needs and a fired one does
    /// not: stepping through indices in order paints the whole width immediately, instead of
    /// crawling along one edge of the fan and only reaching the other edge a fifth of a second
    /// later.
    /// </para>
    /// </summary>
    public static class SlickCanFan
    {
        /// <summary>
        /// The golden angle, in degrees. The turn between consecutive samples: it is the one angle
        /// that never repeats a previous direction however many samples are asked for, which is
        /// what keeps the pattern even at three dabs and at thirty.
        /// </summary>
        public const float GoldenAngleDegrees = 137.50776f;

        /// <summary>
        /// Which way dab number <paramref name="index"/> of <paramref name="count"/> leaves the
        /// nozzle, given the direction the can is pointed and the fan's half-angle.
        ///
        /// <para>
        /// The index wraps, so a caller may keep a cursor that only ever counts up. A degenerate
        /// fan — one sample, or no spread — answers with the aim itself rather than with nothing,
        /// because a can whose fan has been tuned to a point should still spray.
        /// </para>
        /// </summary>
        public static Vector3 Direction(Vector3 forward, int index, int count, float halfAngleDegrees)
        {
            Vector3 axis = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;

            if (count <= 1 || halfAngleDegrees <= 0f) return axis;

            int step = ((index % count) + count) % count;

            // Square root, not the raw share: area grows with the square of the radius, so a linear
            // ramp would put half the dabs in the middle tenth of the fan and leave its rim bare.
            // The half-step offset keeps the first sample off the exact centre line, where it would
            // waste a dab the neighbouring ones already cover.
            float spread = Mathf.Sqrt((step + 0.5f) / count);

            float tilt = spread * halfAngleDegrees;
            float spin = step * GoldenAngleDegrees;

            // Any axis across the aim will do — the spin below sweeps all the way round it — so the
            // only thing this has to avoid is picking one PARALLEL to the aim, which happens when
            // the player sprays straight up or straight down at their own feet. That is not an edge
            // case for this item; it is how you slick the ground you are standing on.
            Vector3 reference = Mathf.Abs(axis.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 side = Vector3.Cross(axis, reference).normalized;

            return Quaternion.AngleAxis(spin, axis) * (Quaternion.AngleAxis(tilt, side) * axis);
        }
    }
}
