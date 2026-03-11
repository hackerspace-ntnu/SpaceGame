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
    public event Action OnInventoryChanged;
    
    public event Action<InventoryItem> OnItemDropped
    {
        add => inventory.OnItemDropped += value; 
        remove => inventory.OnItemDropped -= value;
    }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            inventory = new PlayerInventory(inventorySize, startingItems);
            SyncInventory();
        }

        networkItems.OnListChanged += HandleNetworkListChanged;
        networkSelectedSlot.OnValueChanged += HandleSelectedSlotChanged;
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
        OnInventoryChanged?.Invoke();
    }

    private void HandleSelectedSlotChanged(int oldValue, int newValue)
    {
        var slot = GetSelectedSlot();
        OnSlotSelected?.Invoke(slot);
    }
    
    private void SyncInventory()
    {
        networkItems.Clear();

        var ids = inventory.GetItemIDs();

        foreach (var id in ids)
            networkItems.Add(id);
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
        inventory.SelectSlot(slotIndex);
        networkSelectedSlot.Value = inventory.SelectedSlotIndex;
    }

    // --- Client requests add ---
    public bool TryAddItem(InventoryItem item)
    {
        if (!IsOwner) return false;
        TryAddItemServerRpc(item.ID);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryAddItemServerRpc(string itemId)
    {
        InventoryItem item = Registry<InventoryItem>.Get(itemId);

        if (inventory.TryAddItem(item))
            SyncInventory();
    }

    // --- Client requests remove ---
    public bool TryRemoveItem(int index)
    {
        if (!IsOwner) return false;
        TryRemoveItemServerRpc(index);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryRemoveItemServerRpc(int index)
    {
        if (inventory.TryRemoveItem(index))
        {
            SyncInventory();
        }
    }

    private void DropItem()
    {
        if (!IsOwner) return;
        DropItemServerRpc(networkSelectedSlot.Value);
    }

    [Rpc(SendTo.Server)]
    private void DropItemServerRpc(int slotIndex)
    {
        inventory.DropItem(slotIndex);
        SyncInventory(); 
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
