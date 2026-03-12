using System;
using System.Collections.Generic;
using UnityEngine;

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
    
    public event Action<InventoryItem> OnItemDropped
    {
        add => playerInventory.OnItemDropped += value; 
        remove => playerInventory.OnItemDropped -= value;
    }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        playerInventory = new PlayerInventory(inventorySize, startingItems);
    }

    private void Start()
    {
        player.Input.OnHotbarPressed += SelectSlot;
        player.Input.OnDropPressed += DropItem;
    }

    public void SelectSlot(int slotIndex) => playerInventory.SelectSlot(slotIndex);
    public bool TryAddItem(InventoryItem item) => playerInventory.TryAddItem(item);
    public bool TryRemoveItem(int index) => playerInventory.TryRemoveItem(index);
 
    private void DropItem()
    {
        playerInventory.DropItem(SelectedSlotIndex);
    }
    
    public InventorySlot GetSlot(int index) => playerInventory.GetSlot(index);
    public InventorySlot GetSelectedSlot() => playerInventory.GetSelectedSlot();
    
    public InventoryItem GetSelectedItem()
    {
        var slot = GetSelectedSlot();
        return slot.IsEmpty ? null : slot.Item;
    }
    public int GetInventorySize() => playerInventory.GetInventorySize();
}
