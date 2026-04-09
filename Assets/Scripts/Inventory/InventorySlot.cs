using UnityEngine;

public class InventorySlot
{
    public InventoryItem Item { get; set; }


    public override string ToString()
    {
        return "Slot " + (Item != null ? Item.itemName : "Empty");
    }
}
