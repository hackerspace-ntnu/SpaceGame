using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// How a list of gear slots is written and read back: positional item ids, positional state
    /// bags. Shared by the hotbar saver and the body saver so the two formats cannot drift — a
    /// save reader that understands one understands the other.
    ///
    /// <para>
    /// Items are stored by <b>registry ID = asset GUID</b>, never by name or index; an unknown id
    /// warns and leaves that slot empty, keeping every other slot where it was.
    /// </para>
    /// </summary>
    public static class GearSaveCodec
    {
        /// <summary>Slot ids in order; null for an empty slot.</summary>
        public static List<string> CaptureIds(IReadOnlyList<InventorySlot> slots)
        {
            var ids = new List<string>(slots.Count);

            foreach (InventorySlot slot in slots)
                ids.Add(slot == null || slot.IsEmpty ? null : slot.Item.ID);

            return ids;
        }

        /// <summary>
        /// Slot bags in order; null for an item at its defaults. Returns null altogether when no
        /// slot has state — the ordinary case, which should not put a list of nulls in every save.
        /// </summary>
        public static List<Dictionary<string, string>> CaptureStates(IReadOnlyList<InventorySlot> slots)
        {
            var states = new List<Dictionary<string, string>>(slots.Count);
            bool any = false;

            foreach (InventorySlot slot in slots)
            {
                ItemState state = slot?.State;

                if (state == null || state.IsEmpty)
                {
                    states.Add(null);
                    continue;
                }

                states.Add(state.Copy());
                any = true;
            }

            return any ? states : null;
        }

        /// <param name="context">Only for routing warnings to the right object in the console.</param>
        public static List<InventoryItem> ReadItems(JArray ids, Object context = null)
        {
            var items = new List<InventoryItem>(ids?.Count ?? 0);
            if (ids == null) return items;

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
                    // than the whole list refused, and the position is kept so everything to the
                    // right of it stays where the player left it.
                    Debug.LogWarning($"[Save] Item '{id}' is not in the registry — its slot was left empty. Was the item asset deleted?", context);
                }

                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Hand each slot back what its item had become. A payload with no states — every save
        /// written before per-slot state existed — leaves every slot at its defaults.
        /// Must run AFTER the items are assigned: assigning a slot's item clears its bag.
        /// </summary>
        public static void RestoreStates(IReadOnlyList<InventorySlot> slots, JArray states)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlot slot = slots[i];
                if (slot == null) continue;

                if (states == null || i >= states.Count || states[i] is not JObject bag)
                {
                    slot.State = null;
                    continue;
                }

                var raw = new Dictionary<string, string>();

                foreach (KeyValuePair<string, JToken> entry in bag)
                {
                    // Read defensively: a bag written by a newer build may hold a value shape this
                    // one has never seen, and one bad key must not cost the slot its other five.
                    if (entry.Value == null || entry.Value.Type == JTokenType.Null) continue;
                    raw[entry.Key] = entry.Value.ToString();
                }

                slot.State = raw.Count == 0 ? null : new ItemState(raw);
            }
        }
    }
}
