using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemRepository", menuName = "Items/Item Repository", order = 0)]
public class ItemRepository : ScriptableObject
{ 
    
    public List<InventoryItem> items;
    
    public InventoryItem GetItemById(string itemId)
    {
        var item = items.Find(i => i.itemId == itemId);
        return item;
    }

    public void AddItem(InventoryItem item)
    {
        var existingItem = items.Find(i => i.itemId == item.itemId);
        if(existingItem) return;
        items.Add(item);
    }
}
