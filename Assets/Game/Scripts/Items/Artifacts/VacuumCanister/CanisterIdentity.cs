// Making the hotbar slot name whichever canister the holder is actually carrying.
//
// An empty canister and a full one are two InventoryItem assets over two prefabs, the way an empty
// and a charged oxygen bottle are. This is the one rule that keeps the slot honest about which of
// them is in the hand.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The empty-becomes-full swap, as one static rule.
    ///
    /// <para>
    /// <b>Why the slot's ITEM and not a flag.</b> <c>ItemState</c> is the server's own bag and does
    /// not replicate; the hotbar's item id does, server-owned, through
    /// <c>PlayerInventoryNetwork</c>. So changing which item the slot holds is the only way a
    /// container's contents can be published to every machine without a message of its own — and it
    /// is also what makes a full canister still look full after being dropped, picked up by
    /// somebody else, saved and reloaded, because the identity is what a world instance and a save
    /// record are built from.
    /// </para>
    /// <para>
    /// <b>The captive is not lost in the swap, and the mechanism is worth stating.</b>
    /// <c>InventorySlot.Item</c>'s setter clears the slot's bag whenever the item changes, so the
    /// record is gone the instant the new identity lands. It comes straight back because changing
    /// the slot re-equips the hand, and <c>EquipmentController.Unequip</c> captures the OLD held
    /// instance's state into the slot before destroying it — and that instance still holds the
    /// record. The new instance is then handed the bag by <c>Equip</c>. Both halves are
    /// <c>EquipmentController</c>'s documented order; nothing here re-writes the bag, because a
    /// second copy written afterwards would be a second answer that could disagree.
    /// </para>
    /// <para>
    /// Static and stateless: there is nothing to remember between calls, and the caller is a held
    /// item instance that is destroyed by the very swap it asks for.
    /// </para>
    /// </summary>
    public static class CanisterIdentity
    {
        /// <summary>
        /// Make <paramref name="holder"/>'s selected hotbar slot name <paramref name="wanted"/>,
        /// but only while it names <paramref name="other"/>.
        ///
        /// <para>
        /// <b>Authority only.</b> The hotbar is server state and <c>TrySetSlot</c> refuses
        /// elsewhere, with a warning; the caller has already asked whether it decides.
        /// </para>
        /// <para>
        /// The gate on <paramref name="other"/> is what makes this safe to call every frame: a slot
        /// that already names the wanted item is left alone, and a slot holding something else
        /// entirely — the player scrolled, the item was taken — is not overwritten with a canister
        /// nobody put there.
        /// </para>
        /// <para>
        /// <b>The charge is republished afterwards.</b> <c>TrySetSlot</c> stamps the wire with the
        /// new item's AUTHORED starting charge, because it has no way to know the instance was part
        /// full; the true fill arrives in the slot's bag a moment later, through the re-equip's
        /// state write-back. Pushing it here is what stops the swap reading as a free refill on
        /// every machine but the server's (<c>GDC-L1-SYS-0007</c> — a tank that refills itself by
        /// being used is a strategy players will find).
        /// </para>
        /// </summary>
        /// <returns>True when the slot was changed, in which case the caller has been re-equipped.</returns>
        public static bool Follow(GameObject holder, InventoryItem wanted, InventoryItem other)
        {
            if (holder == null || wanted == null || other == null || wanted == other) return false;

            IPlayerInventory inventory = holder.GetComponent<IPlayerInventory>();
            if (inventory == null) return false;

            int index = inventory.SelectedSlotIndex;

            InventorySlot slot = inventory.GetSlot(index);
            if (slot == null || slot.Item != other) return false;

            if (!inventory.TrySetSlot(index, wanted)) return false;

            inventory.PublishSlotCharges();
            return true;
        }
    }
}
