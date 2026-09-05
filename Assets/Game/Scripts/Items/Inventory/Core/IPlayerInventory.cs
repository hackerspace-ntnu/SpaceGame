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
        event Action<InventoryItem> OnItemDropped;

        bool TryAddItem(InventoryItem item);
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
        int GetInventorySize();
        InventorySlot GetSlot(int index);
        InventorySlot GetSelectedSlot();
        InventoryItem GetSelectedItem();
    }
}
