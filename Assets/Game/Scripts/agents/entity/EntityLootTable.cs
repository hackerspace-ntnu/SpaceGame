// Defines what an entity drops on death and handles the actual drop.
// Drops items from EntityInventoryComponent (guaranteed) plus random rolls from the loot table.
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
                health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (health)
                health.OnDeath -= HandleDeath;
        }

        private void HandleDeath()
        {
            // Only where this entity is simulated. OnDeath fires on every machine, not just the
            // server: a client's copy raises it the moment the replicated health crosses zero. Every
            // one of them rolling its own loot would give as many private, unreplicated piles as
            // there are players — and the dice would disagree. The server's drop replicates in.
            if (!Network.Simulates(this)) return;

            Transform dropOrigin = transform;

            if (dropInventoryContents && entityInventory != null)
            {
                foreach (InventoryItem item in entityInventory.GetAllItems())
                    GameServices.ItemDropService.DropItem(dropOrigin, item);
            }

            if (lootEntries == null)
                return;

            foreach (LootEntry entry in lootEntries)
            {
                if (!entry.item)
                    continue;

                for (int i = 0; i < entry.quantity; i++)
                {
                    if (UnityEngine.Random.value <= entry.dropChance)
                        GameServices.ItemDropService.DropItem(dropOrigin, entry.item);
                }
            }
        }
    }
}
