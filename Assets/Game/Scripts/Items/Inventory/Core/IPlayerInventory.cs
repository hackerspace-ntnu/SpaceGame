using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    public interface IPlayerInventory
    {
        int SelectedSlotIndex { get; }
        event Action<InventorySlot> OnSlotSelected;
        event Action<int, InventorySlot> OnSlotChanged;
        /// <summary>
        /// Something left the bar for the ground. The float is the item's charge
        /// (<see cref="SupplyCharge"/>), or <see cref="SupplyCharge.None"/> for the great majority
        /// of items, which hold nothing.
        ///
        /// <para>
        /// The charge rides the event rather than being looked up afterwards because by then it is
        /// gone: the slot has been cleared, and clearing a slot takes its <see cref="ItemState"/>
        /// with it. Without it a drained tank dropped on the sand comes back full.
        /// </para>
        /// </summary>
        event Action<InventoryItem, float> OnItemDropped;

        bool TryAddItem(InventoryItem item);

        /// <summary>
        /// Add, and say which slot it landed in. <paramref name="index"/> is -1 when nothing was
        /// added.
        ///
        /// <para>
        /// It exists because an item can carry per-instance state that has to follow it into the
        /// slot — a tank's charge (<see cref="SupplyCharge"/>) is the first — and
        /// <see cref="InventorySlot.State"/> can only be written by index. Searching for the item
        /// afterwards is not a substitute: a hotbar can legitimately hold two of the same asset,
        /// and the search would write one tank's charge onto the other.
        /// </para>
        /// <para>
        /// The default implementation is for the hand-written hotbars in tests. It reads the first
        /// free slot before the add and trusts the add to fill it, which is true of every first-fit
        /// inventory in this project; the two real implementations override it and report the index
        /// they actually used.
        /// </para>
        /// </summary>
        bool TryAddItem(InventoryItem item, out int index)
        {
            index = -1;

            for (int i = 0; i < GetInventorySize(); i++)
            {
                InventorySlot slot = GetSlot(i);
                if (slot == null || slot.IsEmpty)
                {
                    index = i;
                    break;
                }
            }

            if (TryAddItem(item)) return true;

            index = -1;
            return false;
        }

        bool TryRemoveItem(int index);
        void SelectSlot(int slotIndex);

        /// <summary>
        /// Replaces the whole hotbar in one go — used when loading a save.
        ///
        /// Neither existing entry point can do this. TryAddItem fills whichever slot happens to be
        /// free first, so a saved layout with a hole in slot 2 comes back compacted into slots 0-1.
        /// SelectSlot toggles, so re-selecting the saved slot on a fresh inventory (whose selection
        /// starts at -1) works, but re-selecting it on one that already has it selected clears it.
        /// Restoring is a single assignment of a known state, not a sequence of player actions.
        ///
        /// <paramref name="items"/> is positional: entry i goes to slot i, and a null entry empties
        /// that slot. Entries past the inventory's size are dropped.
        /// </summary>
        void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot);

        /// <summary>
        /// Server only: put <paramref name="item"/> (or null) into one named slot, replacing whatever
        /// is there. The seam a move between the hotbar and the body slots writes the hotbar half
        /// through — TryAddItem picks its own slot and TryRemoveItem cannot fill one, and a swap
        /// needs both halves to land where the player pointed. Refused, with a warning, off the
        /// server; on a networked client the answer arrives as a slot-change event.
        /// </summary>
        bool TrySetSlot(int index, InventoryItem item);

        /// <summary>
        /// Push every slot's per-instance charge (<see cref="SupplyCharge"/>) out to whoever else
        /// needs to see it. Server side; call it after writing an <see cref="InventorySlot.State"/>
        /// directly, which a restore and a transfer off the pack both do.
        ///
        /// <para>
        /// Nothing by default: an offline hotbar's slots ARE the truth, and there is nobody to tell.
        /// Only <c>PlayerInventoryNetwork</c> overrides it, because only a replicated hotbar has a
        /// second copy that can silently disagree — and a client whose own tank reads full while
        /// the server drains it is exactly that disagreement.
        /// </para>
        /// </summary>
        void PublishSlotCharges() { }
        int GetInventorySize();
        InventorySlot GetSlot(int index);
        InventorySlot GetSelectedSlot();
        InventoryItem GetSelectedItem();
    }
}
