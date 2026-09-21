// Read-only questions about a hotbar: does it hold this, where, and how much room is left.
//
// These three lived as private statics on TraderInteraction until a second caller appeared. They
// are questions about an inventory, not about trading — leaving them there would have made the
// quest system depend on SpaceGame.Gameplay.Trading for nothing.
//
// Everything here counts SLOTS, not stacks. Inventory has no stacking and no quantity field, so
// three of an item is three occupied slots and there is no other number to read.
using UnityEngine;

namespace SpaceGame.Items
{
    public static class InventoryQuery
    {
        /// <summary>
        /// How many slots hold <paramref name="item"/>.
        ///
        /// Compared by reference: <see cref="InventoryItem"/> assets are singletons loaded out of
        /// Resources, so two references to the same item are the same object. Comparing by
        /// <see cref="InventoryItem.ID"/> instead would also match, but silently accepts a
        /// runtime-constructed duplicate that nothing else in the project treats as the same item.
        /// </summary>
        public static int CountHeld(IPlayerInventory inventory, InventoryItem item)
        {
            if (inventory == null || item == null) return 0;

            int count = 0;
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty && slot.Item == item) count++;
            }

            return count;
        }

        /// <summary>
        /// The first slot holding <paramref name="item"/>, or -1.
        ///
        /// The index is what callers actually need: <see cref="IPlayerInventory.TryRemoveItem"/>
        /// takes a slot, never an item.
        /// </summary>
        public static int FindHeld(IPlayerInventory inventory, InventoryItem item)
        {
            if (inventory == null || item == null) return -1;

            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty && slot.Item == item) return i;
            }

            return -1;
        }

        /// <summary>How many slots are empty.</summary>
        public static int CountFree(IPlayerInventory inventory)
        {
            if (inventory == null) return 0;

            int count = 0;
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot == null || slot.IsEmpty) count++;
            }

            return count;
        }
    }
}
