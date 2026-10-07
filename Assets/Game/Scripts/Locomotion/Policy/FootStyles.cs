// What the foot does: the shape of its swing, and which way the sole faces.
//
// Articulation is done by PITCHING THE NORMAL handed to the solver, never by driving the ankle.
// The solver places the CONTACT POINT and derives the ankle from it, so the foot pivots about the
// point it is standing on -- which means the articulation costs nothing and can break nothing: a
// planted foot still cannot slip.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// A pad. Sets the sole flat on the ground and leaves it there, over a plain sine arc.
    public class FlatSole : IFootStyle
    {
        public Vector3 SwingPoint(Vector3 from, Vector3 to, float t, float lift)
            => WalkerGait.SwingPoint(from, to, t, Vector3.up, lift);

        public Vector3 SoleNormal(LegState leg, in WalkerLimbSolver.Frame frame) => leg.GroundNormal;
    }

    /// A joint. Rolls onto the toe to push off, carries the foot toed-up through the swing, and
    /// reaches toe-first for the landing.
    public class ArticulatedSole : IFootStyle
    {
        private readonly float toeOffAngle;
        private readonly float swingToeAngle;

        /// `toeOffAngle` is how far the sole rolls onto its toe at the ends of a stance -- heel up
        /// to push off, toe down to reach for the landing, flat through the middle where the weight
        /// is. `swingToeAngle` is how far the toes lift through mid-swing to clear the ground the
        /// foot is crossing; too much and it high-steps like a dressage horse.
        public ArticulatedSole(float toeOffAngle, float swingToeAngle)
        {
            this.toeOffAngle = toeOffAngle;
            this.swingToeAngle = swingToeAngle;
        }

        /// The horizontal travel is eased at both ends, which is what makes a foot plant rather
        /// than skid: it arrives at the ground with no speed of its own, and a planted foot is
        /// fixed in the world.
        ///
        /// The LIFT is where this differs from a plain sine arc, which peaks at mid-swing and comes
        /// down at its full vertical speed, so the foot stabs at the ground. Running the arc's
        /// clock through t(2-t) puts the apex at 29% and brings the descent to a stop exactly at
        /// touchdown -- the foot snaps up off the ground behind the machine, hangs, and is PLACED
        /// rather than dropped. It is also what an ostrich does.
        public Vector3 SwingPoint(Vector3 from, Vector3 to, float t, float lift)
            => Vector3.Lerp(from, to, t * t * (3f - 2f * t))
               + Vector3.up * (Mathf.Sin(Mathf.PI * t * (2f - t)) * lift);

        /// One curve covers the whole cycle and it is continuous across both handovers, which is
        /// what stops the foot flicking as it lifts or lands:
        ///
        ///   stance -- toe down as the weight arrives and again as it leaves, flat while carried
        ///   swing  -- leaves at that same toe-down, lifts the toes to clear the ground it is
        ///             crossing, and returns to it to reach for the landing
        public Vector3 SoleNormal(LegState leg, in WalkerLimbSolver.Frame frame)
        {
            float pitch = leg.Swinging
                ? Mathf.Lerp(toeOffAngle, -swingToeAngle, Mathf.Sin(Mathf.PI * leg.SwingT))
                : toeOffAngle * (1f - leg.Load);

            // The axle every pitch joint turns about: across the limb's own plane. Rotating the
            // normal about it keeps the tilt fore-and-aft, which is the only direction the ankle
            // can answer.
            Vector3 axle = Vector3.Cross(frame.YawAxis, frame.RestFwd);
            return axle.sqrMagnitude > 1e-8f
                ? Quaternion.AngleAxis(pitch, axle) * leg.GroundNormal
                : leg.GroundNormal;
        }
    }
}
