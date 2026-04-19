// Gives any entity (NPC, enemy, creature) a full inventory — same underlying Inventory class the player uses.
// Does NOT need PlayerController. Drop on any GameObject alongside AgentController.
// Other components (EntityEquipmentController, EntityLootTable) reference this via GetComponent.
using System;
using System.Collections.Generic;
using UnityEngine;

public class EntityInventoryComponent : MonoBehaviour
{
    [SerializeField] private int inventorySize = 4;
    [SerializeField] private List<InventoryItem> startingItems;

    private Inventory inventory;

    public event Action<int, InventorySlot> OnSlotChanged;

    public int Size => inventorySize;

    private void Awake()
    {
        inventory = new Inventory(inventorySize);

        if (startingItems != null)
        {
            foreach (InventoryItem item in startingItems)
                inventory.TryAddItem(item);
        }

        inventory.OnSlotChanged += (index, slot) => OnSlotChanged?.Invoke(index, slot);
    }

    public bool TryAddItem(InventoryItem item) => inventory.TryAddItem(item);
    public bool TryAddItem(InventoryItem item, out int index) => inventory.TryAddItem(item, out index);
    public bool TryRemoveItem(int index) => inventory.TryRemoveItem(index);
    public InventorySlot GetSlot(int index) => inventory.GetSlot(index);
    public int FindEmptySlot() => inventory.FindEmptySlot();
    public List<string> GetItemIDs() => inventory.GetItemIDs();

    // Returns all non-empty items — used by EntityLootTable on death.
    public List<InventoryItem> GetAllItems()
    {
        List<InventoryItem> result = new List<InventoryItem>();
        for (int i = 0; i < inventory.GetSize(); i++)
        {
            InventorySlot slot = inventory.GetSlot(i);
            if (!slot.IsEmpty)
                result.Add(slot.Item);
        }
        return result;
    }
}
