using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the player's hotbar: what is in each slot, and which slot is selected.
    ///
    /// Items are stored as registry IDs — the asset GUIDs <c>InventoryItem</c> already stamps into
    /// itself — never as names or indices into a list. A GUID survives renaming the asset, moving
    /// it between folders, and reordering the item table; the alternatives survive none of those.
    ///
    /// The component is only wiring. Everything that decides what a save contains lives in
    /// <see cref="InventorySaveCodec"/>, which takes an <see cref="IPlayerInventory"/> and no
    /// GameObject — MonoBehaviour Awake does not run outside play mode, so logic reachable only
    /// through a component is logic no EditMode test can reach.
    /// </summary>
    public class PlayerInventorySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = InventorySaveCodec.Key;

        private IPlayerInventory inventory;

        private IPlayerInventory Inventory => inventory ??= GetComponent<IPlayerInventory>();

        public string SaveKey => Key;

        public object CaptureState() => Inventory == null ? null : InventorySaveCodec.Capture(Inventory);

        public void RestoreState(JObject state)
        {
            if (Inventory == null) return;
            InventorySaveCodec.Restore(Inventory, state, this);
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
        }

        public static State Capture(IPlayerInventory inventory)
        {
            int size = inventory.GetInventorySize();
            var ids = new List<string>(size);

            for (int i = 0; i < size; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                ids.Add(slot == null || slot.IsEmpty ? null : slot.Item.ID);
            }

            return new State { itemIds = ids, selectedSlot = inventory.SelectedSlotIndex };
        }

        /// <param name="context">Optional, only for routing warnings to the right object in the console.</param>
        public static void Restore(IPlayerInventory inventory, JObject state, Object context = null)
        {
            if (inventory == null || state == null) return;
            if (state["itemIds"] is not JArray ids) return;

            var items = new List<InventoryItem>(ids.Count);

            foreach (JToken token in ids)
            {
                string id = token?.Type == JTokenType.String ? token.Value<string>() : null;

                if (string.IsNullOrEmpty(id))
                {
                    items.Add(null);
                    continue;
                }

                InventoryItem item = Registry<InventoryItem>.Get(id);

                if (item == null)
                {
                    // An item that no longer exists in this build. The slot is left empty rather
                    // than the whole hotbar refused, and the position is kept so everything to the
                    // right of it stays where the player left it.
                    Debug.LogWarning($"[Save] Item '{id}' is not in the registry — its hotbar slot " +
                                     "was left empty. Was the item asset deleted?", context);
                }

                items.Add(item);
            }

            int selected = state["selectedSlot"] is { Type: JTokenType.Integer } sel ? sel.Value<int>() : -1;

            inventory.RestoreSlots(items, selected);
        }
    }
}
