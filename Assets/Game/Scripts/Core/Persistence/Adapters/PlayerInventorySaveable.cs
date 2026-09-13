using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the player's hotbar: what is in each slot, which slot is selected, and what each of
    /// those items has become.
    ///
    /// Items are stored as registry IDs — the asset GUIDs <c>InventoryItem</c> already stamps into
    /// itself — never as names or indices into a list. A GUID survives renaming the asset, moving
    /// it between folders, and reordering the item table; the alternatives survive none of those.
    ///
    /// <para>
    /// <b>Per-slot state, and why it lives here.</b> Every held object is a fresh
    /// <c>Instantiate</c> of the item prefab, destroyed on unequip — so ammo, charges, cooldowns and
    /// a grapple's anchor had nowhere to live between two instances of the same item, and reset when
    /// the player scrolled one slot and back. <see cref="ItemState"/> gives an
    /// <see cref="InventorySlot"/> somewhere to keep them; this saver is what makes them survive the
    /// session too. The <c>itemIds</c> and <c>selectedSlot</c> fields are untouched, so a save
    /// written before any of this loads exactly as it did.
    /// </para>
    ///
    /// The component is only wiring. Everything that decides what a save contains lives in
    /// <see cref="InventorySaveCodec"/>, which takes an <see cref="IPlayerInventory"/> and no
    /// GameObject — MonoBehaviour Awake does not run outside play mode, so logic reachable only
    /// through a component is logic no EditMode test can reach.
    /// </summary>
    public class PlayerInventorySaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = InventorySaveCodec.Key;

        private IPlayerInventory inventory;
        private IBodyEquipment body;
        private EquipmentController equipment;

        private IPlayerInventory Inventory => inventory ??= GetComponent<IPlayerInventory>();
        private IBodyEquipment Body => body ??= GetComponent<IBodyEquipment>();

        private EquipmentController Equipment =>
            equipment != null ? equipment : equipment = GetComponent<EquipmentController>();

        public string SaveKey => Key;

        public object CaptureState()
        {
            if (Inventory == null) return null;

            // The one item in the player's HAND has been diverging from its slot ever since it was
            // equipped: the slot is only written when the item is put away. Refreshing it here is
            // what makes saving mid-fight store the magazine as it actually is, rather than as it
            // was when the weapon was last holstered.
            if (Equipment != null) Equipment.WriteBackHeldItemState();

            return InventorySaveCodec.Capture(Inventory);
        }

        public void RestoreState(JObject state)
        {
            if (Inventory == null) return;

            InventorySaveCodec.Restore(Inventory, state, Overflow, this);

            // Restoring the hotbar EQUIPS the selected item, as a side effect of assigning the
            // selection — and that happens inside the call above, before the per-slot bags have been
            // put back. So the one item that ends up in the player's hand is the one item restored
            // without its state, and this is the second pass that fixes it.
            if (Equipment != null) Equipment.ReapplyHeldItemState();
        }

        /// <summary>
        /// A save written when the hotbar was wider than it is now. The bar went from four slots to
        /// three when the body slots arrived, so the fourth item of every older save has nowhere to
        /// go — a gauntlet or a pack goes to its body slot, anything else is named in the console
        /// rather than dropped without a word.
        /// </summary>
        private void Overflow(InventoryItem item)
        {
            // Queued rather than placed: the body's own saver may restore after this one and would
            // overwrite a slot filled here. The body seats the queue once every saver has run.
            if (Body != null)
            {
                Body.QueueOverflow(item);
                return;
            }

            Debug.LogWarning($"[Save] '{item.itemName}' was in a hotbar slot this build no longer has and there is no body to wear it — it was not restored.", this);
        }

        /// <summary>
        /// Finish any restored item whose state names something else — a lassoed creature, a
        /// deployed craft, a grapple anchor that can move.
        ///
        /// <para>
        /// Runs once per player bind and again for every late chunk, and every implementor keeps its
        /// pending reference until the referent turns up. Only the held item is asked because only
        /// the held item exists: an item sitting in an unselected slot is an asset and a bag of
        /// strings, with nothing running that could hold a reference.
        /// </para>
        /// </summary>
        public void OnLoadComplete()
        {
            if (Equipment == null) return;

            if (Equipment.HeldUsable is IItemDeferredRestore pending && pending.HasPendingRestore)
                pending.TryCompleteRestore();
        }
    }

    /// <summary>The hotbar's save format and the rules for reading it back.</summary>
    public static class InventorySaveCodec
    {
        public const string Key = "inventory";

        public struct State
        {
            /// Positional: entry i is slot i, and null means the slot was empty. A compacted list
            /// would silently move every item left of a gap.
            public List<string> itemIds;

            public int selectedSlot;

            /// <summary>
            /// Positional like <see cref="itemIds"/>: entry i is what slot i's item had become, or
            /// null for an item at its authored defaults — which is most of them.
            ///
            /// A dictionary of strings rather than a typed struct per item, because the shape is the
            /// item's business rather than the hotbar's: a weapon writes ammo, the grapple writes a
            /// hook point, and neither should need this file to change. See <see cref="ItemState"/>.
            /// </summary>
            public List<Dictionary<string, string>> itemStates;
        }

        private static List<InventorySlot> Slots(IPlayerInventory inventory)
        {
            int size = inventory.GetInventorySize();
            var slots = new List<InventorySlot>(size);
            for (int i = 0; i < size; i++) slots.Add(inventory.GetSlot(i));
            return slots;
        }

        public static State Capture(IPlayerInventory inventory)
        {
            List<InventorySlot> slots = Slots(inventory);

            return new State
            {
                itemIds = GearSaveCodec.CaptureIds(slots),
                selectedSlot = inventory.SelectedSlotIndex,

                // Omitted entirely when nothing has state, which is the ordinary case — a list of
                // nulls in every player's record says nothing and costs a line each.
                itemStates = GearSaveCodec.CaptureStates(slots),
            };
        }

        /// <param name="overflow">Receives each saved item past the bar's current width, in order. Null drops them silently.</param>
        /// <param name="context">Optional, only for routing warnings to the right object in the console.</param>
        public static void Restore(IPlayerInventory inventory, JObject state, Action<InventoryItem> overflow = null,
                                   UnityEngine.Object context = null)
        {
            if (inventory == null || state == null) return;
            if (state["itemIds"] is not JArray ids) return;

            List<InventoryItem> items = GearSaveCodec.ReadItems(ids, context);

            int selected = state["selectedSlot"] is { Type: JTokenType.Integer } sel ? sel.Value<int>() : -1;

            inventory.RestoreSlots(items, selected);

            // After RestoreSlots, never before. Assigning a slot's Item clears its state — which is
            // exactly right, since a slot that changed hands must not keep the last item's ammo —
            // so bags written first would be wiped by the assignment that follows.
            GearSaveCodec.RestoreStates(Slots(inventory), state["itemStates"] as JArray);

            // The bags have just been written straight onto the server's own slots, behind the back
            // of the replication path that normally publishes a slot change. Anything in them that
            // another machine can SEE has to be pushed after the fact, and a tank's charge is the
            // first such thing — without this a client's restored tank shows its authored starting
            // charge until the next time that slot happens to change for another reason.
            inventory.PublishSlotCharges();

            // Entries past the bar's width would otherwise vanish inside RestoreSlots, which
            // documents that it drops them: the one silent loss this codec used to allow.
            int size = inventory.GetInventorySize();
            for (int i = size; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                overflow?.Invoke(items[i]);
            }
        }
    }
}
