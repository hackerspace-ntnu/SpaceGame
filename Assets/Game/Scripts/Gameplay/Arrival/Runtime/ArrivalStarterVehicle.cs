using System;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// The vehicle the crew find parked beside their wreck: one per landed hull, spawned the moment
    /// that hull is down.
    ///
    /// <para>
    /// A plain serializable class held by <see cref="ArrivalDirector"/>, not a component of its own,
    /// for the same reason <see cref="ArrivalFlight"/> is a plain class: it is server-only work that
    /// hangs off the arrival, and it owns no state beyond its tunables. The director decides WHEN
    /// (once per flight, after the settle has parked the hull); this decides WHERE and does the spawn.
    /// </para>
    ///
    /// <para>
    /// Nothing here remembers that the vehicle was delivered, and nothing needs to. A flight only
    /// exists in a world that has not been arrived in, <c>ArrivalSaveable</c> records "arrived" for
    /// good, and a loaded world never flies one — so a reload, or a second player's arrival in the
    /// same world, never reaches this. The vehicle itself persists through its own
    /// <c>SaveableEntity</c> as any runtime-spawned vehicle does, and comes back where it was left.
    /// </para>
    /// </summary>
    [Serializable]
    public class ArrivalStarterVehicle
    {
        [Tooltip("What is parked beside each landed hull. Must be registered in the network prefab " +
                 "list and carry a SaveableEntity with a stamped prefabId, or it exists for the host " +
                 "alone and is gone on reload. Empty parks nothing.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("Where it is parked, in metres in the hull's own axes: x toward the hull's right " +
                 "(starboard), y toward its nose. Must stay clear of the hull, its side stair and " +
                 "its aft ramp — StarterVehicleTests measures the default against the built ship.")]
        [SerializeField] private Vector2 hullOffset = new(-22f, 0f);

        [Tooltip("Degrees the vehicle is turned from the hull's own heading. Zero parks it pointing " +
                 "the way the ship points.")]
        [SerializeField] private float yawFromHull;

        [Tooltip("How far above the landed hull's origin the ground probe starts. Above anything " +
                 "the vehicle could reasonably be parked on, below any arch or overhang.")]
        [SerializeField] private float probeAbove = 40f;

        [Tooltip("How far down from its start the ground probe looks.")]
        [SerializeField] private float probeReach = 120f;

        [Tooltip("Metres above the measured ground the vehicle's origin is placed. It is a dynamic " +
                 "body, so it settles the rest of the way itself; spawning it exactly on the " +
                 "surface risks starting its wheel inside the terrain collider.")]
        [SerializeField] private float dropHeight = 0.5f;

        /// <summary>What is parked. Configured in the Inspector; the setter is for tests, which have none.</summary>
        public GameObject Prefab
        {
            get => prefab;
            set => prefab = value;
        }

        /// <summary>Where it is parked, in the hull's axes. Read by the test that measures it against the ship.</summary>
        public Vector2 HullOffset => hullOffset;

        /// <summary>
        /// Parks one vehicle beside <paramref name="hull"/>. Server-only; returns the spawned object,
        /// or null when there is nothing configured to park.
        /// </summary>
        public GameObject Deliver(GameObject hull)
        {
            if (prefab == null || hull == null) return null;

            Transform frame = hull.transform;
            float hullYaw = frame.eulerAngles.y;

            Vector2 groundXZ = StarterVehiclePlacement.GroundPoint(frame.position, hullYaw, hullOffset);
            float groundY = GroundHeight(groundXZ, hull);

            var position = new Vector3(groundXZ.x, groundY + dropHeight, groundXZ.y);
            GameObject vehicle = GameServices.World.Spawn(prefab, position,
                                                          StarterVehiclePlacement.Facing(hullYaw, yawFromHull));

            if (vehicle == null)
                Debug.LogError($"[Arrival] Spawning the starter vehicle '{prefab.name}' returned nothing. " +
                               "Is it registered in the network prefab list?", hull);

            return vehicle;
        }

        /// <summary>
        /// The world's static surface under the parking spot. Measured against collision, the way a
        /// landed hull is verified, and with the hull and every Rigidbody excluded — so a crewmate,
        /// a mount or an NPC standing on the spot is not taken for the ground
        /// (<c>ShipGrounding.TryResolveCollisionGround</c>).
        /// </summary>
        private float GroundHeight(Vector2 groundXZ, GameObject hull)
        {
            float hullBase = hull.transform.position.y;

            if (ShipGrounding.TryResolveCollisionGround(groundXZ, hullBase + probeAbove, probeReach,
                                                       hull, out float groundY))
                return groundY;

            // The landing site is held loaded for the whole flight, so a miss here is not a chunk
            // still streaming — it is a hole in the world's collision beside the wreck. The hull's
            // own base was measured against real ground seconds ago and is a metre or two out at
            // worst; the vehicle is a dynamic body and settles onto whatever is really there.
            Debug.LogWarning($"[Arrival] No ground under the starter vehicle's spot ({groundXZ}); " +
                             "parking it at the hull's own base height.", hull);
            return hullBase;
        }
    }
}
