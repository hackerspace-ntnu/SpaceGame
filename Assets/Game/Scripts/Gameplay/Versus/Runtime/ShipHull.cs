using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The two measurements a ship has to hand over before anything can put it down: how much ground
    /// it stands over, and how far its belly hangs below its own origin.
    ///
    /// <para>
    /// Both are read off the ship rather than authored beside it, because both have been wrong as
    /// authored numbers. <c>shipGroundClearance</c> was a constant that happened to suit one hull,
    /// and the footprint was a guess that did not match the one the craft's own hover sensor uses —
    /// so the arrival landed a hull at one height and the servo immediately held it at another.
    /// Asking the ship means the two cannot disagree.
    /// </para>
    /// </summary>
    public static class ShipHull
    {
        /// <summary>
        /// Half-extents of the ground a hull stands over, in its own axes.
        ///
        /// <para>
        /// Its hover sensor's footprint when it has one, and that is the point: the sensor's ring is
        /// what decides the height the craft holds once it is flying again, so a landing measured
        /// over a different patch of ground is a landing the craft corrects the moment it wakes up.
        /// Falls back to the collision bounds for a hull with no hover motor.
        /// </para>
        /// </summary>
        public static Vector2 Footprint(GameObject ship)
        {
            if (ship == null) return Vector2.zero;

            var motor = ship.GetComponent<HoverRigidbodyMotor>();
            if (motor != null) return motor.FootprintExtents;

            return TryMeasureCollision(ship, out Bounds bounds)
                ? new Vector2(bounds.extents.x, bounds.extents.z)
                : Vector2.zero;
        }

        /// <summary>
        /// How far the lowest point of the hull's collision hangs below its own origin, in metres.
        /// Positive for the usual hull whose belly is under its pivot.
        ///
        /// <para>
        /// This is what turns "the ground is at 100" into "put the origin at 100.1". Without it the
        /// clearance in a spawn config means whatever the artist's pivot happened to mean, which is
        /// how a ship ends up buried on one prefab and hovering on the next.
        /// </para>
        ///
        /// <para>
        /// Measured on the prefab, or on an instance standing at its authored rotation: collider
        /// bounds are axis-aligned world boxes, so a hull measured while banked reports a deeper
        /// belly than it has. Every caller here measures the prefab before the hull is turned — and
        /// a caller that genuinely wants the belly of a TURNED hull says so, through
        /// <see cref="BellyDropAt"/>.
        /// </para>
        /// </summary>
        public static float BellyDrop(GameObject ship)
        {
            if (ship == null) return 0f;
            if (!TryMeasureCollision(ship, out Bounds bounds)) return 0f;

            return ship.transform.position.y - bounds.min.y;
        }

        /// <summary>
        /// The same measurement, but of the hull as it would hang at <paramref name="rotation"/>.
        ///
        /// <para>
        /// The arrival needs this because its descent ends nose-down: the difference between the
        /// belly of a pitched hull and the belly of a resting one is exactly how far the ship has
        /// to be held up for the part that reaches the ground to be its nose. Guessing that from
        /// the hull's length would be guessing at where its pivot is, which is the mistake
        /// <see cref="BellyDrop"/> exists to stop anyone making.
        /// </para>
        ///
        /// <para>
        /// It turns the ship to measure it and turns it straight back, inside one call with no
        /// frame boundary in it, so nothing that samples the transform — a NetworkTransform, a
        /// seated rider held in LateUpdate — can observe the pose it borrowed. The physics sync is
        /// not optional: collider bounds come from the physics scene, and without it the answer
        /// would be the rotation the hull had before.
        /// </para>
        /// </summary>
        public static float BellyDropAt(GameObject ship, Quaternion rotation)
        {
            if (ship == null) return 0f;

            Transform hull = ship.transform;
            Quaternion previous = hull.rotation;

            hull.rotation = rotation;
            Physics.SyncTransforms();

            float drop = BellyDrop(ship);

            hull.rotation = previous;
            Physics.SyncTransforms();

            return drop;
        }

        /// <summary>
        /// The height of the highest solid part of the hull, so a probe can be started above the
        /// ship rather than at an authored ceiling the ship may be well above.
        /// </summary>
        public static float TopOf(GameObject ship)
        {
            if (ship == null) return 0f;

            return TryMeasureCollision(ship, out Bounds bounds)
                ? bounds.max.y
                : ship.transform.position.y;
        }

        /// <summary>
        /// Everything the hull's solid parts occupy. Triggers are excluded for the reason the ground
        /// probe excludes them: an interaction volume, a boarding trigger or a shelter bounds is not
        /// part of the hull and would report a belly metres below the real one.
        ///
        /// <para>
        /// Colliders that are not in the physics scene are excluded too, and that one is not a
        /// nicety. Collider.bounds is a world-space box the physics scene maintains, and a collider
        /// it has never seen has no box to report: Unity hands back a zero-SIZE bounds sitting at
        /// the WORLD ORIGIN, and Encapsulate then stretches the hull all the way down to y=0.
        /// PlayerShip carries eleven of them — the salvage parts are authored disabled — so a hull
        /// standing at 106 m would report a belly hanging 106 m below its own origin, and every
        /// height derived from it is wrong by the hull's own altitude.
        /// </para>
        /// <para>
        /// Tested on the box rather than on the enabled flag, because both cases have to go and only
        /// one of them is a flag: a prefab ASSET is in no physics scene at all, and its colliders
        /// report the same empty box whatever their flags say. Skipping them there costs nothing —
        /// the measurement was already meaningless — and asking activeInHierarchy instead would
        /// discard every collider on a prefab, which is exactly the measurement the arc is planned
        /// from.
        /// </para>
        /// </summary>
        private static bool TryMeasureCollision(GameObject ship, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Collider collider in ship.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (collider.isTrigger) continue;
                if (collider.bounds.size == Vector3.zero) continue;

                if (!any)
                {
                    bounds = collider.bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            return any;
        }
    }
}
