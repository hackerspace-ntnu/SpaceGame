
using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Inventory holds an array of InventorySlots, which can hold InventoryItems.
/// It provides methods to get a slot, swap items between slots, and find an empty slot.
/// </summary>
public class Inventory
{
    private NetworkList<InventorySlot> slots;
    
    public event Action OnInventoryChanged;
    
    public Inventory(int size)
    {
        slots = new NetworkList<InventorySlot>();

        for (int i = 0; i < size; i++)
        {
            InventorySlot slot = new InventorySlot {ItemId = -1, Amount = 0 };
            Debug.Log("Added slot " + i + " with itemId " + slot.ItemId);
            slots.Add(slot);
        }
    }

    public void UpdateInventorySlot(int index, int itemId)
    {
        Debug.Log("Updating slot " + index + " with itemId " + itemId);
        InventorySlot slot = slots[index].UpdateSlot(itemId);
        slots[index] = slot;
    }

    
    public bool TryAddItem(InventoryItem item)
    {
        int index = FindEmptySlot();
        if (index == -1) return false;

        UpdateInventorySlot(index, item.itemId);
        OnInventoryChanged?.Invoke();
        return true;
    }
    
    public bool TryRemoveItem(int index)
    {
        if (index >= slots.Count) return false;

        UpdateInventorySlot(index, -1);
        OnInventoryChanged?.Invoke();
        return true;
    }
    
    public bool TryMoveItem(int to, int from)
    {
        if (from == to) {return true;}
        if (to >= slots.Count || from >= slots.Count) {return false;}

        bool successfulMove;
        
        InventorySlot slotTo = GetSlot(to);
        InventorySlot slotFrom = GetSlot(from);

        if (slotTo.ItemId == -1) {
            slotTo.ItemId = slotFrom.ItemId;
            slotFrom.ItemId = -1;
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
        return slots.Count;
    }

    public InventorySlot GetSlot(int index)
    {
        if (index < slots.Count)
        {
            return slots[index];
        } 
        throw new IndexOutOfRangeException($"Index {index} is out of range for inventory size {slots.Count}");
    }

    public bool SwapItems(int indexA, int indexB)
    {
        InventorySlot slotA = GetSlot(indexA);
        InventorySlot slotB = GetSlot(indexB);
        
        slots[indexA].UpdateSlot(slotB.ItemId);
        slots[indexB].UpdateSlot(slotA.ItemId);
        return true;
    }

    public int FindEmptySlot()
    {
        for (int i = 0; i < slots.Count; i++)
        {
           if (slots[i].IsEmpty())
           { 
               return i;
           } 
        }
        return -1;
    }
}
