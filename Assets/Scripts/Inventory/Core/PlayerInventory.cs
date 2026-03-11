
using System;
using System.Collections.Generic;
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
    
    public event Action OnInventoryChanged
    {
        add => inventory.OnInventoryChanged += value; 
        remove => inventory.OnInventoryChanged -= value;
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
        if (!item) return false;
        bool result = inventory.TryAddItem(item);
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
}
