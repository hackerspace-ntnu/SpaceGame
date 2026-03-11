
using System;
using System.Collections.Generic;

/// <summary>
/// Inventory holds an array of InventorySlots, which can hold InventoryItems.
/// It provides methods to get a slot, swap items between slots, and find an empty slot.
/// </summary>
public class Inventory
{
    private InventorySlot[] slots;
    
    public event Action OnInventoryChanged;
    
    public Inventory(int size)
    {
        slots = new InventorySlot[size];

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new InventorySlot(i);
        }
    }
    
    public List<string> GetItemIDs()
    {
        List<string> ids = new();

        for (int i = 0; i < GetSize(); i++)
        {
            var slot = GetSlot(i);
            ids.Add(slot.IsEmpty ? null : slot.Item.ID);
        }

        return ids;
    }
    
    public void SetItem(int index, InventoryItem item)
    {
        var slot = GetSlot(index);
        slot.Item = item;
    }
    
    public bool TryAddItem(InventoryItem item)
    {
        int index = FindEmptySlot();
        if (index == -1) return false;
        
        SetItem(index, item);
        OnInventoryChanged?.Invoke();
        return true;
    }
    
    public bool TryRemoveItem(int index)
    {
        if (index >= slots.Length) return false;
        if (!slots[index].Item) return false;
        
        SetItem(index, null);
        OnInventoryChanged?.Invoke();
        return true;
    }
    
    public bool TryMoveItem(int to, int from)
    {
        if (from == to) {return true;}
        if (to >= slots.Length || from >= slots.Length) {return false;}

        bool successfulMove;
        
        InventorySlot slotTo = GetSlot(to);
        InventorySlot slotFrom = GetSlot(from);

        if (slotTo.Item == null) {
            slotTo.Item = slotFrom.Item;
            slotFrom.Item = null;
            successfulMove = true;
        }
        else
        {
            successfulMove = SwapItems(to,from);;
        }

        if (successfulMove)
        {
            OnInventoryChanged?.Invoke();
        }
        
        return successfulMove;
    }
    
    public int GetSize()
    {
        return slots.Length;
    }

    public InventorySlot GetSlot(int index)
    {
        if(index < 0) return null;
        
        if (index < slots.Length)
        {
            return slots[index];
        } 
        
        return null;
    }

    public bool SwapItems(int indexA, int indexB)
    {
        (slots[indexA].Item, slots[indexB].Item) = (slots[indexB].Item, slots[indexA].Item); 
        return true;
    }

    public int FindEmptySlot()
    {
        for (int i = 0; i < slots.Length; i++)
        {
           if (!slots[i].Item)
           { 
               return i;
           } 
        }
        return -1;
    }
}
