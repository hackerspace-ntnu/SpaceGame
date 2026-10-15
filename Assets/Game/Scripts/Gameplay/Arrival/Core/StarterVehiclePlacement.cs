using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Where the starter vehicle is parked beside a landed hull: the maths, with no physics and no
    /// spawning in it, so it can be tested on its own.
    ///
    /// <para>
    /// The offset is in the HULL's axes: x toward the hull's right, y toward its nose. A landed
    /// hull differs from its prefab by yaw alone (the settle guarantees it — see
    /// <c>ArrivalDirector.Settle</c>), so turning the offset by that yaw is the whole transform.
    /// Pitch and roll are dropped deliberately: a hull restored askew is straightened a second later
    /// by <c>SeatedRider</c>, and a vehicle parked off a tilted frame would land in the air or in the
    /// ground.
    /// </para>
    /// </summary>
    public static class StarterVehiclePlacement
    {
        /// <summary>
        /// The ground point, in world X/Z, <paramref name="hullOffset"/> away from a hull standing at
        /// <paramref name="hullPosition"/> turned to <paramref name="hullYaw"/> degrees.
        /// </summary>
        public static Vector2 GroundPoint(Vector3 hullPosition, float hullYaw, Vector2 hullOffset)
        {
            Vector3 world = hullPosition + Quaternion.Euler(0f, hullYaw, 0f) * new Vector3(hullOffset.x, 0f, hullOffset.y);
            return new Vector2(world.x, world.z);
        }

        /// <summary>
        /// The vehicle's rotation: upright, turned <paramref name="yawFromHull"/> degrees from the
        /// hull's own heading. Zero parks it pointing the way the ship points.
        /// </summary>
        public static Quaternion Facing(float hullYaw, float yawFromHull) =>
            Quaternion.Euler(0f, hullYaw + yawFromHull, 0f);

        /// <summary>
        /// The smallest gap, in metres, between a vehicle of <paramref name="vehicleRadius"/> parked
        /// at <paramref name="hullOffset"/> and <paramref name="hullBounds"/> — the hull's solid parts
        /// measured in its own axes, ramps and stair included. Negative means they overlap.
        /// </summary>
        public static float Clearance(Vector2 hullOffset, float vehicleRadius, Bounds hullBounds)
        {
            var footprint = new Rect(hullBounds.min.x, hullBounds.min.z, hullBounds.size.x, hullBounds.size.z);

            float dx = Mathf.Max(footprint.xMin - hullOffset.x, 0f, hullOffset.x - footprint.xMax);
            float dy = Mathf.Max(footprint.yMin - hullOffset.y, 0f, hullOffset.y - footprint.yMax);

            // Inside the footprint the distance to the nearest edge is the depth of the overlap.
            if (dx == 0f && dy == 0f)
            {
                float depth = Mathf.Min(hullOffset.x - footprint.xMin, footprint.xMax - hullOffset.x,
                                        hullOffset.y - footprint.yMin, footprint.yMax - hullOffset.y);
                return -depth - vehicleRadius;
            }

            return Mathf.Sqrt(dx * dx + dy * dy) - vehicleRadius;
        }
    }
}
