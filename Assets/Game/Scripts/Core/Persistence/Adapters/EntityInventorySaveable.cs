using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// A component beside an entity's bag that put something in it only for a while — a band's kit weapon lent to a
    /// resident while it musters and walks (ExpeditionMember). Those slots are saved empty, so a load never makes the loan
    /// the entity's own: nothing would ever take it back.
    /// </summary>
    public interface ILentSlots
    {
        /// <summary>Slot <paramref name="slot"/> holds a loan right now.</summary>
        bool IsLent(int slot);
    }

    /// <summary>What a bag with loans in it saves. Apart from the saver, so it can be tested without the save serializer.</summary>
    public static class LentSlots
    {
        /// <summary>
        /// For a bag of <paramref name="size"/> slots: slot i's item id (<paramref name="idAt"/>, null for empty), or null where
        /// <paramref name="lent"/> says the slot holds a loan. Positional, like the saver's state.
        /// </summary>
        public static List<string> SavedIds(int size, Func<int, string> idAt, Func<int, bool> lent)
        {
            var ids = new List<string>(size);
            for (int i = 0; i < size; i++) ids.Add(lent(i) ? null : idAt(i));
            return ids;
        }
    }

    /// <summary>
    /// Persists an NPC's inventory — what it is carrying, and therefore what it will drop when killed.
    ///
    /// <b>Why an agent's inventory is not the player's.</b> <see cref="PlayerInventorySaveable"/> works
    /// through <c>IPlayerInventory</c>, which an <see cref="EntityInventoryComponent"/> does not
    /// implement; the two hold the same underlying <c>Inventory</c> but expose different surfaces. A
    /// shared codec would have to invent a common interface for two things that legitimately differ —
    /// an NPC has no selected slot and no hotbar.
    ///
    /// <b>It matters more than it looks.</b> <c>EntityLootTable</c> drops the inventory's contents on
    /// death, so an inventory that resets to its prefab contents on every load turns a looted NPC back
    /// into a full one. And because restoring a slot raises <c>OnSlotChanged</c>,
    /// <c>EntityEquipmentController</c> re-equips a restored weapon on its own — the visible weapon in
    /// the NPC's hand comes back with no extra state.
    /// </summary>
    [RequireComponent(typeof(EntityInventoryComponent))]
    public class EntityInventorySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "entityInventory";

        private EntityInventoryComponent entityInventory;

        private EntityInventoryComponent Inventory =>
            entityInventory != null ? entityInventory : entityInventory = GetComponent<EntityInventoryComponent>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>
            /// Positional: entry i is slot i, and null means the slot was empty. Compacting would
            /// silently shift every item left of a gap — the same rule the player's hotbar follows.
            /// </summary>
            public List<string> itemIds;
        }

        public object CaptureState()
        {
            if (Inventory == null) return null;

            ILentSlots loans = GetComponent<ILentSlots>();
            return new State
            {
                itemIds = LentSlots.SavedIds(Inventory.Size, i =>
                {
                    InventorySlot slot = Inventory.GetSlot(i);
                    return slot == null || slot.IsEmpty ? null : slot.Item.ID;
                }, i => loans != null && loans.IsLent(i)),
            };
        }

        public void RestoreState(JObject state)
        {
            if (Inventory == null || state == null) return;

            List<string> ids = state.ToObject<State>(SaveSerializer.Serializer).itemIds;
            if (ids == null) return;

            for (int i = 0; i < ids.Count && i < Inventory.Size; i++)
            {
                string id = ids[i];

                if (string.IsNullOrEmpty(id))
                {
                    Inventory.RestoreSlot(i, null);
                    continue;
                }

                InventoryItem item = Registry<InventoryItem>.Get(id);

                if (item == null)
                {
                    // An item deleted from the build since the save. The slot is emptied rather than the
                    // whole inventory refused, and its position is kept so nothing to the right moves.
                    Debug.LogWarning($"[Save] Item '{id}' is not in the registry — a slot on '{name}' " +
                                     "was left empty. Was the item asset deleted?", this);
                }

                Inventory.RestoreSlot(i, item);
            }
        }
    }
}
