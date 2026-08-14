using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
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
            player.Input.OnHotbarScrolled += ScrollSlot;
            player.Input.OnDropPressed += DropItem;
        }

        public override void OnDestroy()
        {
            networkItems.OnListChanged -= HandleNetworkListChanged;
            networkSelectedSlot.OnValueChanged -= HandleSelectedSlotChanged;

            // NetworkBehaviour.OnDestroy disposes this behaviour's NetworkVariables -- including
            // networkItems, a NetworkList backed by a native container -- and deregisters it from
            // its NetworkObject. Without this the native allocation leaks on every despawn.
            base.OnDestroy();
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

        private void ScrollSlot(int direction)
        {
            int target = HotbarNavigation.GetScrollTarget(SelectedSlotIndex, direction, inventorySize);
            if (target == HotbarNavigation.NoChange) return;

            SelectSlot(target);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SelectSlotServerRpc(int slotIndex)
        {
            networkSelectedSlot.Value =
                networkSelectedSlot.Value == slotIndex ? -1 : slotIndex;
        }

        /// <summary>
        /// Server-side hotbar assignment for a load. Writes through the NetworkList rather than
        /// into the local Inventory, so the owning client's own copy is rebuilt by the same
        /// replication path every other change uses — restoring the local inventory directly would
        /// leave the server's authoritative list saying something else.
        /// </summary>
        public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Save] RestoreSlots ignored on a client — the hotbar is server state.", this);
                return;
            }

            // The list is sized in InitializeNetworkState, which runs in OnNetworkSpawn. A restore
            // that beats it would index past the end.
            if (networkItems.Count < inventorySize) InitializeNetworkState();

            for (int i = 0; i < networkItems.Count; i++)
            {
                InventoryItem item = items != null && i < items.Count ? items[i] : null;
                networkItems[i] = item != null ? item.ID : default;
            }

            networkSelectedSlot.Value = selectedSlot >= 0 && selectedSlot < networkItems.Count ? selectedSlot : -1;
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
}
