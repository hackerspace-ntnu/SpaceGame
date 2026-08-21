// Gives any entity (NPC, enemy, creature) a full inventory — same underlying Inventory class the player uses.
// Does NOT need PlayerController. Drop on any GameObject alongside AgentController.
// Other components (EntityEquipmentController, EntityLootTable) reference this via GetComponent.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
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

        /// <summary>
        /// Writes a named slot for a save being loaded, replacing whatever the prefab's starting items
        /// put there.
        ///
        /// TryAddItem will not do: it finds the first free slot, so an NPC whose second slot was emptied
        /// during play would have everything shift left, and it refuses a null item — which is the
        /// ordinary contents of a slot the player looted. Raises OnSlotChanged, which is what makes
        /// <see cref="EntityEquipmentController"/> re-equip a restored weapon without being told.
        /// </summary>
        public void RestoreSlot(int index, InventoryItem item) => inventory.RestoreSlot(index, item);

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
}
