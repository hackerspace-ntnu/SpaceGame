using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The crash vented the crew's bottles: take an empty one off the gear wall, fill it at the oxygen plant, open the pack
    /// and seat it in the socket. Met when ANY crew member has a filled bottle seated (<see cref="OxygenPlantMount.AirRefilled"/>),
    /// so a late joiner, an AFK player or a dead one never holds the crew up — each player's own status still walks them
    /// through their own bottle.
    ///
    /// <para>
    /// Stateless, like every step: whether somebody has air again is the mount's (replicated, saved), and the sub-step shown
    /// is read off this machine's own player — their hotbar, their pack and its socket — which already replicate.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Refill Air Step")]
    public class RefillAirStep : ObjectiveStep
    {
        [Tooltip("The oxygen bottle: the one item the plant fills and the pack's socket takes.")]
        [SerializeField] private InventoryItem bottle;

        [Tooltip("How full a bottle must be to count as filled, 0-1. The plant fills one in five seconds.")]
        [SerializeField, Range(0.05f, 1f)] private float filledFraction = 0.9f;

        [SerializeField] private string takeStatus = "Take an empty bottle from the gear wall  <b>(RMB)</b>";
        [SerializeField] private string fillStatus = "Fill it at the oxygen plant's collar  <b>(RMB)</b>";
        [SerializeField] private string seatStatus = "Press <b>B</b> and put the full bottle in your pack's socket";
        [SerializeField] private string doneStatus = "Breathing from a full bottle";

        [Tooltip("Said once, as the crew's reserve runs low before anybody has a bottle seated.")]
        [SerializeField, TextArea(2, 4)] private string[] reserveRemark =
        {
            "Your suit reserve is running low. Fill a bottle and get it in your pack, now.",
        };

        [Tooltip("The fraction of the suit's reserve at which the low-reserve remark is said.")]
        [SerializeField, Range(0f, 1f)] private float reserveRemarkAt = 0.35f;

        [Tooltip("How high above a target the beacon stands, in metres.")]
        [SerializeField, Min(0f)] private float beaconLift = 1.5f;

        private const int ReserveRemark = 0;

        private enum Stage { Take, Fill, Seat, Done }

        public override bool IsMet(ObjectiveWorld world)
        {
            OxygenPlantMount mount = MountOf(world);
            return mount == null || mount.AirRefilled;
        }

        public override string Status(ObjectiveWorld world) => StageOf(LocalBody()) switch
        {
            Stage.Take => takeStatus,
            Stage.Fill => fillStatus,
            Stage.Seat => seatStatus,
            _ => doneStatus,
        };

        /// <summary>The gear wall while you have no bottle in hand, the plant while yours is empty.</summary>
        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            OxygenPlantMount mount = MountOf(world);
            if (mount == null) return false;

            switch (StageOf(LocalBody()))
            {
                case Stage.Take:
                    if (CrewGear.TryFindOnWall(world.Ship, bottle, out position)) return true;
                    position = mount.Point;
                    return true;
                case Stage.Fill:
                    position = mount.Point;
                    return true;
                default:
                    return false;
            }
        }

        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            bool found = TryGetWaypoint(world, out position);
            position += Vector3.up * beaconLift;
            return found;
        }

        // ── Remarks ──────────────────────────────────────────────────────────

        public override int RemarkCount => 1;

        public override IReadOnlyList<string> RemarkLines(int index) =>
            index == ReserveRemark ? reserveRemark : Array.Empty<string>();

        public override bool IsRemarkDue(ObjectiveWorld world, int index)
        {
            if (index != ReserveRemark) return false;

            foreach (SuitOxygen suit in SuitOxygen.Every)
                if (suit != null && suit.OnReserve && suit.SuitFraction <= reserveRemarkAt) return true;

            return false;
        }

        // ── This machine's player ────────────────────────────────────────────

        /// <summary>Where <paramref name="body"/> is in the refill: no bottle in hand, an empty one, a full one, or one seated.</summary>
        private Stage StageOf(GameObject body)
        {
            PackContainer pack = CrewGear.PackOf(body);
            if (pack != null && pack.TryFindSocketed(SupplyKind.Oxygen, out PackPlacement seated) && seated.Charge >= filledFraction)
                return Stage.Done;

            float best = BestCarried(body, pack);
            return best < 0f ? Stage.Take : best >= filledFraction ? Stage.Seat : Stage.Fill;
        }

        /// <summary>The fullest bottle <paramref name="body"/> carries outside the socket: hotbar or pack. Negative for none.</summary>
        private float BestCarried(GameObject body, PackContainer pack)
        {
            float best = -1f;
            if (body == null || bottle == null) return best;

            if (body.TryGetComponent(out PlayerController controller) && controller.PlayerInventory != null)
            {
                IPlayerInventory hotbar = controller.PlayerInventory;
                for (int i = 0; i < hotbar.GetInventorySize(); i++)
                {
                    InventorySlot slot = hotbar.GetSlot(i);
                    if (slot == null || slot.Item != bottle) continue;

                    float charge = SupplyCharge.Read(slot.State);
                    best = Mathf.Max(best, charge < 0f ? SupplyCharge.StartingChargeOf(bottle) : charge);
                }
            }

            if (pack == null) return best;

            pack.TryFindSocketed(SupplyKind.Oxygen, out PackPlacement seated);
            foreach (PackPlacement placement in pack.Layout.Placements)
            {
                if (placement.ItemId == seated.ItemId || pack.ItemFor(placement.ItemId) != bottle) continue;
                best = Mathf.Max(best, placement.Charge);
            }

            return best;
        }

        private static GameObject LocalBody()
        {
            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            return player != null ? player.gameObject : null;
        }

        private static OxygenPlantMount MountOf(ObjectiveWorld world) =>
            world.Ship != null ? world.Ship.GetComponentInChildren<OxygenPlantMount>(true) : null;
    }
}
