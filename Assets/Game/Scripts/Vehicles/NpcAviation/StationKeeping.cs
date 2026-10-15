// Keeping a station on something that moves: where to aim and how hard to push. Pure, shared by the
// drifting fleet's escorts (FleetEscortModule) and NPC fliers flying formation (EscortSteering), so
// the two never disagree about what "keeping up" means.
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public static class StationKeeping
    {
        /// <summary>
        /// The station point moved on by the anchor's own motion over <paramref name="leadSeconds"/>, so a
        /// follower aiming at it does not trail its station by speed ÷ gain.
        /// </summary>
        public static Vector3 Led(Vector3 stationPoint, Vector3 anchorVelocity, float leadSeconds) =>
            stationPoint + anchorVelocity * Mathf.Max(0f, leadSeconds);

        /// <summary>
        /// The share of its top speed a follower <paramref name="distance"/> metres off its station asks for:
        /// <paramref name="gain"/> metres per second per metre off, clamped to [min, max]. With no top speed
        /// there is nothing to share out, so it asks for max.
        /// </summary>
        public static float SpeedFraction(float distance, float gain, float topSpeed, float min, float max)
        {
            if (topSpeed <= 0f) return max;
            return Mathf.Clamp(distance * gain / topSpeed, min, max);
        }
    }
}
