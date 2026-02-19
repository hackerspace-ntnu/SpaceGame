
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
/// <summary>
/// Instance of InventoryComponent that represents the player's inventory.
/// Handles hotkey input for selecting items and dropping them,
/// and interacts with the EquipmentController to equip/unequip items based on selection.
/// Also spawns dropped items in the world with physics applied.
/// </summary>
public class PlayerInventory  : NetworkBehaviour
{
    
    private Inventory inventory;
    private InputAction hotkeyAction;
    [SerializeField] private HotbarController hotbarController;
    [SerializeField] private EquipmentController equipmentController;
    
    public event Action<int> OnSlotSelected;
    public int selectedSlotIndex { get; private set; } = -1;
    
    public List<InventoryItem> startingItems;
    private void Start()
    {
        if(!IsOwner) return;
        
        inventory  = new Inventory(4);
        
        foreach (var item in startingItems)
        {
            inventory.TryAddItem(item);
        }
        
        if (hotbarController == null) return;
        
        hotbarController.OnHotbarKeyPressed += HandleHotbarKey;
        hotbarController.OnDropPressed += HandleDrop;
    }
    
    
    private void OnDestroy()
    {
        if(!IsOwner) return;
        if (hotbarController == null) return;
        hotbarController.OnHotbarKeyPressed -= HandleHotbarKey;
        hotbarController.OnDropPressed -= HandleDrop;
    }
    
    /// <summary>
    /// Handles hotbar key input to select/deselect inventory slots and equip/unequip items.
    /// </summary>
    /// <param name="slotIndex"></param>
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
    
    /// <summary>
    /// Handles dropping the currently selected item from the inventory, unequipping it if necessary,
    /// and spawning it in the world with physics applied.
    /// </summary>
    private void HandleDrop()
    {
        if (selectedSlotIndex < 0)
            return;

        InventorySlot slot = inventory.GetSlot(selectedSlotIndex);
        if (slot == null || slot.Item == null)
            return;

        InventoryItem itemToDrop = slot.Item;

        if (!itemToDrop.itemPrefab)
        {
            Debug.LogWarning("itemprefab is not defined");
            return;
        }

        bool removed = TryRemoveItem(selectedSlotIndex);
        
        if (!removed) return;
        
        equipmentController.Unequip();
        SpawnDroppedItem(itemToDrop);

    }
    
    private void SpawnDroppedItem(InventoryItem item)
    {
        Vector3 dropPos = transform.position + transform.forward * 1.2f + Vector3.up * 0.5f;

        GameObject obj = Instantiate(item.itemPrefab, dropPos, Quaternion.identity);

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        if (rb != null)
        {
            Vector3 force = transform.forward * 1.5f + Vector3.up * 1.0f;
            rb.AddForce(force, ForceMode.Impulse);
        }
    }
    
    /// <summary>
    /// TryRemoveItem unequippis the item
    /// if the removed item was currently selected.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public bool TryRemoveItem(int index)
    {
        bool removed = inventory.TryRemoveItem(index);
        if(!removed) return false;
        
        if (index == selectedSlotIndex)
        {
            equipmentController.Unequip();
        }
        return true;
    }
    public InventorySlot GetSeletedSlot()
    {
        if (selectedSlotIndex < 0) {
            return null;
        }
        return inventory.GetSlot(selectedSlotIndex);
    }
    
}
