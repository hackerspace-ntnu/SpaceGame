using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Fit one hull module: the first turn of the salvage loop, done once with a module the server
    /// drops a walk away from the wreck. Met by any module fitted beyond what the hull was authored
    /// with, whichever module and whoever fits it.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Recover Module Step")]
    public class RecoverModuleStep : PlacedItemStep
    {
        [Tooltip("How far from the hull the module comes down, metres. Far enough to be a walk and " +
                 "to need the beacon, near enough to be seen from the ramp.")]
        [SerializeField] private Vector2 distance = new(90f, 130f);

        protected override Vector3 ChooseOffset()
        {
            float bearing = Random.Range(0f, 2f * Mathf.PI);
            float reach = Random.Range(distance.x, distance.y);
            return new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * reach;
        }

        public override bool IsMet(ObjectiveWorld world)
        {
            ShipPartRack ship = world.Ship;
            return ship != null && (ship.InstalledMask & ~ship.AuthoredMask) != 0;
        }

        /// <summary>
        /// The module while it lies loose; once somebody is carrying it, the hole it actually fits —
        /// an empty socket of its own kind — so the marker walks them home to the right place.
        /// </summary>
        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            TryFindLoose(world, out position) || world.TryGetEmptySocket(out position, KindOf(item));

        /// <summary>The socket kind the placed module fits, or null when the item is not a module.</summary>
        private static ShipPartKind? KindOf(InventoryItem module)
        {
            ShipPartItem part = module != null && module.itemPrefab != null
                ? module.itemPrefab.GetComponent<ShipPartItem>()
                : null;
            return part != null ? part.Kind : null;
        }
    }
}
