
using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
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
    
    public event Action<int> OnSlotSelected;
    public event Action OnInventoryChanged;
    public int selectedSlotIndex { get; private set; } = -1;
    
    public List<InventoryItem> startingItems;

    private void Awake()
    {
        InitializeInventory();
    }

    public override void OnNetworkSpawn()
    {
        InitializeInput();
    }
    
    private void InitializeInventory()
    {
        inventory  = new Inventory(4);
        
        foreach (var item in startingItems)
        {
            inventory.TryAddItem(item);
        }
    }
    
    private void InitializeInput()
    {
        if (!IsOwner)
        {
            hotbarController.enabled = false;
            return;
        };
        
        if (hotbarController == null) return;
        
        hotbarController.OnHotbarKeyPressed += HandleHotbarKey;
        hotbarController.OnDropPressed += HandleDrop;
        inventory.OnInventoryChanged += () => OnInventoryChanged?.Invoke();
    }


    public override void OnDestroy()
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
    }
    
    
    /// <summary>
    /// Handles dropping the currently selected item from the inventory, unequipping it if necessary,
    /// and spawning it in the world with physics applied.
    /// </summary>
    private void HandleDrop()
    {
        if (!IsOwner && selectedSlotIndex < 0) return;
        if (selectedSlotIndex < 0 || selectedSlotIndex >= inventory.GetSize()) return;

        InventorySlot slot = inventory.GetSlot(selectedSlotIndex);
        if (slot == null || slot.Item == null) return;

        InventoryItem item = slot.Item;
        TryRemoveItem(selectedSlotIndex);
        DropItemServerRpc(item.ItemId);
    }

    [ServerRpc]
    private void DropItemServerRpc(string itemId)
    {
        InventoryItem item = GameManager.Instance.GetItem(itemId);
        SpawnDroppedItem(item);
    }
    
    
    private void SpawnDroppedItem(InventoryItem item)
    {
        Vector3 dropPos = transform.position + transform.forward * 1.2f + Vector3.up * 0.5f;

        GameObject obj = Instantiate(item.itemPrefab, dropPos, Quaternion.identity);
        var networkObject = obj.GetComponent<NetworkObject>();
        networkObject.Spawn();
        
        ApplyForceToDroppedItemClientRpc(networkObject);

    }
    
    [ClientRpc]
    private void ApplyForceToDroppedItemClientRpc(NetworkObjectReference droppedItem)
    {
       var droppedItemObject = droppedItem.TryGet(out NetworkObject networkObject) ? networkObject.gameObject : null;
        Rigidbody rb = droppedItemObject?.GetComponent<Rigidbody>();

        if (rb == null) return;
        
        rb.isKinematic = false;
        Vector3 force = transform.forward * 1.5f + Vector3.up * 1.0f;
        rb.AddForce(force, ForceMode.Impulse);
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public bool TryAddItem(InventoryItem item)
    {
        if (!item) return false;

        return inventory.TryAddItem(item);
    } 

    /// <summary>
    /// TryRemoveItem unequippis the item
    /// if the removed item was currently selected.
    /// </summary>
    /// <param name="itemIndex"></param>
    /// <returns></returns>
    public bool TryRemoveItem(int itemIndex)
    {
        return inventory.TryRemoveItem(itemIndex);
    }

    
    public InventorySlot GetSlot(int index)
    {
        return inventory.GetSlot(index);
    }
    
    public InventorySlot GetSelectedSlot()
    {
        if (selectedSlotIndex < 0) {
            return null;
        }
        return inventory.GetSlot(selectedSlotIndex);
    }
    
    public int GetInventorySize()
    {
        return inventory.GetSize();
    }
}
