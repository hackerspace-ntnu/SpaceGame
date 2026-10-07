using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Somebody has sat down at the cockpit terminal, whose first page draws the hull with every
    /// missing module glowing red. The damage is explained by the ship's own readout rather than by
    /// a second screen that says the same thing.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Use Terminal Step")]
    public class UseTerminalStep : ObjectiveStep
    {
        public override bool IsMet(ObjectiveWorld world)
        {
            TerminalConsole console = ConsoleOf(world);
            return console != null && console.Occupied;
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            TerminalConsole console = ConsoleOf(world);
            position = console != null ? console.transform.position : default;
            return console != null;
        }

        /// <summary>The damage itself: an empty socket on the hull, not the console that describes it.</summary>
        public override bool TryGetFocus(ObjectiveWorld world, out Vector3 position) =>
            world.TryGetEmptySocket(out position);

        private static TerminalConsole ConsoleOf(ObjectiveWorld world)
        {
            ShipPartRack ship = world.Ship;
            return ship != null ? ship.GetComponentInChildren<TerminalConsole>(true) : null;
        }
    }
}
