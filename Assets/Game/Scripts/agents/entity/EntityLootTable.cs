// Defines what an entity drops on death and handles the actual drop.
// Drops items from EntityInventoryComponent and everything worn (guaranteed) plus random rolls from the loot table.
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
        [Tooltip("If true, the bag's contents (EntityInventoryComponent) and everything worn (EntityBodyEquipment) are also dropped.")]
        [SerializeField] private bool dropInventoryContents = true;

        [Header("Lying there")]
        [Tooltip("Seconds what this entity drops lies beside its body before the world takes it away -- " +
                 "and then only once no player is close enough to watch it go (Remains). Picking it " +
                 "up makes it the player's for good. 0 = it stays until somebody does.")]
        [SerializeField] private float lootLifetime = 180f;

        [Header("Killed aloft")]
        [Tooltip("A body killed seated in an IAirborneCarrier (the NPC craft) drops its loot once it is " +
                 "within this many metres of solid ground below it, not at the kill point.")]
        [SerializeField, Min(0.1f)] private float landedHeight = 1.5f;
        [Tooltip("How far below a body killed aloft to look for the ground it is falling to, metres. Must " +
                 "exceed the highest an NPC flies.")]
        [SerializeField, Min(1f)] private float groundSearchDepth = 600f;

        private HealthComponent health;
        private EntityInventoryComponent entityInventory;

        // Seated under an IAirborneCarrier: latched on the way in, and kept through a death, so the seat
        // letting the body go before this table hears OnDeath (handler order) cannot turn it into a ground death.
        private bool seatedAloft;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            entityInventory = GetComponent<EntityInventoryComponent>();
            seatedAloft = UnderAirborneCarrier();

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

        private void OnTransformParentChanged()
        {
            if (UnderAirborneCarrier()) seatedAloft = true;
            else if (!health || health.Alive) seatedAloft = false;
        }

        private bool UnderAirborneCarrier() =>
            transform.parent != null && transform.parent.GetComponentInParent<IAirborneCarrier>() != null;

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

            // Killed in the air (D8): the body falls, and its loot goes down with it rather than from the
            // kill point.
            if (seatedAloft)
            {
                LootAwaitingGround.Begin(gameObject, DropAll, landedHeight, groundSearchDepth);
                return;
            }

            DropAll();
        }

        /// The bag, what is worn and the rolls, beside the body.
        private void DropAll()
        {
            if (dropInventoryContents)
            {
                if (entityInventory != null) DropBag();
                DropWorn();
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

        /// <summary>
        /// What it was wearing, under the same guards and lifetime as the bag: a Sky nomad's wing pack is
        /// how a player gets to fly (D7). Taken off the body as it drops, so a corpse never holds a copy
        /// of what is lying beside it.
        /// </summary>
        private void DropWorn()
        {
            if (!TryGetComponent(out EntityBodyEquipment body)) return;
            foreach (InventoryItem item in body.TakeAllWorn()) DropOne(item);
        }

        private void DropOne(InventoryItem item)
        {
            GameObject dropped = GameServices.ItemDropService.DropItem(transform, item);
            if (dropped != null && lootLifetime > 0f) Remains.On(dropped).Begin(lootLifetime);
        }
    }
}
