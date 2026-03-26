using UnityEngine;

public class InventorySlot
{
    public int Index { get; private set; }
    public InventoryItem Item { get; set; }
    
    public bool IsEmpty => Item == null;

    public InventorySlot(int index)
    {
        Index = index;
        Item = null;
    }
    
    public override string ToString()
    {
        return "Slot " + (!IsEmpty ? Item.itemName : "Empty");
    }
}
