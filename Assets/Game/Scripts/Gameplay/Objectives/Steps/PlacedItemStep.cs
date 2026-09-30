using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A step about one item the server puts on the ground near the ship when the step begins: a
    /// module from the wreck, an artifact thrown clear in the crash.
    ///
    /// <para>
    /// The item is an ordinary pickup spawned through the world service, so it replicates to every
    /// peer and is saved as a runtime entity like anything a player drops — nothing here remembers
    /// it. Where it is NOW is read back from the scanner registry every time it is asked, which is
    /// what keeps the waypoint right after it has been picked up, carried, dropped and reloaded.
    /// </para>
    /// </summary>
    public abstract class PlacedItemStep : ObjectiveStep
    {
        [Tooltip("What the server puts on the ground when the step begins.")]
        [SerializeField] protected InventoryItem item;

        [Tooltip("How many spots are tried in one frame before waiting for more ground to stream in.")]
        [SerializeField, Min(1)] private int placementTries = 8;

        [Tooltip("How far above the hull the ground probe starts, metres. Above anything the wreck " +
                 "could have come down beside.")]
        [SerializeField, Min(1f)] private float probeHeight = 400f;

        [Tooltip("Height above the ground the item is dropped from, so it settles rather than " +
                 "starting inside the sand.")]
        [SerializeField, Min(0f)] private float dropHeight = 0.5f;

        [Tooltip("How far from the hull a loose copy is looked for. Wider than the item can be " +
                 "placed, so it is still found after somebody carries it off and drops it.")]
        [SerializeField, Min(1f)] private float searchRadius = 600f;

        public InventoryItem Item => item;

        /// <summary>A candidate drop point in the hull's own frame. Called once per try.</summary>
        protected abstract Vector3 ChooseOffset();

        public override bool TryBegin(ObjectiveWorld world)
        {
            if (item == null)
            {
                Debug.LogError($"[Objectives] '{name}' has no item to place; the step goes on without one.", this);
                return true;
            }

            for (int i = 0; i < placementTries; i++)
            {
                if (!world.TryGroundNearShip(ChooseOffset(), probeHeight, out Vector3 ground)) continue;

                world.Spawn(item, ground + Vector3.up * dropHeight, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                return true;
            }

            return false;
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            TryFindLoose(world, out position);

        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position) =>
            TryFindLoose(world, out position);

        protected bool TryFindLoose(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            ShipPartRack ship = world.Ship;
            return ship != null && world.TryFindLoose(item, ship.transform.position, searchRadius, out position);
        }
    }
}
