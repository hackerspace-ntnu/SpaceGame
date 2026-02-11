
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
    public int selectedSlotIndex { get; private set; } = -1;
    
    public List<InventoryItem> startingItems;
    private void Awake()
    {
        inventory = new Inventory(4);
        
        if (hotbarController == null) return;
        
        hotbarController.OnHotbarKeyPressed += HandleHotbarKey;
        hotbarController.OnDropPressed += HandleDrop;
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
        if (hotbarController == null) return;
        hotbarController.OnHotbarKeyPressed -= HandleHotbarKey;
        hotbarController.OnDropPressed -= HandleDrop;
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
    public override bool TryRemoveItem(int index)
    {
        if (index >= inventory.InventorySize) {return false;}
        InventorySlot slot = inventory.GetSlot(index);
        if (slot.Item == null) {return false;}

        slot.Item = null;
        NotifyInventoryChanged();
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
        return GetSlot(selectedSlotIndex);
    }
    
}
