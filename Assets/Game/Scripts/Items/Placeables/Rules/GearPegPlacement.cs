// Put a gear on a peg of a GearDoor. A placeable whose "ground" is a gate covered in pegs.
//
// Criteria: a peg of some gate stands near where the player aims, it is free, and a gear of this size there would
//           not hit a gear already standing.
// Logic:    the gate seats it, which is NOT a spawn: a gear on a peg is one number in the gate's slots, drawn on every
//           machine from the gate's own announcement, so nothing is made here for World.Spawn to do.
// Preview:  a ghost of this gear on that peg, green where it would go, red where it is refused.
//
// The aim's target is not used: on the server it resolves to the building's NetworkObject, not to the peg, so the
// peg is found again from the aim point, the one thing the request carries that is the same on both machines.
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Items
{
    public class GearPegPlacement : PlacementRule
    {
        [Tooltip("The item this rule seats: the gear the player is holding. The gate knows which of its kinds it is.")]
        [SerializeField] private InventoryItem gear;

        // The gate a ghost is up on, so it comes down when the aim leaves it.
        private GearDoor previewed;

        public override bool CanPlace(in PlacementAim aim) =>
            GearDoor.TryFindPeg(aim.Point, gear, out _, out _, out _);

        public override bool Place(in PlacementAim aim, GameObject placer)
        {
            // ServerSeat answers whether it actually went on, and PlaceableItem spends the item on that answer.
            return GearDoor.TryFindPeg(aim.Point, gear, out GearDoor door, out int peg, out _) &&
                   door.ServerSeat(peg, door.KindOf(gear));
        }

        public override string RefusalHint(in PlacementAim aim)
        {
            GearDoor.TryFindPeg(aim.Point, gear, out _, out _, out string refusal);
            return refusal;
        }

        public override void Preview(in PlacementAim aim, bool placeable)
        {
            // TryFindPeg names the nearest peg's gate even when it refuses it, which is when the ghost goes red.
            GearDoor.TryFindPeg(aim.Point, gear, out GearDoor door, out int peg, out _);
            if (door != previewed) EndPreview();

            previewed = door;
            if (door != null) door.ShowGhost(peg, door.KindOf(gear), placeable);
        }

        public override void EndPreview()
        {
            if (previewed != null) previewed.HideGhost();
            previewed = null;
        }
    }
}
