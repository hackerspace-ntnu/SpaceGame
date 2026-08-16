// Where the body goes.
//
// NOTHING THAT MOVES AT STRIDE FREQUENCY MAY GO THROUGH A FILTER. A first-order smooth costs both
// amplitude and phase at the frequency it is asked to pass, so a bob or a lean routed through one
// arrives late and shallow, and how late depends on speed -- which is most of what "the legs lag
// behind" turned out to mean. The base's `heightSmooth` is kept, but only for the GROUND, which
// genuinely is noise: a foot onto a rock, a ray catching an edge. Everything here is added on top
// of that, unfiltered and exactly in phase with the footfalls it belongs to.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// Ride at a fixed height above the planted feet, with the deck held FLATTER THAN THE GROUND
    /// rather than perfectly flat. For a crewed platform: the legs absorb most of the terrain so
    /// riders are not tipped around, which is the point of building a station on legs at all.
    ///
    /// The "rather than perfectly flat" is the part that was learned the hard way. A deck pinned
    /// dead level puts every hip at one height while the feet are at several, so crossing a slope
    /// asks the downhill legs to span the ride height PLUS the whole drop. Past their reach they
    /// stop arriving, and the machine walks along on its uphill legs with the downhill ones hanging
    /// in the air. Following even part of the slope moves each hip toward its own foot, and it is
    /// the same motion a real suspension makes.
    public class LevelDeckBody : IBodyMotion
    {
        private readonly float slopeFollow;
        private readonly float maxTilt;
        private readonly float tiltSmooth;

        private Quaternion tilt = Quaternion.identity;
        private bool primed;

        /// `slopeFollow` is how much of the ground's tilt the deck takes on: 0 is rigidly level and
        /// will strand legs on any slope worth the name, 1 lies flat on the hillside and tips the
        /// crew off. `maxTilt` caps it in degrees however steep the ground gets, which is what keeps
        /// a deck walkable when the machine crosses something it should probably have gone around.
        public LevelDeckBody(float slopeFollow = 0.6f, float maxTilt = 12f, float tiltSmooth = 6f)
        {
            this.slopeFollow = slopeFollow;
            this.maxTilt = maxTilt;
            this.tiltSmooth = tiltSmooth;
        }

        public void Pose(in BodyFrame f, in SupportState s, float dt, ref BodyPose pose)
        {
            pose.PathPos.y = s.SupportHeight + f.RideHeight;

            // Smoothed, unlike anything the gait drives. The support plane steps whenever a foot is
            // planted or lifted, and a deck that snapped to each of those would shudder -- but this
            // is terrain, not stride frequency, so a filter is the right tool here.
            Quaternion wanted = s.Plane.Tilt(slopeFollow, maxTilt);
            tilt = primed
                ? Quaternion.Slerp(tilt, wanted, 1f - Mathf.Exp(-tiltSmooth * dt))
                : wanted;
            primed = true;

            pose.Attitude = Quaternion.AngleAxis(pose.Yaw, Vector3.up) * tilt;
            pose.DisplayOffset = Vector3.zero;
        }
    }

    /// Bobs, sways over the stance foot, and pitches toward horizontal as it speeds up. That motion
    /// is most of what reads as an animal rather than a table walking.
    ///
    /// Balance is procedural, not physical: the body follows curves driven by the gait clock rather
    /// than solving a real centre-of-mass problem. A real balance solve fights the rider for control
    /// and falls over when it loses; this reads correct and stays steerable.
    public class BobbingBody : IBodyMotion
    {
        private readonly float bobAmount;
        private readonly float swayAmount;
        private readonly float runPitch;
        private readonly float turnRoll;
        private readonly float attitudeSmooth;
        private readonly float slopeFollow;
        private readonly float maxTilt;

        private float smoothedPitch;
        private float smoothedRoll;
        private Quaternion tilt = Quaternion.identity;
        private bool primed;

        /// The lean, HELD rather than filtered: it is read straight off the load, and only carried
        /// over through the moments -- a run's flight phase -- when there is no load to read it from.
        private float swayHold;

        /// `slopeFollow` and `maxTilt` are the same terrain-following tunables LevelDeckBody has,
        /// and an animal wants MORE of it than a crewed deck does: nothing is standing on its back
        /// to be tipped off, and a body that stays rigidly level while crossing a hillside strands
        /// its downhill leg exactly as a deck does.
        public BobbingBody(float bobAmount, float swayAmount, float runPitch, float turnRoll,
                           float attitudeSmooth = 8f, float slopeFollow = 0.8f, float maxTilt = 25f)
        {
            this.bobAmount = bobAmount;
            this.swayAmount = swayAmount;
            this.runPitch = runPitch;
            this.turnRoll = turnRoll;
            this.attitudeSmooth = attitudeSmooth;
            this.slopeFollow = slopeFollow;
            this.maxTilt = maxTilt;
        }

        public void Pose(in BodyFrame f, in SupportState s, float dt, ref BodyPose pose)
        {
            pose.PathPos.y = s.SupportHeight + f.RideHeight;

            // Twice the stride frequency, lowest just as a foot takes the load. Sized against LEG
            // REACH rather than ride height: a model whose origin sits at foot level has a ride
            // height of ~0, and a bob scaled by that would not exist.
            float bob = -Mathf.Cos(2f * Mathf.PI * 2f * (f.Phase - f.Duty))
                        * bobAmount * f.LegReach * f.Effort;

            // Lean over whichever foot is carrying the weight, in proportion to how much of it that
            // foot has. With one foot loaded the weighted mean is that foot's offset whatever its
            // share, so the lean holds steady as the last foot unloads and simply stays where it
            // was through a flight phase, where there is no load to read at all.
            if (s.Load > 1e-4f) swayHold = s.LeanX / s.Load * swayAmount * f.Effort;

            float targetPitch = -runPitch * f.RunBlend;
            float targetRoll = turnRoll *
                Mathf.Clamp(f.CommandedYawRate / Mathf.Max(f.MaxYawRate, 1e-4f), -1f, 1f);
            smoothedPitch = Mathf.Lerp(smoothedPitch, targetPitch, 1f - Mathf.Exp(-attitudeSmooth * dt));
            smoothedRoll = Mathf.Lerp(smoothedRoll, targetRoll, 1f - Mathf.Exp(-attitudeSmooth * dt));

            // The ground's own tilt, under the gait's pitch and roll rather than instead of them:
            // the machine still leans into its turns and pitches into a run, it just does so about
            // the slope it is standing on rather than about the horizontal.
            Quaternion wantedTilt = s.Plane.Tilt(slopeFollow, maxTilt);
            tilt = primed
                ? Quaternion.Slerp(tilt, wantedTilt, 1f - Mathf.Exp(-attitudeSmooth * dt))
                : wantedTilt;
            primed = true;

            Quaternion yaw = Quaternion.AngleAxis(pose.Yaw, Vector3.up);
            pose.Attitude = yaw * tilt * Quaternion.Euler(smoothedPitch, 0f, -smoothedRoll);
            pose.DisplayOffset = Vector3.up * bob + yaw * Vector3.right * swayHold;
        }
    }
}
