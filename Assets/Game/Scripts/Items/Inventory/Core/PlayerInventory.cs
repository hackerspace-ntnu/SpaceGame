using System;
using System.Collections.Generic;

namespace SpaceGame.Items
{
    /// <summary>
    /// Instance of InventoryComponent that represents the player's inventory.
    /// Handles hotkey input for selecting items and dropping them,
    /// and interacts with the EquipmentController to equip/unequip items based on selection.
    /// Also spawns dropped items in the world with physics applied.
    /// </summary>
    ///

    public class PlayerInventory
    {
        private readonly Inventory inventory;
        public int SelectedSlotIndex { get; private set; } = -1;

        public event Action<InventorySlot> OnSlotSelected;
    
        public event Action<int, InventorySlot> OnSlotChanged
        {
            add => inventory.OnSlotChanged += value; 
            remove => inventory.OnSlotChanged -= value;
        }
    
        public event Action<InventoryItem> OnItemDropped;
    
        public PlayerInventory(int size, List<InventoryItem> startingItems = null)
        {
            inventory = new Inventory(size);
            if (startingItems != null)
            {
                foreach (var item in startingItems)
                    inventory.TryAddItem(item);
            }
        }
    
        public List<string> GetItemIDs()
        {
            return inventory.GetItemIDs();
        }
    
        public void SetItem(int index, InventoryItem item)
        {
           inventory.SetItem(index, item);
        }

        /// <summary>
        /// Assigns the whole hotbar and the selection at once, as a load does. Bypasses SelectSlot
        /// because that toggles, and a restore is an assignment of a known state rather than a
        /// player pressing a key.
        /// </summary>
        public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot)
        {
            int size = inventory.GetSize();

            for (int i = 0; i < size; i++)
            {
                InventoryItem item = items != null && i < items.Count ? items[i] : null;
                inventory.RestoreSlot(i, item);
            }

            SelectedSlotIndex = selectedSlot >= 0 && selectedSlot < size ? selectedSlot : -1;
            OnSlotSelected?.Invoke(GetSlot(SelectedSlotIndex));
        }

        public void SelectSlot(int slotIndex)
        {
            if (slotIndex == SelectedSlotIndex)
                SelectedSlotIndex = -1;
            else
                SelectedSlotIndex = slotIndex;
        
            var slot = GetSlot(SelectedSlotIndex);
            OnSlotSelected?.Invoke(slot);
        }
    
        public bool TryAddItem(InventoryItem item)
        {
            return TryAddItem(item, out _);
        }

        public bool TryAddItem(InventoryItem item, out int index)
        {
            index = -1;
            if (!item) return false;
            bool result = inventory.TryAddItem(item, out index);
            return result;
        }

        public bool TryRemoveItem(int index)
        {
            bool result = inventory.TryRemoveItem(index);
            if (result && SelectedSlotIndex == index)
                SelectedSlotIndex = -1;
            return result;
        }
    
        public void DropItem(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= inventory.GetSize()) return;
            InventorySlot slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty) return;
        
            InventoryItem item = slot.Item;
            inventory.TryRemoveItem(slotIndex);
            if (SelectedSlotIndex == slotIndex)
                SelectSlot(-1);
            OnItemDropped?.Invoke(item);
        }

        public InventorySlot GetSlot(int index) => inventory.GetSlot(index);
        public InventorySlot GetSelectedSlot() => SelectedSlotIndex >= 0 ? inventory.GetSlot(SelectedSlotIndex) : null;
        public int GetInventorySize() => inventory.GetSize();
    
        public int FindEmptySlot() => inventory.FindEmptySlot();
    }
}
