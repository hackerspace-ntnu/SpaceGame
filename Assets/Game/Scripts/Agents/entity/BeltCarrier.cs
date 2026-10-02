// Draws what an NPC carries but is not holding, on its belt.
//
// The hand shows ONE item: EntityEquipmentController owns that. Everything else in the entity's bag
// is carried, and a carried item that is nowhere on the body reads as an NPC with empty pockets and
// a hammer in its fist. This component is the other half: each bag slot that is not in the hand and
// whose prefab has a BeltMount is drawn hanging from one of three belt anchors.
//
// It is purely derived. Nothing here is saved or sent: the bag is EntityInventorySaveable's, which
// slot is in the hand is EntityEquipmentSaveable's, and every machine builds its own bag from the
// same startingItems, so every machine hangs the same things in the same places.
//
// The anchors are bound to the HIPS bone but authored in character space (the NPC root's frame:
// +Y up, +Z forward), and converted once at startup. A rig's bone axes are whatever its last export
// left them, and the same belt has to sit right on every skeleton -- see HandGripFrame, which exists
// for the same reason.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(EntityInventoryComponent))]
    public class BeltCarrier : MonoBehaviour
    {
        [Tooltip("The belt's anchors. A slot with no entry here is never used.")]
        [SerializeField] private BeltAnchorPose[] anchors = BeltSeat.DefaultAnchors();

        private sealed class Hung
        {
            public InventoryItem item;
            public BeltSlot slot;
            public EquipItemSocket socket;
        }

        private readonly Dictionary<BeltSlot, Transform> anchorBySlot = new Dictionary<BeltSlot, Transform>();
        private readonly Dictionary<int, Hung> hungBySlotIndex = new Dictionary<int, Hung>();
        private readonly HashSet<int> wanted = new HashSet<int>();

        private EntityInventoryComponent inventory;
        private EntityEquipmentController equipment;
        private int lastHandSlot = int.MinValue;
        private bool dirty = true;

        private void Awake()
        {
            inventory = GetComponent<EntityInventoryComponent>();
            equipment = GetComponent<EntityEquipmentController>();

            Animator animator = GetComponentInChildren<Animator>(true);
            Transform hips = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips)
                : null;

            if (hips == null)
            {
                Debug.LogError($"{name}: BeltCarrier needs a humanoid avatar with a Hips bone.", this);
                enabled = false;
                return;
            }

            foreach (BeltAnchorPose pose in anchors)
                anchorBySlot[pose.slot] = BeltSeat.CreateAnchor(transform, hips, pose);
        }

        private void OnEnable() => inventory.OnSlotChanged += OnSlotChanged;

        private void OnDisable() => inventory.OnSlotChanged -= OnSlotChanged;

        private void OnDestroy()
        {
            foreach (Hung hung in hungBySlotIndex.Values)
                hung.socket.Unequip();
            hungBySlotIndex.Clear();
        }

        // After the Animator has posed the hips: an anchor is a child of the hips bone, and the item
        // seated on it is placed from the anchor's pose.
        private void LateUpdate()
        {
            int handSlot = equipment != null ? equipment.EquippedSlotIndex : -1;
            if (handSlot != lastHandSlot)
            {
                lastHandSlot = handSlot;
                dirty = true;
            }

            if (!dirty) return;
            dirty = false;
            Refresh(handSlot);
        }

        private void OnSlotChanged(int index, InventorySlot slot) => dirty = true;

        private void Refresh(int handSlot)
        {
            wanted.Clear();
            for (int i = 0; i < inventory.Size; i++)
            {
                if (i == handSlot) continue;

                InventorySlot slot = inventory.GetSlot(i);
                if (slot == null || slot.IsEmpty || !HasMount(slot.Item)) continue;

                wanted.Add(i);
            }

            var gone = new List<int>();
            foreach (KeyValuePair<int, Hung> entry in hungBySlotIndex)
            {
                InventorySlot slot = wanted.Contains(entry.Key) ? inventory.GetSlot(entry.Key) : null;
                if (slot == null || slot.Item != entry.Value.item) gone.Add(entry.Key);
            }

            foreach (int index in gone)
            {
                hungBySlotIndex[index].socket.Unequip();
                hungBySlotIndex.Remove(index);
            }

            foreach (int index in wanted)
            {
                if (hungBySlotIndex.ContainsKey(index)) continue;
                Hang(index, inventory.GetSlot(index).Item);
            }
        }

        private static bool HasMount(InventoryItem item) =>
            item != null && item.itemPrefab != null && item.itemPrefab.GetComponent<BeltMount>() != null;

        private void Hang(int slotIndex, InventoryItem item)
        {
            BeltMount prefabMount = item.itemPrefab.GetComponent<BeltMount>();
            if (!TryTakeAnchor(prefabMount.Preferred, out BeltSlot slot)) return;

            if (BeltSeat.Hang(anchorBySlot[slot], item.itemPrefab, out EquipItemSocket socket) == null) return;

            hungBySlotIndex[slotIndex] = new Hung { item = item, slot = slot, socket = socket };
        }

        /// <summary>The preferred slot if it is free, otherwise the first free one in enum order.</summary>
        private bool TryTakeAnchor(BeltSlot preferred, out BeltSlot taken)
        {
            if (IsFree(preferred))
            {
                taken = preferred;
                return true;
            }

            foreach (BeltSlot candidate in Enum.GetValues(typeof(BeltSlot)))
            {
                if (!IsFree(candidate)) continue;
                taken = candidate;
                return true;
            }

            taken = preferred;
            return false;
        }

        private bool IsFree(BeltSlot slot)
        {
            if (!anchorBySlot.ContainsKey(slot)) return false;
            foreach (Hung hung in hungBySlotIndex.Values)
                if (hung.slot == slot) return false;
            return true;
        }
    }
}
