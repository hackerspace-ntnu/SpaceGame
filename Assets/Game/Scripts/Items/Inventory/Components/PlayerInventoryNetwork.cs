using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// One hotbar slot on the wire: which item, and how full it is.
    ///
    /// <para>
    /// The charge is here rather than being left to <see cref="ItemState"/> because
    /// <c>ItemState</c> does not replicate at all — it is a bag on the server's own slot. That was
    /// harmless while everything in it was invisible (a magazine count, a cooldown), and stopped
    /// being harmless the moment an item grew a gauge the player reads: a client's own oxygen tank
    /// painted its authored starting charge and stayed there while the server drained it.
    /// </para>
    /// <para>
    /// One byte, quantised by <see cref="SupplyCharge.ToByte"/> — finer than the whole percent any
    /// readout shows. Zero for the great majority of items, which hold nothing; the receiving end
    /// knows from the item id whether the number means anything.
    /// </para>
    /// </summary>
    public struct HotbarSlotWire : INetworkSerializable, IEquatable<HotbarSlotWire>
    {
        public FixedString64Bytes ItemId;
        public byte Charge;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Charge);
        }

        /// <summary>
        /// Required by <c>NetworkList</c>, which uses it to decide whether a write is a change
        /// worth telling anyone about.
        /// </summary>
        public bool Equals(HotbarSlotWire other) =>
            ItemId.Equals(other.ItemId) && Charge == other.Charge;

        public override bool Equals(object obj) => obj is HotbarSlotWire other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ItemId.GetHashCode(), Charge);

        /// <summary>
        /// The wire form of a slot holding <paramref name="item"/>.
        ///
        /// <para>
        /// The empty case is typed <c>default(FixedString64Bytes)</c> rather than written as
        /// <c>usable ? item.ID : default</c>. Both arms of that ternary would be strings, so C#
        /// types the whole expression as string and converts the RESULT — meaning an empty slot
        /// converts <c>default(string)</c>, i.e. null, and FixedString64Bytes throws an NRE on null.
        /// That took the entire inventory restore down once already.
        /// </para>
        /// </summary>
        public static HotbarSlotWire For(InventoryItem item, float charge) => new()
        {
            ItemId = item != null && !string.IsNullOrEmpty(item.ID)
                ? new FixedString64Bytes(item.ID)
                : default(FixedString64Bytes),
            Charge = SupplyCharge.ToByte(charge),
        };
    }

    public class PlayerInventoryNetwork : NetworkBehaviour, IPlayerInventory
    {
        [SerializeField] private int inventorySize = 4;
        [SerializeField] private List<InventoryItem> startingItems;
    
    
        private PlayerInventory inventory;
        PlayerController player;
    
        private NetworkList<HotbarSlotWire> networkItems = new();
        private NetworkVariable<int> networkSelectedSlot = new(-1);

        public int SelectedSlotIndex => networkSelectedSlot.Value;
    
        public event Action<InventorySlot> OnSlotSelected;
        public event Action<int, InventorySlot> OnSlotChanged;

        public event Action<InventoryItem, float> OnItemDropped;

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
                return;
            }

            AdoptCurrentState();
        }

        /// <summary>
        /// Take up the hotbar as it already stands, for a machine that was not here when it was set.
        ///
        /// Both subscriptions above only fire on CHANGE. A player object that spawns on a client
        /// mid-session — a late joiner, or anyone whose player streams in — arrives with the
        /// current values already in the NetworkList and NetworkVariable and no change events
        /// coming, so without this their local inventory stays empty and their hands stay empty:
        /// every other player looks unarmed, and stays that way until they next switch slots.
        ///
        /// The events are raised for anything already listening, but the local model is what
        /// matters here — OnNetworkSpawn runs before any listener's Start, so a component that
        /// subscribes there would miss these. EquipmentController therefore reads the current
        /// selection in its own Start rather than waiting to be told.
        /// </summary>
        private void AdoptCurrentState()
        {
            for (int i = 0; i < networkItems.Count; i++) ApplyWire(i);

            inventory.SelectSlot(networkSelectedSlot.Value);
            OnSlotSelected?.Invoke(GetSelectedSlot());
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

        private void HandleNetworkListChanged(NetworkListEvent<HotbarSlotWire> changeEvent)
        {
            int index = changeEvent.Index;

            if (index < 0 || index >= networkItems.Count)
                return;

            ApplyWire(index);
        }

        /// <summary>
        /// Rebuild one local slot from the wire: the item, then the per-instance charge that came
        /// with it.
        ///
        /// <para>
        /// The order matters and is not interchangeable. <c>Inventory.SetItem</c> clears the slot's
        /// <see cref="ItemState"/> whenever the item actually changes — which is right, since a slot
        /// that changed hands must not keep the last item's ammo — so a charge written first would
        /// be wiped by the assignment that follows.
        /// </para>
        /// <para>
        /// Only written for an item that CARRIES a charge. Stamping every rifle's slot with a bag
        /// saying "0%" would put an ItemState on every slot in the game and write one into every
        /// save file.
        /// </para>
        /// </summary>
        private void ApplyWire(int index)
        {
            HotbarSlotWire wire = networkItems[index];

            InventoryItem item = string.IsNullOrEmpty(wire.ItemId.Value)
                ? null
                : Registry<InventoryItem>.Get(wire.ItemId.Value);

            inventory.SetItem(index, item);

            InventorySlot slot = inventory.GetSlot(index);

            if (slot != null && SupplyCharge.Carries(item))
            {
                slot.State ??= new ItemState();
                SupplyCharge.Write(slot.State, SupplyCharge.FromByte(wire.Charge));
            }

            OnSlotChanged?.Invoke(index, slot);
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

            // Add starting items. A null entry or an unset ID is skipped rather than assigned:
            // FixedString64Bytes throws on a null string, and one bad entry in the inspector list
            // would otherwise take the whole spawn down.
            foreach (var item in startingItems)
            {
                if (item == null || string.IsNullOrEmpty(item.ID)) continue;

                int index = inventory.FindEmptySlot();
                if (index != -1)
                    networkItems[index] = HotbarSlotWire.For(item, SupplyCharge.StartingChargeOf(item));
            }
        }

        // --- Client requests selection ---
        public void SelectSlot(int slotIndex)
        {
            if(!IsOwner) return;

            // The Hotbar map still binds keys past the bar's width — 4 on a three-slot bar — and
            // a selection past the end would clear the hands for a slot that does not exist.
            if (slotIndex >= inventorySize) return;

            SelectSlotServerRpc(slotIndex);
        }

        private void ScrollSlot(int direction)
        {
            int target = HotbarNavigation.GetScrollTarget(SelectedSlotIndex, direction, inventorySize);
            if (target == HotbarNavigation.NoChange) return;

            SelectSlot(target);
        }

        // Owner, not Everyone. A hotbar belongs to the player holding it: the default permission let
        // any client in the session change what anybody else was carrying or holding.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
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
                bool usable = item != null && !string.IsNullOrEmpty(item.ID);

                // The charge is deliberately the item's AUTHORED starting value here and not
                // the saved one: the bags have not been restored yet (see InventorySaveCodec.Restore,
                // which puts them back after this call and then republishes). Writing a zero here
                // instead would flash every restored tank empty for one wire round.
                networkItems[i] = usable
                    ? HotbarSlotWire.For(item, SupplyCharge.StartingChargeOf(item))
                    : default;
            }

            networkSelectedSlot.Value = selectedSlot >= 0 && selectedSlot < networkItems.Count ? selectedSlot : -1;
        }

        public bool TrySetSlot(int index, InventoryItem item)
        {
            if (!Network.Simulates(this))
            {
                Debug.LogWarning("[Inventory] TrySetSlot ignored on a client — the hotbar is server state.", this);
                return false;
            }

            if (index < 0 || index >= networkItems.Count) return false;

            networkItems[index] = HotbarSlotWire.For(item, SupplyCharge.StartingChargeOf(item));
            return true;
        }

        /// <inheritdoc/>
        public void PublishSlotCharges()
        {
            if (!Network.Simulates(this)) return;

            for (int i = 0; i < networkItems.Count; i++)
            {
                InventorySlot slot = inventory?.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;

                float charge = SupplyCharge.Read(slot.State);
                if (charge < 0f) continue;

                HotbarSlotWire wire = networkItems[i];
                byte quantised = SupplyCharge.ToByte(charge);
                if (wire.Charge == quantised) continue;

                wire.Charge = quantised;
                networkItems[i] = wire;
            }
        }

        // --- Client requests add ---

        /// <summary>
        /// Put <paramref name="item"/> in the first free hotbar slot, and say whether it went in.
        ///
        /// The answer is only real on the authority, and it has to be: callers act on it. A pickup
        /// despawns the world object when this returns true, and the backpack decides whether to
        /// overflow into the pack. An earlier version returned a flat <c>true</c> whatever happened,
        /// so picking anything up with a full hotbar deleted it.
        ///
        /// A client gets an optimistic true. It cannot get a real answer without waiting for a round
        /// trip, and no caller on the client path consumes one — every caller that acts on the
        /// result (pickup, backpack transfer) already runs server-side.
        /// </summary>
        public bool TryAddItem(InventoryItem item) => TryAddItem(item, out _);

        /// <inheritdoc/>
        /// <remarks>
        /// A client's optimistic true comes back with index -1, not with a guess. The slot it will
        /// land in is the server's to decide, so a client that wrote per-instance state into the
        /// index it predicted would be writing it into whatever the server later put there.
        /// </remarks>
        public bool TryAddItem(InventoryItem item, out int index)
        {
            index = -1;

            if (!Network.Simulates(this))
            {
                TryAddItemServerRpc(item != null ? item.ID : string.Empty);
                return true;
            }

            return AddItem(item, out index);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TryAddItemServerRpc(string itemId)
        {
            var item = Registry<InventoryItem>.Get(itemId);
            AddItem(item, out _);
        }

        private bool AddItem(InventoryItem item, out int slot)
        {
            slot = -1;

            // Registry<InventoryItem>.Get returns null for an id this build does not know, so the
            // caller above can hand us nothing at all. Assigning its ID would throw inside
            // FixedString64Bytes rather than simply failing to pick the item up.
            if (item == null || string.IsNullOrEmpty(item.ID))
            {
                Debug.LogWarning("[Inventory] Ignored an add for an item with no id.", this);
                return false;
            }

            int index = inventory.FindEmptySlot();
            if (index == -1) return false;

            networkItems[index] = HotbarSlotWire.For(item, SupplyCharge.StartingChargeOf(item));
            slot = index;
            return true;
        }

        // --- Client requests remove ---
        public bool TryRemoveItem(int index)
        {
            if (!Network.Simulates(this))
            {
                TryRemoveItemServerRpc(index);
                return true;
            }

            return RemoveItem(index);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TryRemoveItemServerRpc(int index)
        {
            RemoveItem(index);
        }

        private bool RemoveItem(int index)
        {
            if (index < 0 || index >= networkItems.Count) return false;

            networkItems[index] = default;
            return true;
        }

        private void DropItem()
        {
            if (!IsOwner) return;
            DropItemServerRpc(networkSelectedSlot.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DropItemServerRpc(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= networkItems.Count) return;

            HotbarSlotWire wire = networkItems[slotIndex];
            if (string.IsNullOrEmpty(wire.ItemId.Value)) return;

            var item = Registry<InventoryItem>.Get(wire.ItemId.Value);

            // The bag before the slot is cleared, so a dropped tank hits the ground holding what it
            // held in the hand rather than reverting to its prefab's starting charge.
            InventorySlot slot = inventory?.GetSlot(slotIndex);
            float charge = SupplyCharge.Read(slot?.State);

            networkItems[slotIndex] = default;
            networkSelectedSlot.Value = -1;

            OnItemDropped?.Invoke(item, charge);
        }
    
        public InventorySlot GetSlot(int index)
        {
            return inventory.GetSlot(index);
        }

        public InventorySlot GetSelectedSlot()
        {
            return GetSlot(networkSelectedSlot.Value);
        }
    
        /// <summary>
        /// What the player is holding, or null when they are holding nothing.
        ///
        /// The null check is load-bearing, not defensive noise: <see cref="networkSelectedSlot"/>
        /// starts at -1 — nothing selected is the state every player spawns in — and
        /// <c>Inventory.GetSlot</c> answers a negative index with null rather than an empty slot.
        /// Without it this throws on a hotbar nobody has touched yet, which is every hotbar for
        /// the first few seconds of a session.
        /// </summary>
        public InventoryItem GetSelectedItem()
        {
            InventorySlot slot = GetSelectedSlot();
            return slot == null || slot.IsEmpty ? null : slot.Item;
        }

        public int GetInventorySize() => inventorySize;
    }
}
