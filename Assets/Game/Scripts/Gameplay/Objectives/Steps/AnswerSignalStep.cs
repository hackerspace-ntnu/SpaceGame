using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Answer the signal the working transmitter picked up: walk to the settlement it comes from
    /// (<see cref="ShipSignal"/>). Met the moment any member of the crew is inside the settlement's reach.
    ///
    /// <para>
    /// The destination is the ship's, not this step's: it is chosen by the server when the transmitter first
    /// works, saved with the hull and replicated, so this step only reads it. It begins once the signal has
    /// been heard — the frame after the transmitter goes in — and a signal that leads nowhere (no fixed,
    /// friendly settlement in the world) is met at once rather than left standing.
    /// </para>
    /// <para>
    /// Explicit guidance, waypoint and beacon both: the destination is hundreds of metres to kilometres
    /// across open desert, which is the open-world exception <c>GDC-L1-LEVEL-0001</c> carves out. The map
    /// table charts it before anyone sets out (<c>GDC-L1-LEVEL-0006</c>).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Answer Signal Step")]
    public class AnswerSignalStep : ObjectiveStep
    {
        [Tooltip("How far outside the settlement's own reach still counts as arrived, metres. Its reach is " +
                 "its buildings' extent, so this is the walk-up to the first wall.")]
        [SerializeField, Min(0f)] private float arrivalMargin = 15f;

        [Tooltip("The line under the title; {0} is who is calling, as the faction names itself.")]
        [SerializeField] private string sourceStatus = "Calling on the open band: <b>{0}</b>";

        [Tooltip("Under the title when the call names nobody.")]
        [SerializeField] private string unnamedStatus = "Calling on the open band";

        public override bool TryBegin(ObjectiveWorld world)
        {
            ShipPartRack ship = world.Ship;
            if (ship == null) return false;

            ShipSignal signal = ship.GetComponent<ShipSignal>();
            if (signal == null)
            {
                Debug.LogError($"[Objectives] '{name}': the crew's hull has no ShipSignal, so there is no signal " +
                               "to answer. The step is skipped.", this);
                return true;
            }

            return signal.Destination.Received;
        }

        public override bool IsMet(ObjectiveWorld world)
        {
            if (!TryGetDestination(world, out SignalDestination destination)) return true;
            return HasArrived(destination, world.NearestCrewDistance(destination.Position), arrivalMargin);
        }

        /// <summary>
        /// Somebody stands within the settlement's reach plus <paramref name="margin"/>, measured from its
        /// centre over the ground. Never true for a signal that leads nowhere: that is met another way.
        /// </summary>
        public static bool HasArrived(in SignalDestination destination, float nearestCrewDistance, float margin) =>
            destination.HasDestination && nearestCrewDistance <= destination.Radius + margin;

        public override string Status(ObjectiveWorld world)
        {
            if (!TryGetDestination(world, out SignalDestination destination)) return string.Empty;
            return string.IsNullOrEmpty(destination.Origin) ? unnamedStatus : string.Format(sourceStatus, destination.Origin);
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            bool known = TryGetDestination(world, out SignalDestination destination);
            position = destination.Position;
            return known;
        }

        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position) =>
            TryGetWaypoint(world, out position);

        /// <summary>Where the signal leads, when it leads anywhere.</summary>
        private static bool TryGetDestination(ObjectiveWorld world, out SignalDestination destination)
        {
            ShipPartRack ship = world.Ship;
            ShipSignal signal = ship != null ? ship.GetComponent<ShipSignal>() : null;
            destination = signal != null ? signal.Destination : default;
            return destination.HasDestination;
        }
    }
}
