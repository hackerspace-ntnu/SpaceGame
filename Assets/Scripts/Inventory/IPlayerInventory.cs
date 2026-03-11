using System;
using UnityEngine;

public interface IPlayerInventory
{
    int SelectedSlotIndex { get; }
    event Action<InventorySlot> OnSlotSelected;
    event Action OnInventoryChanged;
    event Action<InventoryItem> OnItemDropped;

    bool TryAddItem(InventoryItem item);
    bool TryRemoveItem(int index);
    void SelectSlot(int slotIndex);
    int GetInventorySize();
    InventorySlot GetSlot(int index);
    InventorySlot GetSelectedSlot();
    InventoryItem GetSelectedItem();
}
