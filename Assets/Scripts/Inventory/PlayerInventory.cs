
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInventory  : InventoryComponent
{
    
    private InputAction hotkeyAction;
    [SerializeField] private HotbarController hotbarController;
    [SerializeField] private EquipmentController equipmentController;
    
    public event Action<int> OnSlotSelected;
    private int selectedSlotIndex = -1;
    
    public List<InventoryItem> startingItems;
    private void Awake()
    {
        inventory = new Inventory(4);
        
        if (hotbarController != null)
            hotbarController.OnHotbarKeyPressed += HandleHotbarKey;
    }

    private void Start()
    {
        foreach (var item in startingItems)
        {
            TryAddItem(item);
        }
    }
    
    private void OnDestroy()
    {
        if (hotbarController != null)
            hotbarController.OnHotbarKeyPressed -= HandleHotbarKey;
    }
    
    private void HandleHotbarKey(int slotIndex)
    {
        if (slotIndex == selectedSlotIndex)
            selectedSlotIndex = -1;
        else
            selectedSlotIndex = slotIndex;   
        
        OnSlotSelected?.Invoke(selectedSlotIndex);

        if (selectedSlotIndex < 0)
        {
            equipmentController.Unequip();
            return;
        }
        
        InventorySlot slot = inventory.GetSlot(selectedSlotIndex);
        if (slot == null || slot.Item == null)
        {
            equipmentController.Unequip();
            return;
        }
        
        
        equipmentController.Equip(slot.Item);
    }
    
}
