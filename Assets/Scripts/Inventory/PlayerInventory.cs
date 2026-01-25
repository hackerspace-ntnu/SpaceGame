
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInventory  : InventoryComponent
{
    
    private InputAction hotkeyAction;
    public HotbarController hotbarController;
    
    public event Action<int> OnSlotSelected;
    
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
        OnSlotSelected?.Invoke(slotIndex);
    }
    
}
