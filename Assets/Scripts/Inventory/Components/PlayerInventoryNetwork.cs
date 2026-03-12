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
    public event Action<int, InventorySlot> OnSlotChanged
    {
        add => inventory.OnSlotChanged += value; 
        remove => inventory.OnSlotChanged -= value;
    }
    
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
        inventory = new PlayerInventory(inventorySize, startingItems);
        
        if (IsServer)
        {
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
        int index = changeEvent.Index;

        var id = networkItems[index];

        InventoryItem item = string.IsNullOrEmpty(id.Value)
            ? null
            : Registry<InventoryItem>.Get(id.Value);

        inventory.SetItem(index, item);
    }

    private void HandleSelectedSlotChanged(int oldValue, int newValue)
    {
        var slot = GetSelectedSlot();
        OnSlotSelected?.Invoke(slot);
    }
    
    private void SyncInventory()
    {
        var ids = inventory.GetItemIDs();

        for (int i = 0; i < ids.Count; i++)
        {
            networkItems[i] = ids[i];
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

        if (inventory.TryAddItem(item, out int index))
            networkItems[index] = itemId;
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
            networkItems[index] = null;
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
