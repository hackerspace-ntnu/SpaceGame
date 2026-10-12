// A tracked hull's handling as pure numbers: how far the heading turns this step, what speed it wants
// with a stop or a turn ahead, how the speed eases toward that, and what pitch/roll and height the
// ground under its footprint asks for. TrackedHullMotor applies them to the transform. Nothing here
// touches a scene, so it is tested alone (TrackedHullDriveTests).
//
// A tracked hull steers by skid: it turns on the spot and never needs speed to turn, so heading and
// speed are independent steps -- unlike a wheel, whose turn rate falls with speed.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Vehicles.Motors
{
    [System.Serializable]
    public struct TrackedHullSettings
    {
        [Tooltip("Heading change on the spot or on the move, deg/s.")] public float turnRate;
        [Tooltip("Top speed along the heading, m/s. Also the IMovementMotor TopSpeed.")] public float cruiseSpeed;
        [Tooltip("m/s^2 gained while below the wanted speed.")] public float acceleration;
        [Tooltip("m/s^2 shed while above the wanted speed; also sizes the stopping curve into a destination.")]
        public float braking;
        [Tooltip("Heading error (degrees) at which the hull wants no speed at all, so it turns on the spot " +
                 "first. Below it the wanted speed falls off linearly with the error.")]
        public float alignAngle;

        // Floors for Validate: below these the hull cannot turn, move or stop.
        private const float MinRate = 0.01f;
        private const float MinAlignAngle = 1f;

        /// Clamp to the floors. For the owning motor's OnValidate.
        public void Validate()
        {
            turnRate = Mathf.Max(MinRate, turnRate);
            cruiseSpeed = Mathf.Max(MinRate, cruiseSpeed);
            acceleration = Mathf.Max(MinRate, acceleration);
            braking = Mathf.Max(MinRate, braking);
            alignAngle = Mathf.Max(MinAlignAngle, alignAngle);
        }
    }

    /// <summary>The ground a footprint rests on: height under the pivot and the tilt, in the hull's yaw frame.</summary>
    public readonly struct HullFooting
    {
        public readonly float Height;
        public readonly Quaternion Tilt;

        public HullFooting(float height, Quaternion tilt)
        {
            Height = height;
            Tilt = tilt;
        }
    }

    public static class TrackedHullDrive
    {
        /// <summary>
        /// Compass bearing (degrees clockwise from +Z, the same convention as a transform's yaw) from
        /// <paramref name="from"/> to <paramref name="to"/>, or <paramref name="fallbackDeg"/> when they
        /// share a column.
        /// </summary>
        public static float Bearing(Vector3 from, Vector3 to, float fallbackDeg)
        {
            float x = to.x - from.x;
            float z = to.z - from.z;
            return x * x + z * z > 1e-6f ? Mathf.Atan2(x, z) * Mathf.Rad2Deg : fallbackDeg;
        }

        /// <summary>The heading after turning toward <paramref name="bearingDeg"/> for one step, the short way, without overshoot.</summary>
        public static float NextHeading(float headingDeg, float bearingDeg, float deltaTime, in TrackedHullSettings s) =>
            Mathf.MoveTowardsAngle(headingDeg, bearingDeg, s.turnRate * deltaTime);

        /// <summary>
        /// The speed to aim for with <paramref name="remainingDistance"/> left before the stop point and
        /// the target <paramref name="headingErrorDeg"/> off the nose: the cruise speed (scaled by the
        /// order's multiplier, where 0 or less means full speed), cut by the heading error so the hull
        /// turns before it drives, and capped by the speed it can still brake from in the distance left.
        /// </summary>
        public static float WantedSpeed(float remainingDistance, float headingErrorDeg, float speedMultiplier,
                                        in TrackedHullSettings s)
        {
            if (!(remainingDistance > 0f)) return 0f;

            float multiplier = speedMultiplier <= 0f ? 1f : Mathf.Clamp01(speedMultiplier);
            float aligned = Mathf.Clamp01(1f - Mathf.Abs(headingErrorDeg) / Mathf.Max(1f, s.alignAngle));
            float stopping = Mathf.Sqrt(2f * s.braking * remainingDistance);
            return Mathf.Min(s.cruiseSpeed * multiplier * aligned, stopping);
        }

        /// <summary>Ease <paramref name="speed"/> toward <paramref name="wanted"/>: up at acceleration, down at braking, never past cruise or backward.</summary>
        public static float NextSpeed(float speed, float wanted, float deltaTime, in TrackedHullSettings s)
        {
            wanted = Mathf.Clamp(wanted, 0f, s.cruiseSpeed);
            float rate = wanted > speed ? s.acceleration : s.braking;
            return Mathf.MoveTowards(speed, wanted, rate * deltaTime);
        }

        /// <summary>
        /// The footing under a hull from ground samples given in its YAW frame (x right, z forward, y the
        /// ground height, relative to the pivot's column). False only when there are no samples at all.
        ///
        /// Tilted, the hull lies on the least-squares plane through the samples (capped at
        /// <paramref name="maxTilt"/> degrees) and the height is that plane's under the pivot. Level --
        /// asked for, or forced because the samples span no plane (fewer than three, or in a line) -- it
        /// rests on the HIGHEST sample instead: a rigid hull held flat over a crest settles on the crest,
        /// and the average would bury it there.
        /// </summary>
        public static bool TryFooting(Vector3[] samples, int count, bool tilt, float maxTilt, out HullFooting footing)
        {
            footing = default;
            if (samples == null || count <= 0 || count > samples.Length) return false;

            if (tilt && WalkerSupportPlane.TryFit(samples, count, out WalkerSupportPlane plane))
            {
                footing = new HullFooting(plane.Height, plane.Tilt(1f, maxTilt));
                return true;
            }

            float highest = float.NegativeInfinity;
            for (int i = 0; i < count; i++) highest = Mathf.Max(highest, samples[i].y);
            footing = new HullFooting(highest, Quaternion.identity);
            return true;
        }

        /// <summary>Frame-rate independent exponential approach: the fraction of the gap closed in one step at <paramref name="sharpness"/> (1/s).</summary>
        public static float SmoothFactor(float sharpness, float deltaTime) =>
            sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
