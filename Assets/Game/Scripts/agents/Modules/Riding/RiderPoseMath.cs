// How the rider's spine answers the mount's motion, as arithmetic with no Unity object attached.
//
// Split out of MountedRiderPose so the clamps can be tested directly. Clamps are the part that
// actually matters here: an unbounded gain on a measured velocity is one bad frame — a physics
// step through a chunk load, a teleport, a leap landing — away from folding the rider's spine
// through their own pelvis, and it is not the kind of thing a playtest reliably catches.
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Gains and limits for the three ways the rider answers the mount, in degrees and
    /// degrees-per-(metre/second).
    /// </summary>
    [System.Serializable]
    public struct RiderPoseGains
    {
        [Tooltip("Degrees of counter-lean per m/s of the mount's vertical speed. The rider gives " +
                 "with the stride instead of riding it rigid.")]
        public float bounceGain;
        public float bounceMax;

        [Tooltip("Degrees of forward lean per m/s of ground speed.")]
        public float speedGain;
        public float speedMax;

        [Tooltip("Degrees of roll per 100°/s of the mount's yaw rate — the rider banks into a turn.")]
        public float turnGain;
        public float turnMax;

        public static RiderPoseGains Default => new RiderPoseGains
        {
            bounceGain = 2.5f,
            bounceMax = 9f,
            speedGain = 0.7f,
            speedMax = 12f,
            turnGain = 6f,
            turnMax = 10f,
        };
    }

    public static class RiderPoseMath
    {
        /// <summary>
        /// The rider's motion response, as Euler degrees to add to the authored spine pose.
        /// X is pitch (positive = lean forward), Z is roll (positive = lean right).
        /// </summary>
        /// <param name="verticalSpeed">Mount's world vertical speed, m/s. Positive = rising.</param>
        /// <param name="forwardSpeed">Mount's ground speed along its own forward, m/s.</param>
        /// <param name="turnRate">Mount's yaw rate, degrees/s. Positive = turning right.</param>
        public static Vector3 SpineOffset(float verticalSpeed, float forwardSpeed, float turnRate,
                                          in RiderPoseGains gains)
        {
            // Rising mount pushes the rider down into the saddle, so they fold forward; falling
            // away from under them throws them back. Hence the sign: it is inertia, not a pose.
            float bounce = Clamp(verticalSpeed * gains.bounceGain, gains.bounceMax);

            // Only forward motion leans the rider forward. Reversing does not lean them backwards
            // — a mount backing up is a slow, deliberate manoeuvre and leaning out of it looks like
            // the rider is being shoved.
            float speedLean = Clamp(Mathf.Max(0f, forwardSpeed) * gains.speedGain, gains.speedMax);

            // Per 100°/s, so the serialized number stays a readable single digit.
            float roll = Clamp(turnRate * 0.01f * gains.turnGain, gains.turnMax);

            return new Vector3(bounce + speedLean, 0f, roll);
        }

        private static float Clamp(float value, float limit)
        {
            limit = Mathf.Abs(limit);
            return Mathf.Clamp(value, -limit, limit);
        }
    }
}
