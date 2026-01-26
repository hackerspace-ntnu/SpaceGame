using System;
using System.Collections.Generic;
using NUnit.Framework.Constraints;
using Unity.VisualScripting;
using UnityEngine;

public class Inventory
{
    private InventorySlot[] InventorySlots;
    
    public int InventorySize;
    public Inventory(int size)
    {
        InventorySize = size;
        InventorySlots = new InventorySlot[InventorySize];

        for (int i = 0; i<InventorySize; i++)
        {
            InventorySlots[i] = new InventorySlot
            {
                SlotIndex = i,
                Item = null
            };
        }
    }
    public InventorySlot GetSlot(int index)
    {
        if (index >= InventorySize) {return null;}

        return InventorySlots[index];
    }
    

    public bool SwapItems(int indexA, int indexB)
    {
    (InventorySlots[indexA].Item, InventorySlots[indexB].Item) = (InventorySlots[indexB].Item, InventorySlots[indexA].Item); 
    return true;
    }

    public int FindEmptySlot()
    {
        for (int i = 0; i <= InventorySize; i++)
        {
           if (InventorySlots[i].Item == null)
            {
                return i;
            } 
        }
        return -1;
    }
}
