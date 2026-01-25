using UnityEngine;
using UnityEngine.InputSystem.Interactions;

public class InventoryComponent : MonoBehaviour
{
    public Inventory Inventory { get; set; }
    public bool TryAddItem(InventoryItem item)
    {
        int index = Inventory.FindEmptySlot();
        if (index == -1) {return false;}

        InventorySlot slot = Inventory.GetSlot(index);
        slot.Item = item;
        return true;
    }

    public bool TryRemoveItem(int index)
    {
        if (index >= Inventory.InventorySize) {return false;}
        InventorySlot slot = Inventory.GetSlot(index);
        if (slot.Item == null) {return false;}

        slot.Item = null;
        return true;
    }

    public bool TryMoveItem(int to, int from)
    {
        if (from == to) {return true;}
        if (to >= Inventory.InventorySize || from >= Inventory.InventorySize) {return false;}
        InventorySlot slotTo = Inventory.GetSlot(to);
        InventorySlot slotFrom = Inventory.GetSlot(from);

        if (slotTo.Item == null) {
            slotTo.Item = slotFrom.Item;
            slotFrom.Item = null;
            return true;
            }
        return Inventory.SwapItems(to,from);
    }
}