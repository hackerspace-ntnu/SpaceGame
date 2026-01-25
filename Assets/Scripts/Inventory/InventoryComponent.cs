using UnityEngine;
using UnityEngine.InputSystem.Interactions;

public class InventoryComponent : MonoBehaviour
{
    protected Inventory inventory { get; set; }
    
    public bool TryAddItem(InventoryItem item)
    {
        int index = inventory.FindEmptySlot();
        if (index == -1) {return false;}

        InventorySlot slot = inventory.GetSlot(index);
        slot.Item = item;
        return true;
    }

    public bool TryRemoveItem(int index)
    {
        if (index >= inventory.InventorySize) {return false;}
        InventorySlot slot = inventory.GetSlot(index);
        if (slot.Item == null) {return false;}

        slot.Item = null;
        return true;
    }

    public bool TryMoveItem(int to, int from)
    {
        if (from == to) {return true;}
        if (to >= inventory.InventorySize || from >= inventory.InventorySize) {return false;}
        InventorySlot slotTo = inventory.GetSlot(to);
        InventorySlot slotFrom = inventory.GetSlot(from);

        if (slotTo.Item == null) {
            slotTo.Item = slotFrom.Item;
            slotFrom.Item = null;
            return true;
            }
        return inventory.SwapItems(to,from);
    }
    
    public int InventorySize => inventory.InventorySize;
    public InventorySlot GetSlot(int index) => inventory.GetSlot(index);
}