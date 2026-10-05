// Defines what an entity drops on death and handles the actual drop.
// Drops items from EntityInventoryComponent (guaranteed) plus random rolls from the loot table.
// Every drop lies beside the body for `lootLifetime` and is then taken away like the body (Remains).
// Requires HealthComponent on the same GameObject.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [Serializable]
    public struct LootEntry
    {
        [Tooltip("Item to potentially drop.")]
        public InventoryItem item;
        [Tooltip("0 = never, 1 = always."), Range(0f, 1f)]
        public float dropChance;
        [Tooltip("How many to drop if the roll succeeds.")]
        public int quantity;
    }

    public class EntityLootTable : MonoBehaviour
    {
        [Header("Loot Rolls")]
        [SerializeField] private List<LootEntry> lootEntries;

        [Header("Drop inventory items on death")]
        [Tooltip("If true, all items currently in EntityInventoryComponent are also dropped.")]
        [SerializeField] private bool dropInventoryContents = true;

        [Header("Lying there")]
        [Tooltip("Seconds what this entity drops lies beside its body before the world takes it away -- " +
                 "and then only once no player is close enough to watch it go (Remains). Picking it " +
                 "up makes it the player's for good. 0 = it stays until somebody does.")]
        [SerializeField] private float lootLifetime = 180f;

        private HealthComponent health;
        private EntityInventoryComponent entityInventory;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            entityInventory = GetComponent<EntityInventoryComponent>();

            if (!health)
                Debug.LogWarning($"{name}: EntityLootTable needs a HealthComponent.", this);
        }

        private void OnEnable()
        {
            if (health)
                health.OnDeath += Drop;
        }

        private void OnDisable()
        {
            if (health)
                health.OnDeath -= Drop;
        }

        /// Roll the table and put the results on the floor beside the body.
        private void Drop()
        {
            // Only where this entity is simulated. OnDeath fires on every machine, not just the
            // server: a client's copy raises it the moment the replicated health crosses zero. Every
            // one of them rolling its own loot would give as many private, unreplicated piles as
            // there are players — and the dice would disagree. The server's drop replicates in.
            if (!Network.Simulates(this)) return;

            // A save being loaded, not a kill. The loot from this death was already dropped in the
            // session that caused it, and those pickups are in the save as runtime entities of their
            // own — so rolling again here does not restore the drop, it duplicates it. Reload five
            // times and the corpse pays out five times.
            if (health && health.IsRestoring) return;

            if (dropInventoryContents && entityInventory != null) DropBag();

            if (lootEntries == null)
                return;

            foreach (LootEntry entry in lootEntries)
            {
                if (!entry.item)
                    continue;

                for (int i = 0; i < entry.quantity; i++)
                {
                    if (UnityEngine.Random.value <= entry.dropChance)
                        DropOne(entry.item);
                }
            }
        }

        /// <summary>
        /// Everything in the bag, onto the floor -- and out of the bag. Left in, the body lies there
        /// for minutes still holding the gun that is also on the sand beside it; emptied, the slot
        /// change reaches every machine the way the hand's contents always do (NpcRandomLoadout), and
        /// the bag a save writes for this corpse is the empty one.
        /// </summary>
        private void DropBag()
        {
            for (int slot = 0; slot < entityInventory.Size; slot++)
            {
                InventorySlot contents = entityInventory.GetSlot(slot);
                if (contents == null || contents.IsEmpty) continue;

                DropOne(contents.Item);
                entityInventory.RestoreSlot(slot, null);
            }
        }

        private void DropOne(InventoryItem item)
        {
            GameObject dropped = GameServices.ItemDropService.DropItem(transform, item);
            if (dropped != null && lootLifetime > 0f) Remains.On(dropped).Begin(lootLifetime);
        }
    }
}
