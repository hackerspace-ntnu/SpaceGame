// Draws what an NPC carries but is not holding, on its belt and backpack.
//
// The hand shows ONE item: EntityEquipmentController owns that. Everything else in the entity's bag
// is carried, and a carried item that is nowhere on the body reads as an NPC with empty pockets and
// a hammer in its fist. This component is the other half: each bag slot that is not in the hand and
// whose prefab has a BeltMount is drawn hanging from one of the anchors the worn garments offer.
//
// Where an item hangs is planned over the whole bag, the hand's slot included (Plan), so drawing a tool
// leaves its place empty and putting it away finds it free. CanStow answers whether an item has such a
// place; ResidentHands asks before it takes a tool out of the hand.
//
// Putting a tool away or drawing it is instant as state and travels as a picture: the item's new instance starts where the old
// one was and is carried across over TransitSeconds (ToolTransit), so it is seen to go from the hand to its hook and back. The
// hand is empty (or full) from the first frame; only the picture takes its time.
//
// It is purely derived. Nothing here is saved or sent: the bag is EntityInventorySaveable's, which
// slot is in the hand is EntityEquipmentSaveable's, and every machine builds its own bag from the
// same startingItems, so every machine hangs the same things in the same places.
//
// The anchors are not this component's: each belt and backpack prefab carries its own mount points
// (GarmentMounts), tuned against its own geometry, so every Raxy wearing it hangs things in the
// same places. They become bone-parented anchors once at startup (BeltSeat.CreateAnchors). A wearer
// with no such garment has nowhere to hang anything, and says so.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(EntityInventoryComponent))]
    public class BeltCarrier : MonoBehaviour
    {
        private sealed class Hung
        {
            public InventoryItem item;
            public BeltSlot slot;
            public EquipItemSocket socket;
        }

        private Dictionary<BeltSlot, Transform> anchorBySlot;
        private readonly Dictionary<int, Hung> hungBySlotIndex = new Dictionary<int, Hung>();
        private readonly HashSet<int> wanted = new HashSet<int>();
        private readonly List<BeltMount> mounts = new List<BeltMount>();
        private Dictionary<int, BeltSlot> placement = new Dictionary<int, BeltSlot>();

        private readonly List<ToolTransit> transits = new List<ToolTransit>();

        private EntityInventoryComponent inventory;
        private EntityEquipmentController equipment;
        private int lastHandSlot = int.MinValue;
        private bool dirty = true;

        // Where the held item sat in the hand when last seen, for a stow to start from after the hand's instance is gone.
        private ToolTransit.Anchored heldPose;
        private int heldPoseSlot = -1;

        /// <summary>
        /// Seconds a tool takes to travel between the hand and its hook when drawn or stowed; 0 = it appears where it goes. Set by
        /// whatever owns the hand (a resident's tuning): nothing about it is saved or sent, every machine plays the same short move.
        /// </summary>
        public float TransitSeconds { get; set; }

        private void Awake()
        {
            inventory = GetComponent<EntityInventoryComponent>();
            equipment = GetComponent<EntityEquipmentController>();

            Animator animator = GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError($"{name}: BeltCarrier needs a humanoid avatar.", this);
                enabled = false;
                return;
            }

            anchorBySlot = BeltSeat.CreateAnchors(transform, animator);
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
            int leftHand = handSlot != lastHandSlot ? lastHandSlot : -1;
            if (handSlot != lastHandSlot)
            {
                lastHandSlot = handSlot;
                dirty = true;
            }

            if (dirty)
            {
                dirty = false;
                Refresh(handSlot, leftHand);
            }

            Tick(Time.deltaTime);
            RememberHeld(handSlot);
        }

        /// <summary>Advances the tools in transit. Part of the late update; public so an edit-mode check can step it.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = transits.Count - 1; i >= 0; i--)
                if (!transits[i].Tick(deltaTime)) transits.RemoveAt(i);
        }

        private void RememberHeld(int handSlot)
        {
            GameObject held = equipment != null ? equipment.HeldObject : null;
            Transform hand = equipment != null ? equipment.HandBone : null;
            if (held == null || hand == null)
            {
                heldPoseSlot = -1;
                return;
            }

            heldPose = ToolTransit.Anchored.Of(hand, held.transform);
            heldPoseSlot = handSlot;
        }

        private void OnSlotChanged(int index, InventorySlot slot) => dirty = true;

        // leftHand is the slot that was in the hand until this frame (-1: none), so a stow starts from the hand and a draw from the hook.
        private void Refresh(int handSlot, int leftHand)
        {
            Plan();
            wanted.Clear();
            foreach (int index in placement.Keys)
                if (index != handSlot) wanted.Add(index);

            if (anchorBySlot.Count == 0) WarnNothingToHangOn(handSlot);

            var gone = new List<int>();
            foreach (KeyValuePair<int, Hung> entry in hungBySlotIndex)
            {
                InventorySlot slot = wanted.Contains(entry.Key) ? inventory.GetSlot(entry.Key) : null;
                if (slot == null || slot.Item != entry.Value.item || placement[entry.Key] != entry.Value.slot) gone.Add(entry.Key);
            }

            ToolTransit.Anchored? drawnFrom = null;
            foreach (int index in gone)
            {
                Hung hung = hungBySlotIndex[index];
                if (index == handSlot && hung.socket.Current != null)
                    drawnFrom = ToolTransit.Anchored.Of(anchorBySlot[hung.slot], hung.socket.Current.transform);
                hung.socket.Unequip();
                hungBySlotIndex.Remove(index);
            }

            foreach (int index in wanted)
            {
                if (hungBySlotIndex.ContainsKey(index)) continue;
                Hang(index, inventory.GetSlot(index).Item, placement[index]);
                if (index == leftHand && index == heldPoseSlot && hungBySlotIndex.TryGetValue(index, out Hung stowed) && stowed.socket.Current != null)
                    Travel(stowed.socket.Current.transform, heldPose);
            }

            GameObject drawn = equipment != null ? equipment.HeldObject : null;
            if (drawnFrom.HasValue && drawn != null) Travel(drawn.transform, drawnFrom.Value);
        }

        private void Travel(Transform visual, ToolTransit.Anchored from)
        {
            if (TransitSeconds > 0f) transits.Add(new ToolTransit(visual, from, TransitSeconds));
        }

        /// <summary>
        /// Whether the item in bag slot <paramref name="slotIndex"/> has an anchor of its own, so that taking it out of the
        /// hand puts it on the body. False (with the reason) for an item with no <see cref="BeltMount"/> and for one the
        /// worn garments have no room for: either would leave it hanging nowhere, which reads as lost.
        /// </summary>
        public bool CanStow(int slotIndex, out string refusal)
        {
            refusal = null;
            if (anchorBySlot == null)
            {
                refusal = "it has no humanoid avatar to wear a belt on";
                return false;
            }

            InventorySlot slot = inventory.GetSlot(slotIndex);
            if (slot == null || slot.IsEmpty || !HasMount(slot.Item))
            {
                refusal = "it has no BeltMount (HandToolRoster: CarryOnly)";
                return false;
            }

            Plan();
            if (placement.ContainsKey(slotIndex)) return true;

            refusal = "every belt or pack anchor it may hang from is missing or taken by an earlier item";
            return false;
        }

        /// <summary>
        /// The anchor every mountable bag slot hangs on, in slot order, the hand's included. Where an item hangs does not
        /// depend on what is in the hand, so drawing the tool leaves its place on the belt empty and putting it back
        /// finds it free, and the belt never reshuffles around a draw. Items past the anchors the garments offer get none.
        /// </summary>
        private void Plan()
        {
            mounts.Clear();
            for (int i = 0; i < inventory.Size; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                mounts.Add(slot == null || slot.IsEmpty ? null : MountOf(slot.Item));
            }

            placement = BeltSeat.Plan(mounts, anchorBySlot.Keys);
        }

        private void WarnNothingToHangOn(int handSlot)
        {
            for (int i = 0; i < inventory.Size; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (i != handSlot && slot != null && !slot.IsEmpty && HasMount(slot.Item))
                    Debug.LogWarning($"{name}: carries '{slot.Item.name}' but wears no belt or backpack with mount points.", this);
            }
        }

        private static BeltMount MountOf(InventoryItem item) =>
            item != null && item.itemPrefab != null ? item.itemPrefab.GetComponent<BeltMount>() : null;

        private static bool HasMount(InventoryItem item) => MountOf(item) != null;

        private void Hang(int slotIndex, InventoryItem item, BeltSlot slot)
        {
            if (BeltSeat.Hang(anchorBySlot[slot], item.itemPrefab, out EquipItemSocket socket) == null) return;

            hungBySlotIndex[slotIndex] = new Hung { item = item, slot = slot, socket = socket };
        }
    }
}
