
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements.Experimental;

public class PlayerInventory  : InventoryComponent

{
    private InputAction hotkeyAction;
    
    public List<InventoryItem> startingItems;
    private void Awake()
    {
        Inventory = new Inventory(4);
        hotkeyAction = InputSystem.actions.FindAction("Hotkey");
    }

    private void Start()
    {
        foreach (var item in startingItems)
        {
            TryAddItem(item);
        }
    }
    private void Update()
    {
        if (hotkeyAction.WasPressedThisFrame()) { 
            float value = hotkeyAction.ReadValue<float>();
            string itemName = Inventory.GetSlot((int)value-1).Item.itemName;
            Debug.Log(itemName);
        }
    }
    
}
