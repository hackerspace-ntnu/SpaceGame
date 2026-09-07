using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Items
{
    public class PlayerInventoryComponent: MonoBehaviour, IPlayerInventory
    {
        [SerializeField] private int inventorySize = 4;
        [SerializeField] private List<InventoryItem> startingItems;
    
        private PlayerController player;
        private PlayerInventory playerInventory;

        public int SelectedSlotIndex => playerInventory.SelectedSlotIndex;
        public event Action<InventorySlot> OnSlotSelected
        {
            add => playerInventory.OnSlotSelected += value; 
            remove => playerInventory.OnSlotSelected -= value;
        }
        public event Action<int, InventorySlot> OnSlotChanged
        {
            add => playerInventory.OnSlotChanged += value; 
            remove => playerInventory.OnSlotChanged -= value;
        }
    
        public event Action<InventoryItem, ItemState> OnItemDropped
        {
            add => playerInventory.OnItemDropped += value;
            remove => playerInventory.OnItemDropped -= value;
        }

        private EquipmentController equipment;

        /// <summary>
        /// The hand this hotbar feeds. Resolved on demand rather than in Awake, for the reason
        /// <c>PlayerInventorySaveable</c> does the same: component order within a frame is not
        /// something to hang a null on.
        /// </summary>
        private EquipmentController Equipment =>
            equipment != null ? equipment : equipment = GetComponent<EquipmentController>();

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            playerInventory = new PlayerInventory(inventorySize, startingItems);
        }

        private void Start()
        {
            player.Input.OnHotbarPressed += SelectSlot;
            player.Input.OnHotbarScrolled += ScrollSlot;
            player.Input.OnDropPressed += DropItem;
        }

        public void SelectSlot(int slotIndex) => playerInventory.SelectSlot(slotIndex);

        public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot) =>
            playerInventory.RestoreSlots(items, selectedSlot);

        private void ScrollSlot(int direction)
        {
            int target = HotbarNavigation.GetScrollTarget(SelectedSlotIndex, direction, GetInventorySize());
            if (target == HotbarNavigation.NoChange) return;

            SelectSlot(target);
        }

        public bool TryAddItem(InventoryItem item) => playerInventory.TryAddItem(item);

        public bool TryAddItem(InventoryItem item, out int index) =>
            playerInventory.TryAddItem(item, out index);

        public bool TrySetSlot(int index, InventoryItem item)
        {
            if (index < 0 || index >= GetInventorySize()) return false;
            playerInventory.SetSlot(index, item);
            return true;
        }
        public bool TryRemoveItem(int index) => playerInventory.TryRemoveItem(index);
 
        private void DropItem()
        {
            // The held object has been diverging from its slot since it was equipped, so the same
            // write-back a save does has to run first — see PlayerInventoryNetwork's copy of this
            // for why it cannot be left to Unequip.
            Equipment?.WriteBackHeldItemState();

            playerInventory.DropItem(SelectedSlotIndex);
        }
    
        public InventorySlot GetSlot(int index) => playerInventory.GetSlot(index);
        public InventorySlot GetSelectedSlot() => playerInventory.GetSelectedSlot();
    
        /// <summary>
        /// What the player is holding, or null when they are holding nothing.
        ///
        /// Null-checked for the reason <c>PlayerInventoryNetwork</c>'s copy of this is: nothing
        /// selected is the state a hotbar starts in, and <c>PlayerInventory.GetSelectedSlot</c>
        /// answers that with null rather than with an empty slot.
        /// </summary>
        public InventoryItem GetSelectedItem()
        {
            InventorySlot slot = GetSelectedSlot();
            return slot == null || slot.IsEmpty ? null : slot.Item;
        }
        public int GetInventorySize() => playerInventory.GetInventorySize();
    }
}
