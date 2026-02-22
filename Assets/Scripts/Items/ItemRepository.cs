using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemRepository", menuName = "Items/Item Repository", order = 0)]
public class ItemRepository : ScriptableObject
{ 
    
    public List<InventoryItem> items;
    
    public InventoryItem GetItemById(int itemId)
    {
        var item = items.Find(i => i.itemId == itemId);
        return item;
    }
}
