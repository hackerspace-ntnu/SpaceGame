// Kinematic flight for NPC sky vessels. No rigidbody forces: the server moves the hull directly
// and NetworkTransform carries it to clients, so the rules are pure and ticked by VesselPilot.
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public static class VesselFlightMath
    {
        /// <summary>
        /// The altitude to hold: <paramref name="clearance"/> above the higher of the ground below and
        /// the ground ahead, so a ridge is climbed for before the hull reaches it.
        /// <paramref name="floor"/> is the least clearance ever used, whatever the Inspector says.
        /// </summary>
        public static float CruiseAltitude(float groundBelow, float groundAhead, float clearance, float floor) =>
            Mathf.Max(groundBelow, groundAhead) + Mathf.Max(clearance, floor);

        /// <summary>
        /// One tick of flying at <paramref name="target"/>: accelerate toward the speed that can still
        /// brake to a stop at <paramref name="accel"/> in the distance left, capped at
        /// <paramref name="maxSpeed"/>. A step whose progress toward the target would reach or pass it ends
        /// on it at rest.
        /// </summary>
        public static (Vector3 position, Vector3 velocity) Step(Vector3 position, Vector3 velocity, Vector3 target,
                                                                float maxSpeed, float accel, float dt)
        {
            Vector3 toTarget = target - position;
            float distance = toTarget.magnitude;
            if (distance <= Vector3.kEpsilon) return (target, Vector3.zero);

            float brakingSpeed = Mathf.Sqrt(2f * accel * distance);
            Vector3 desired = toTarget / distance * Mathf.Min(maxSpeed, brakingSpeed);
            velocity = Vector3.MoveTowards(velocity, desired, accel * dt);

            // Only progress TOWARD the target can reach it; a step moving away or sideways never snaps.
            Vector3 step = velocity * dt;
            if (Vector3.Dot(step, toTarget) / distance >= distance) return (target, Vector3.zero);
            return (position + step, velocity);
        }

        /// <summary>
        /// Yaw toward <paramref name="flatDirection"/> at <paramref name="degreesPerSecond"/>, ignoring
        /// its vertical part. A direction with no horizontal part leaves the rotation unchanged.
        /// </summary>
        public static Quaternion TurnToward(Quaternion rotation, Vector3 flatDirection, float degreesPerSecond, float dt)
        {
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude <= Vector3.kEpsilon) return rotation;

            Quaternion facing = Quaternion.LookRotation(flatDirection, Vector3.up);
            return Quaternion.RotateTowards(rotation, facing, degreesPerSecond * dt);
        }
    }
}
