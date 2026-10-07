using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Every socket on the hull filled — the long goal the opening points at. No waypoint: where the
    /// other modules lie is for the crew to find out.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Repair Ship Step")]
    public class RepairShipStep : ObjectiveStep
    {
        [Tooltip("Label before the fitted/total count under the title.")]
        [SerializeField] private string countLabel = "Modules fitted";

        public override bool IsMet(ObjectiveWorld world)
        {
            ShipPartRack ship = world.Ship;
            return ship != null && ship.IsComplete;
        }

        public override string Status(ObjectiveWorld world)
        {
            ShipPartRack ship = world.Ship;
            if (ship == null) return string.Empty;

            int total = ship.Sockets.Count;
            return $"{countLabel}  <b>{ShipPartInfo.CountInstalled(ship.InstalledMask, total)}/{total}</b>";
        }
    }
}
