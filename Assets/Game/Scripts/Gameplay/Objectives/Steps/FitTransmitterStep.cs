using System;
using System.Collections.Generic;
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
    /// a remembered stage, so it is right on every machine and after a reload.
    /// </para>
    /// <para>
    /// <b>One marker on the tower, and it is the transmitter.</b> The waypoint is the socket while the burnt
    /// unit is in it; then the transmitter itself, in its cradle on the dish's feed horn; then, once it is off
    /// the dish, the transmitter if it lies loose, else the empty socket it goes in. Nothing marks the hook
    /// board, the ladder or the control room: how to reach a transmitter 60 m up a turning dish is the
    /// tower's to tell (<c>GDC-L1-LEVEL-0001</c>, <c>GDC-L1-LEVEL-0004</c>), with two remarks to nudge — one
    /// when the crew first walk up to the tower, one when somebody takes the hook.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Fit Transmitter Step")]
    public class FitTransmitterStep : ObjectiveStep
    {
        /// <summary>Remark indices — the save format: append only.</summary>
        public const int TowerRemark = 0, HookRemark = 1;

        [Tooltip("The working transmitter, so it can be found and marked.")]
        [SerializeField] private InventoryItem transmitter;

        [Tooltip("How far from the hull a loose transmitter is looked for, metres. Past the tower.")]
        [SerializeField, Min(1f)] private float searchRadius = 700f;

        [Header("Status, by what the socket holds")]
        [SerializeField] private string jammedStatus = "Burnt out and jammed in its cradle";
        [SerializeField] private string burningStatus = "On fire. Take the extinguisher off its bracket and put it out";
        [SerializeField] private string pullStatus = "The fire is out. Pull the burnt unit";
        [SerializeField] private string fetchStatus = "Fetch the working transmitter from the radar dish tower";

        [Header("Remarks")]
        [Tooltip("Said once, when any crew member first comes this close to the tower, metres, flat.")]
        [SerializeField, Min(1f)] private float towerRemarkRadius = 60f;

        [SerializeField, TextArea(2, 4)] private string[] towerRemark =
        {
            "That dish is pointing straight at the sky. I wonder if we can turn it to a better position...",
        };

        [Tooltip("Said once, when the grappling hook has left its board.")]
        [SerializeField, TextArea(2, 4)] private string[] hookRemark =
        {
            "That's a grappling hook. Press I to fit the gauntlet on your arm, then Q or E to fire it, " +
            "depending on which arm it's on.",
        };

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

            return TryFindOnDish(out position) switch
            {
                DishState.OnDish => true,
                DishState.Gone => TryGetBeacon(world, out position) ||
                                  world.TryGetEmptySocket(out position, ShipPartKind.Transmitter),
                _ => TryGetBeacon(world, out position),
            };
        }

        /// <summary>
        /// The working transmitter while it lies in the open — dropped on the way home, say. Never while the
        /// burnt unit is in or the transmitter is still on the dish: there the waypoint is the one marker.
        /// </summary>
        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            ShipPartRack ship = world.Ship;
            ShipPartSocket socket = world.SocketOf(ShipPartKind.Transmitter);
            if (ship == null || socket == null || ship.IsBroken(ship.IndexOf(socket))) return false;
            if (TryFindOnDish(out _) == DishState.OnDish) return false;

            return world.TryFindLoose(transmitter, ship.transform.position, searchRadius, out position);
        }

        /// <summary>The briefing looks at the burnt unit, which is what the crew can do something about first.</summary>
        public override bool TryGetFocus(ObjectiveWorld world, out Vector3 position)
        {
            ShipPartSocket socket = world.SocketOf(ShipPartKind.Transmitter);
            position = socket != null ? socket.Centre : default;
            return socket != null;
        }

        // ── Remarks ──────────────────────────────────────────────────────────

        public override int RemarkCount => 2;

        public override IReadOnlyList<string> RemarkLines(int index) => index switch
        {
            TowerRemark => towerRemark,
            HookRemark => hookRemark,
            _ => Array.Empty<string>(),
        };

        public override bool IsRemarkDue(ObjectiveWorld world, int index)
        {
            DishConsole console = Console();
            if (console == null) return false;

            return index switch
            {
                TowerRemark => world.NearestCrewDistance(console.Rig != null ? console.Rig.transform.position
                                                                             : console.transform.position) <= towerRemarkRadius,
                HookRemark => console.DrivesUnlocked,
                _ => false,
            };
        }

        // ── The tower ────────────────────────────────────────────────────────

        private enum DishState { Unknown, OnDish, Gone }

        /// <summary>
        /// Where the transmitter is on the dish. <see cref="DishState.Unknown"/> while the tower's chunk is not
        /// loaded (no marker, the tower itself is the landmark), <see cref="DishState.Gone"/> once its cradle
        /// is empty.
        /// </summary>
        private DishState TryFindOnDish(out Vector3 position)
        {
            position = default;
            DishConsole console = Console();
            WallInventory cradle = console != null ? console.TransmitterCradle : null;
            if (cradle == null) return DishState.Unknown;

            foreach (PackPlacement placement in cradle.Layout.Placements)
            {
                if (cradle.ItemFor(placement.ItemId) != transmitter) continue;

                position = cradle.TryGetComponent(out BoxCollider device) ? device.bounds.center : cradle.transform.position;
                return DishState.OnDish;
            }

            return DishState.Gone;
        }

        /// <summary>Public for the tests: the device's position while it is still on the dish.</summary>
        public bool TryGetTransmitterOnDish(out Vector3 position) => TryFindOnDish(out position) == DishState.OnDish;

        private static DishConsole Console() => DishConsole.Live.Count > 0 ? DishConsole.Live[0] : null;
    }
}
