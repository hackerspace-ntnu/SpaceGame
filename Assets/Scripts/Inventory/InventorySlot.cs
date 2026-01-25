using UnityEngine;

public class InventorySlot
{
    public int SlotIndex;
    public InventoryItem Item { get; set; }


    public override string ToString()
    {
        return "Slot " + SlotIndex + ": " + (Item != null ? Item.itemName : "Empty");
    }
}
