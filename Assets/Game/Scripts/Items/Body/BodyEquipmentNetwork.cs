using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The three worn slots, as server state. The same shape as <see cref="PlayerInventoryNetwork"/>
    /// — a <see cref="NetworkList{T}"/> of item ids the server owns, a local <see cref="Inventory"/>
    /// mirror on every machine so each slot carries an <see cref="ItemState"/> bag, and events
    /// derived from replication so every machine wears the same things without a message saying so.
    ///
    /// <para>
    /// <b>Moves.</b> One owner-permission request names two slots in either list. The server runs
    /// <see cref="GearMoves"/>, then writes both lists — the hotbar half through
    /// <see cref="IPlayerInventory.TrySetSlot"/> — and the answer arrives as slot-change events on
    /// every machine. Nothing moves locally, which is how the gear screen cannot disagree with the
    /// server about where anything is.
    /// </para>
    /// <para>
    /// <b>Per-instance state does not travel with a move.</b> A NetworkList write arrives per index,
    /// and assigning a slot's item clears its bag on every machine. The backpack already lives with
    /// the same rule. The one state that would matter — a deployed wing pack — cannot happen,
    /// because nothing moves while the player is mounted.
    /// </para>
    /// </summary>
    public class BodyEquipmentNetwork : NetworkBehaviour, IBodyEquipment
    {
        [Tooltip("What this player wears from the start, by body slot: Back, LeftGauntlet, RightGauntlet. " +
                 "An entry of the wrong kind for its slot is skipped with a warning.")]
        [SerializeField] private List<InventoryItem> startingBody;

        private Inventory mirror;
        private IPlayerInventory hotbar;

        private readonly NetworkList<FixedString64Bytes> networkItems = new();

        public event Action<BodySlot, InventorySlot> OnBodySlotChanged;

        private void Awake()
        {
            // Built here rather than in OnNetworkSpawn so a body that is asked before it spawns —
            // the controller's Start, a saver's capture — answers with empty slots, not a null.
            mirror = new Inventory(GearRef.BodySlotCount);
            hotbar = GetComponent<IPlayerInventory>();
        }

        public override void OnNetworkSpawn()
        {
            networkItems.OnListChanged += HandleListChanged;

            if (IsServer)
            {
                InitializeNetworkState();
                return;
            }

            AdoptCurrentState();
        }

        public override void OnDestroy()
        {
            networkItems.OnListChanged -= HandleListChanged;

            // NetworkBehaviour.OnDestroy disposes this behaviour's NetworkVariables — including the
            // list, a native container — and deregisters it. Without this the allocation leaks on
            // every despawn.
            base.OnDestroy();
        }

        /// <summary>
        /// Take up the slots as they already stand, for a machine that was not here when they were
        /// set. OnListChanged only fires on CHANGE; a late joiner arrives with the values already in
        /// the list and no events coming.
        /// </summary>
        private void AdoptCurrentState()
        {
            for (int i = 0; i < networkItems.Count && i < mirror.GetSize(); i++)
                Apply(i, networkItems[i]);
        }

        private void InitializeNetworkState()
        {
            networkItems.Clear();
            for (int i = 0; i < GearRef.BodySlotCount; i++)
                networkItems.Add(default);

            if (startingBody == null) return;

            for (int i = 0; i < startingBody.Count && i < GearRef.BodySlotCount; i++)
            {
                InventoryItem item = startingBody[i];
                if (item == null || string.IsNullOrEmpty(item.ID)) continue;

                if (!BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                {
                    Debug.LogWarning($"[Body] Starting item '{item.itemName}' is a {item.equipKind} and does not fit the {(BodySlot)i} slot — skipped.", this);
                    continue;
                }

                networkItems[i] = new FixedString64Bytes(item.ID);
            }
        }

        private void HandleListChanged(NetworkListEvent<FixedString64Bytes> change)
        {
            int index = change.Index;
            if (index < 0 || index >= networkItems.Count || index >= mirror.GetSize()) return;

            Apply(index, networkItems[index]);
        }

        private void Apply(int index, FixedString64Bytes id)
        {
            InventoryItem item = string.IsNullOrEmpty(id.Value) ? null : Registry<InventoryItem>.Get(id.Value);
            mirror.SetItem(index, item);
            OnBodySlotChanged?.Invoke((BodySlot)index, mirror.GetSlot(index));
        }

        // ── IBodyEquipment ─────────────────────────────────────────────────────

        public InventorySlot GetSlot(BodySlot slot) => mirror.GetSlot((int)slot);

        /// <summary>
        /// A mounted rider is parented into the seat, so the mount is an ancestor. Asked of the
        /// hierarchy rather than kept as a flag, because every mount, dismount, save-restore and
        /// late-joiner path already keeps the hierarchy right and none of them would remember a
        /// flag.
        /// </summary>
        public bool IsMounted => GetComponentInParent<MountModule>() != null;

        public void RequestMove(GearRef from, GearRef to)
        {
            if (!IsOwner) return;
            MoveServerRpc((int)from.Area, from.Index, (int)to.Area, to.Index);
        }

        // Owner, not Everyone. A body belongs to the player wearing it: the default permission would
        // let any client in the session strip anyone else's gauntlets.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MoveServerRpc(int fromArea, int fromIndex, int toArea, int toIndex)
        {
            ServerMove(new GearRef((GearArea)fromArea, fromIndex), new GearRef((GearArea)toArea, toIndex));
        }

        /// <summary>The server's half of a move. Public so an offline session and a test can drive it directly.</summary>
        public void ServerMove(GearRef from, GearRef to)
        {
            if (!Network.Simulates(this)) return;

            InventoryItem fromItem = ItemAt(from);
            InventoryItem toItem = ItemAt(to);

            MoveResult result = GearMoves.Resolve(from, fromItem != null ? fromItem.equipKind : null,
                                                  to, toItem != null ? toItem.equipKind : null,
                                                  IsMounted);

            if (!result.Allowed)
            {
                Debug.Log($"[Body] Move {from} → {to} refused: {result.Reason}.", this);
                return;
            }

            // Both halves are written regardless of swap-or-move: a move writes null into the
            // source, which is the same operation with a different value.
            Write(to, fromItem);
            Write(from, toItem);
        }

        private InventoryItem ItemAt(GearRef slot)
        {
            if (slot.IsNone) return null;

            InventorySlot s = slot.IsBody ? mirror.GetSlot(slot.Index) : hotbar?.GetSlot(slot.Index);
            return s == null || s.IsEmpty ? null : s.Item;
        }

        private void Write(GearRef slot, InventoryItem item)
        {
            if (slot.IsBody)
            {
                if (slot.Index >= networkItems.Count) return;
                networkItems[slot.Index] = Id(item);
                return;
            }

            if (hotbar == null || !hotbar.TrySetSlot(slot.Index, item))
                Debug.LogWarning($"[Body] Could not write {slot} on the hotbar.", this);
        }

        private static FixedString64Bytes Id(InventoryItem item) =>
            item != null && !string.IsNullOrEmpty(item.ID)
                ? new FixedString64Bytes(item.ID)
                : default(FixedString64Bytes);

        public void RestoreSlots(IReadOnlyList<InventoryItem> items)
        {
            if (!Network.Simulates(this))
            {
                Debug.LogWarning("[Save] Body RestoreSlots ignored on a client — the body is server state.", this);
                return;
            }

            if (networkItems.Count < GearRef.BodySlotCount) InitializeNetworkState();

            for (int i = 0; i < networkItems.Count; i++)
            {
                InventoryItem item = items != null && i < items.Count ? items[i] : null;

                if (item != null && !BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                {
                    Debug.LogWarning($"[Save] '{item.itemName}' does not fit the {(BodySlot)i} slot it was saved in — left empty.", this);
                    item = null;
                }

                networkItems[i] = Id(item);
            }
        }

        private readonly List<InventoryItem> overflow = new();

        public void QueueOverflow(InventoryItem item)
        {
            if (item != null) overflow.Add(item);
        }

        public List<InventoryItem> DrainOverflow()
        {
            var unplaced = new List<InventoryItem>();

            foreach (InventoryItem item in overflow)
                if (!TryPlaceInBody(item)) unplaced.Add(item);

            overflow.Clear();
            return unplaced;
        }

        public bool TryPlaceInBody(InventoryItem item)
        {
            if (!Network.Simulates(this) || item == null) return false;
            if (networkItems.Count < GearRef.BodySlotCount) InitializeNetworkState();

            for (int i = 0; i < networkItems.Count; i++)
            {
                if (!BodySlotRules.Accepts((BodySlot)i, item.equipKind)) continue;
                if (!string.IsNullOrEmpty(networkItems[i].Value)) continue;

                networkItems[i] = Id(item);
                return true;
            }

            return false;
        }
    }
}
