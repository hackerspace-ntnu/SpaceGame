// The monowheel's handling as pure numbers: speed from throttle, heading from steer, how much of a
// slide the sand gives back, how far the chassis leans into a turn, and what speed an NPC driver
// should want with a corner or a stop ahead. MonowheelMotor applies these to the Rigidbody;
// MonowheelLean applies the lean to the art. Nothing here touches a scene, so it is tested alone.
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [System.Serializable]
    public struct MonowheelDriveSettings
    {
        [Tooltip("Forward speed at full throttle, m/s.")] public float topSpeed;
        [Tooltip("Top speed backing up, m/s.")] public float reverseSpeed;
        [Tooltip("m/s^2 under throttle.")] public float acceleration;
        [Tooltip("m/s^2 under brake.")] public float braking;
        [Tooltip("m/s^2 lost rolling with no input.")] public float coastDrag;
        [Tooltip("Heading change at a crawl, deg/s.")] public float turnRate;
        [Tooltip("Heading change at top speed, deg/s.")] public float turnRateAtTop;
        [Tooltip("How fast sideways slide is bled off, 1/s. Low = more slide on sand.")] public float lateralGrip;
        [Tooltip("Largest chassis roll into a turn, degrees.")] public float maxLean;
        [Tooltip("Degrees of roll per m/s^2 of lateral acceleration.")] public float leanPerLateralAccel;
        [Tooltip("Turn ahead (degrees) at which an NPC driver is down to its slowest corner speed.")] public float cornerSlowAngle;
        [Tooltip("Slowest corner speed, as a fraction of top speed.")] public float cornerMinSpeedFraction;
        [Tooltip("Heading error (degrees) at which an NPC driver wants no speed at all. Below it the wanted speed falls " +
                 "off linearly, so a target behind the wheel is turned toward before it is driven at, not orbited.")]
        public float alignAngle;
    }

    public static class MonowheelDrive
    {
        public static float NextSpeed(float speed, float throttle, float deltaTime, in MonowheelDriveSettings s)
        {
            throttle = Mathf.Clamp(throttle, -1f, 1f);
            if (throttle > 0f) return Mathf.MoveTowards(speed, s.topSpeed * throttle, s.acceleration * deltaTime);
            if (throttle < 0f)
                return speed > 0f
                    ? Mathf.MoveTowards(speed, 0f, s.braking * deltaTime)
                    : Mathf.MoveTowards(speed, -s.reverseSpeed * -throttle, s.acceleration * deltaTime);
            return Mathf.MoveTowards(speed, 0f, s.coastDrag * deltaTime);
        }

        public static float TurnRate(float speed, in MonowheelDriveSettings s) =>
            Mathf.Lerp(s.turnRate, s.turnRateAtTop, Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.01f, s.topSpeed)));

        public static float NextHeading(float headingDeg, float steer, float speed, float deltaTime, in MonowheelDriveSettings s)
        {
            float direction = speed < 0f ? -1f : 1f;
            return headingDeg + Mathf.Clamp(steer, -1f, 1f) * TurnRate(speed, s) * deltaTime * direction;
        }

        public static Vector3 GripVelocity(Vector3 velocity, Vector3 forward, float speed, float deltaTime, in MonowheelDriveSettings s)
        {
            forward.y = 0f;
            forward.Normalize();
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 lateral = flat - forward * Vector3.Dot(flat, forward);
            lateral *= Mathf.Exp(-s.lateralGrip * deltaTime);
            Vector3 result = forward * speed + lateral;
            result.y = velocity.y;
            return result;
        }

        public static float Lean(float speed, float yawRateDegPerSec, in MonowheelDriveSettings s)
        {
            float lateralAccel = speed * yawRateDegPerSec * Mathf.Deg2Rad;
            return Mathf.Clamp(lateralAccel * s.leanPerLateralAccel, -s.maxLean, s.maxLean);
        }

        public static float WantedSpeed(float remainingDistance, float turnAheadDeg, float speedMultiplier, in MonowheelDriveSettings s)
        {
            float cornerFactor = Mathf.Lerp(1f, s.cornerMinSpeedFraction,
                                            Mathf.Clamp01(Mathf.Abs(turnAheadDeg) / Mathf.Max(1f, s.cornerSlowAngle)));
            // An unset multiplier (<= 0) means full speed -- LeggedDriver's `intent.SpeedMultiplier <= 0f ? 1f` convention.
            float cruise = s.topSpeed * Mathf.Clamp01(speedMultiplier <= 0f ? 1f : speedMultiplier) * cornerFactor;
            float stopping = Mathf.Sqrt(2f * s.braking * Mathf.Max(0f, remainingDistance));
            return Mathf.Min(cruise, stopping);
        }

        /// <summary>
        /// Throttle (-1..1) and steer (-1..1) for an NPC driver heading for steerAt. The wanted speed
        /// is scaled down by how far off the heading the target lies, so a wheel facing away from it
        /// turns first instead of accelerating round it in a circle.
        /// </summary>
        public static (float throttle, float steer) NpcInput(float headingDeg, float speed, Vector3 position,
                                                             Vector3 steerAt, float wantedSpeed,
                                                             in MonowheelDriveSettings s)
        {
            Vector3 to = steerAt - position;
            to.y = 0f;
            float bearing = to.sqrMagnitude > 1e-4f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : headingDeg;
            float error = Mathf.DeltaAngle(headingDeg, bearing);
            // headingDeg is clockwise from +Z, same convention Atan2(x, z) gives, and the same one
            // LeggedDriver's HeadingErrorTo/SignedAngle(forward, to, Vector3.up) uses: a target to
            // +X of heading 0 gives a positive error. WalkerSteering.Turn returns positive to turn
            // toward a positive error (checked against NpcInput_SteersTowardTheTarget, which wants
            // steer > 0 for a target to the right) -- no sign flip needed.
            float steer = SpaceGame.Locomotion.WalkerSteering.Turn(error);
            float aligned = wantedSpeed * Mathf.Clamp01(1f - Mathf.Abs(error) / Mathf.Max(1f, s.alignAngle));
            float throttle = speed < aligned - SpeedBand ? 1f : speed > aligned + SpeedBand ? -1f : 0f;
            return (throttle, steer);
        }

        /// <summary>m/s either side of the wanted speed where an NPC neither throttles nor brakes.</summary>
        private const float SpeedBand = 0.5f;
    }
}
