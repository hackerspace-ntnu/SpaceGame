using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// One item, sitting in one socket of an open pack, waiting to be looked at and taken.
    ///
    /// This is added by <see cref="BackpackObject"/> onto the display copy that
    /// <see cref="BackpackItemVisual"/> built — the same GameObject that carries the BoxCollider the
    /// interaction ray hits. That pairing is what makes the pick work: Interactor calls GetComponent
    /// on the collider it hit before it walks up to a parent, so an item in a pocket always answers
    /// for itself rather than handing the interaction to the pack behind it.
    /// </summary>
    public class BackpackSlotView : MonoBehaviour, IInteractable
    {
        private BackpackObject pack;
        private BackpackCompartment compartment;
        private int index = -1;

        public void Bind(BackpackObject owner, BackpackCompartment slotCompartment, int slotIndex)
        {
            pack = owner;
            compartment = slotCompartment;
            index = slotIndex;
        }

        public bool CanInteract()
        {
            // Strap items are visible on a worn pack too, but reaching one means the pack is off and
            // open — otherwise the crosshair would offer to unclip a bedroll off the player's own back.
            return pack != null && pack.IsOpen && index >= 0;
        }

        /// <summary>
        /// Ask for this item. Nothing moves here.
        ///
        /// <para>
        /// This used to reach straight into <c>interactor.GetComponent&lt;IPlayerInventory&gt;()</c>
        /// and move the item, which was wrong twice over. It was wrong on this machine, because on
        /// this project's player the Interactor sits on the camera rig and a plain GetComponent
        /// there finds no inventory at all — the same trap PickupableItem.RequestPickup documents,
        /// and the reason taking anything out of a pack quietly did nothing. And it was wrong for
        /// the session, because two players can be looking into one open pack and only one machine
        /// may be allowed to decide which of them got the last water cell.
        /// </para>
        /// <para>
        /// So the request goes to the server, which performs BOTH halves of the transfer — and both
        /// halves replicate themselves from there: the hotbar through PlayerInventoryNetwork, the
        /// pack through BackpackNetwork. Nothing is done optimistically, on purpose: an optimistic
        /// take would have to be taken back from whichever player lost the race, and watching an
        /// item appear in your hand and vanish again is worse than the round trip.
        /// </para>
        /// </summary>
        public void Interact(Interactor interactor)
        {
            if (!CanInteract() || interactor == null) return;

            // No "hotbar is full" line any more. It cannot be said honestly from here — the machine
            // that knows is the one that runs the swap — and a full hotbar is no longer a refusal
            // anyway: BackpackObject.TryTakeToHotbar swaps the selected item into the pocket.
            pack.RequestTake(compartment, index, interactor);
        }
    }
}
