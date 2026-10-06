using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Get the long-range transmitter working: put out the burnt-out unit's fire, pull the unit, fetch the
    /// working one off the radar dish tower and fit it. Met the moment the rack has a working transmitter in
    /// its socket, by whatever route and whoever fits it — so a crew that did it early walk straight through.
    ///
    /// <para>
    /// The status follows the job as it stands on the hull (the rack's broken mask, the fire's phase), never
    /// a remembered stage, so it is right on every machine and after a reload. The waypoint is the socket
    /// while the burnt unit is in it, and the working transmitter while it lies loose. While it is on the
    /// tower there is none: the tower is the tallest thing for kilometres and in sight of the wreck, which is
    /// the guidance (<c>GDC-L1-LEVEL-0001</c>).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Fit Transmitter Step")]
    public class FitTransmitterStep : ObjectiveStep
    {
        [Tooltip("The working transmitter, so a loose one can be found and marked.")]
        [SerializeField] private InventoryItem transmitter;

        [Tooltip("How far from the hull a loose transmitter is looked for, metres. Past the tower.")]
        [SerializeField, Min(1f)] private float searchRadius = 700f;

        [Header("Status, by what the socket holds")]
        [SerializeField] private string jammedStatus = "Burnt out and jammed in its cradle";
        [SerializeField] private string burningStatus = "On fire. Take the extinguisher off its bracket and put it out";
        [SerializeField] private string pullStatus = "The fire is out. Pull the burnt unit";
        [SerializeField] private string fetchStatus = "Fetch the working transmitter from the radar dish tower";

        public override bool IsMet(ObjectiveWorld world) => ShipSignal.IsTransmitterFitted(world.Ship);

        public override string Status(ObjectiveWorld world)
        {
            ShipPartRack ship = world.Ship;
            ShipPartSocket socket = world.SocketOf(ShipPartKind.Transmitter);
            if (ship == null || socket == null) return string.Empty;

            if (!ship.IsBroken(ship.IndexOf(socket))) return fetchStatus;

            ShipPartFire fire = ship.GetComponent<ShipPartFire>();
            if (fire == null) return pullStatus;

            return fire.State.phase switch
            {
                ShipPartFirePhase.Burning => burningStatus,
                ShipPartFirePhase.Out => pullStatus,
                _ => jammedStatus,
            };
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            ShipPartRack ship = world.Ship;
            ShipPartSocket socket = world.SocketOf(ShipPartKind.Transmitter);
            if (ship == null || socket == null) return false;

            if (ship.IsBroken(ship.IndexOf(socket)))
            {
                position = socket.Centre;
                return true;
            }

            return TryGetBeacon(world, out position);
        }

        /// <summary>The working transmitter while it lies in the open — dropped on the way home, say.</summary>
        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            ShipPartRack ship = world.Ship;
            return ship != null && world.TryFindLoose(transmitter, ship.transform.position, searchRadius, out position);
        }

        /// <summary>The briefing looks at the burnt unit, which is what the crew can do something about first.</summary>
        public override bool TryGetFocus(ObjectiveWorld world, out Vector3 position)
        {
            ShipPartSocket socket = world.SocketOf(ShipPartKind.Transmitter);
            position = socket != null ? socket.Centre : default;
            return socket != null;
        }
    }
}
