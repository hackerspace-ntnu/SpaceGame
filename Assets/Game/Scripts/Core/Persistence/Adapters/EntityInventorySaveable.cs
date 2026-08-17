using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
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

            var ids = new List<string>(Inventory.Size);

            for (int i = 0; i < Inventory.Size; i++)
            {
                InventorySlot slot = Inventory.GetSlot(i);
                ids.Add(slot == null || slot.IsEmpty ? null : slot.Item.ID);
            }

            return new State { itemIds = ids };
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
