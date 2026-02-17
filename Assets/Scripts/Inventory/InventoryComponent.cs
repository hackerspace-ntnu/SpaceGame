using System;
using UnityEngine;

/// <summary>
/// InventoryComponent is a base class for any component that has an inventory.
/// It connects the Inventory data structure to the Unity component system and provides methods for interacting with the inventory.
/// </summary>
public class InventoryComponent : MonoBehaviour
{
    protected Inventory inventory { get; set; }
    
    public event Action OnInventoryChanged;
    
    protected void NotifyInventoryChanged()
    {
        OnInventoryChanged?.Invoke();
    }
    
    public bool TryAddItem(InventoryItem item)
    {
        if (!item)
        {
            Debug.LogWarning("item is not defined for this gameObject");
            return false;
        }
        
        int index = inventory.FindEmptySlot();
        if (index == -1) {return false;}

        InventorySlot slot = inventory.GetSlot(index);
        slot.Item = item;
        NotifyInventoryChanged();
        return true;
    }

    public virtual bool TryRemoveItem(int index)
    {
        if (index >= inventory.InventorySize) {return false;}
        InventorySlot slot = inventory.GetSlot(index);
        if (slot.Item == null) {return false;}

        slot.Item = null;
        NotifyInventoryChanged();
        return true;
    }

    public bool TryMoveItem(int to, int from)
    {
        if (from == to) {return true;}
        if (to >= inventory.InventorySize || from >= inventory.InventorySize) {return false;}

        bool successfulMove;
        
        InventorySlot slotTo = inventory.GetSlot(to);
        InventorySlot slotFrom = inventory.GetSlot(from);

        if (slotTo.Item == null) {
            slotTo.Item = slotFrom.Item;
            slotFrom.Item = null;
            successfulMove = true;
        }
        else
        {
            successfulMove = inventory.SwapItems(to,from);;
        }

        if (successfulMove)
        {
            NotifyInventoryChanged();
        }
        
        return successfulMove;
    }
    
    public int InventorySize => inventory.InventorySize;
    public InventorySlot GetSlot(int index) => inventory.GetSlot(index);
}