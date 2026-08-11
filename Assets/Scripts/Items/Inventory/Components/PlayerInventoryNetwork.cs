using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventoryNetwork : NetworkBehaviour, IPlayerInventory
{
    [SerializeField] private int inventorySize = 4;
    [SerializeField] private List<InventoryItem> startingItems;
    
    
    private PlayerInventory inventory;
    PlayerController player;
    
    private NetworkList<FixedString64Bytes> networkItems = new();
    private NetworkVariable<int> networkSelectedSlot = new(-1);

    public int SelectedSlotIndex => networkSelectedSlot.Value;
    
    public event Action<InventorySlot> OnSlotSelected;
    public event Action<int, InventorySlot> OnSlotChanged;

    public event Action<InventoryItem> OnItemDropped;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        inventory = new PlayerInventory(inventorySize, startingItems);
        
        networkItems.OnListChanged += HandleNetworkListChanged;
        networkSelectedSlot.OnValueChanged += HandleSelectedSlotChanged;
        
        if (IsServer)
        {
            InitializeNetworkState();
        }
    }
    
    private void Start()
    {
        if (!IsOwner) return;
        player.Input.OnHotbarPressed += SelectSlot;
        player.Input.OnDropPressed += DropItem;
    }

    public override void OnDestroy()
    {
        networkItems.OnListChanged -= HandleNetworkListChanged;
        networkSelectedSlot.OnValueChanged -= HandleSelectedSlotChanged;
    }

    private void HandleNetworkListChanged(NetworkListEvent<FixedString64Bytes> changeEvent)
    {
        int index = changeEvent.Index;
        
        if (index < 0 || index >= networkItems.Count)
            return;
        
        var id = networkItems[index];

        InventoryItem item = string.IsNullOrEmpty(id.Value)
            ? null
            : Registry<InventoryItem>.Get(id.Value);

        inventory.SetItem(index, item);
        OnSlotChanged?.Invoke(index, inventory.GetSlot(index));
    }

    private void HandleSelectedSlotChanged(int oldValue, int newValue)
    {
        inventory.SelectSlot(newValue);
        OnSlotSelected?.Invoke(GetSelectedSlot());
    }
    
    private void InitializeNetworkState()
    {
        networkItems.Clear();

        // Fill with empty slots first
        for (int i = 0; i < inventorySize; i++)
            networkItems.Add(default);

        // Add starting items
        foreach (var item in startingItems)
        {
            int index = inventory.FindEmptySlot();
            if (index != -1)
                networkItems[index] = item.ID;
        }
    }

    // --- Client requests selection ---
    public void SelectSlot(int slotIndex)
    {
        if(!IsOwner) return;
        SelectSlotServerRpc(slotIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SelectSlotServerRpc(int slotIndex)
    {
        networkSelectedSlot.Value =
            networkSelectedSlot.Value == slotIndex ? -1 : slotIndex;
    }

    // --- Client requests add ---
    public bool TryAddItem(InventoryItem item)
    {
        Network.Execute(
            local: () => AddItem(item),
            client: () => TryAddItemServerRpc(item.ID));
        
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryAddItemServerRpc(string itemId)
    {
        var item = Registry<InventoryItem>.Get(itemId);
        AddItem(item);
    }

    private void AddItem(InventoryItem item)
    {
        int index = inventory.FindEmptySlot();
        if (index == -1) return;

        networkItems[index] = item.ID;
    }

    // --- Client requests remove ---
    public bool TryRemoveItem(int index)
    {
        Network.Execute(
            local: () => RemoveItem(index),
            client: () => TryRemoveItemServerRpc(index));
        
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryRemoveItemServerRpc(int index)
    {
        RemoveItem(index);
    }

    private void RemoveItem(int index)
    {
        if (index < 0 || index >= networkItems.Count) return;

        networkItems[index] = default;
    }

    private void DropItem()
    {
        if (!IsOwner) return;
        DropItemServerRpc(networkSelectedSlot.Value);
    }

    [Rpc(SendTo.Server)]
    private void DropItemServerRpc(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= networkItems.Count) return;

        var id = networkItems[slotIndex];
        if (string.IsNullOrEmpty(id.Value)) return;

        var item = Registry<InventoryItem>.Get(id.Value);

        networkItems[slotIndex] = default;
        networkSelectedSlot.Value = -1;

        OnItemDropped?.Invoke(item);
    }
    
    public InventorySlot GetSlot(int index)
    {
        return inventory.GetSlot(index);
    }

    public InventorySlot GetSelectedSlot()
    {
        return GetSlot(networkSelectedSlot.Value);
    }
    
    public InventoryItem GetSelectedItem()
    {
        var slot = GetSelectedSlot();
        return slot.IsEmpty ? null : slot.Item;
    }

    public int GetInventorySize() => inventorySize;
}
