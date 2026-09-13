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

        [Header("When")]
        [Tooltip("Off: the drop lands the instant the entity dies. On: it is held back until " +
                 "HealthReactionModule despawns the body, so the pickup appears as the corpse " +
                 "goes rather than lying on the floor next to it. Needs a HealthReactionModule " +
                 "with a despawn delay; without one the drop would never happen, so this falls " +
                 "back to dropping on death and says so.")]
        [SerializeField] private bool dropOnDespawn;

        private HealthComponent health;
        private EntityInventoryComponent entityInventory;
        private HealthReactionModule reaction;

        /// The resolved answer to `dropOnDespawn`, which is the serialized WISH. It is switched
        /// off in Awake when there is nothing to wait for, because a loot table that quietly
        /// pays out nothing is worse than one that pays out early.
        private bool waitForDespawn;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            entityInventory = GetComponent<EntityInventoryComponent>();
            reaction = GetComponent<HealthReactionModule>();

            if (!health)
                Debug.LogWarning($"{name}: EntityLootTable needs a HealthComponent.", this);

            waitForDespawn = dropOnDespawn && reaction != null && reaction.Despawns;

            if (dropOnDespawn && !waitForDespawn)
                Debug.LogWarning(
                    $"{name}: EntityLootTable is set to hold its drop until the body despawns, " +
                    "but nothing here despawns it — there is no HealthReactionModule, or its " +
                    "despawn delay is zero. Dropping on death instead, because the alternative " +
                    "is a creature that can be killed and never pays out.", this);
        }

        private void OnEnable()
        {
            if (health)
                health.OnDeath += HandleDeath;
            if (waitForDespawn && reaction)
                reaction.Despawning += Drop;
        }

        private void OnDisable()
        {
            if (health)
                health.OnDeath -= HandleDeath;
            if (waitForDespawn && reaction)
                reaction.Despawning -= Drop;
        }

        private void HandleDeath()
        {
            // Not yet. The body is still there to be looked at, and Drop is on the despawn
            // instead -- which a restored death reaches in the same frame as this one, so the
            // guards below still get their say either way.
            if (waitForDespawn) return;

            Drop();
        }

        /// Roll the table and put the results on the floor. Either the death or the despawn
        /// calls this, never both.
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
            //
            // With `dropOnDespawn` there is one case this gets wrong in the safe direction. A world
            // saved while a corpse is still lying there has not paid out yet, and this refuses to
            // pay out on the load either, so that kill's loot is gone. Fixing it properly means
            // remembering per-corpse whether the table has been rolled, which is a saver this
            // component does not have; losing a drop is the better failure than minting one on
            // every reload for as long as the body exists.
            if (health && health.IsRestoring) return;

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
