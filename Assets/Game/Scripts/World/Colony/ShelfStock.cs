using System;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.World
{
    /// <summary>
    /// Puts item pickups on a shelf once per world: the weapon rack's guns, a spare oxygen tank on a workbench.
    ///
    /// <para>
    /// <b>Spawned, never nested.</b> A pickup carries its own <c>NetworkObject</c>, and a building is scenery that a
    /// settlement wraps in a <c>NetworkObject</c> of its own; a pickup baked into the building prefab would nest one
    /// inside the other and inherit the prefab's hash in every scene that places it. So the server spawns each item
    /// through <see cref="IWorldService"/> the first time the building is simulated, and from then on the pickups are
    /// ordinary world items, saved and replicated like any dropped one.
    /// </para>
    /// <para>
    /// <see cref="Stocked"/> is saved (<c>ShelfStockSaveable</c>), so a reload does not lay the shelf a second time
    /// beside what is already lying there or what the players already took.
    /// </para>
    /// </summary>
    public sealed class ShelfStock : MonoBehaviour, IPersistentEntity
    {
        [Serializable]
        private struct Slot
        {
            public InventoryItem item;
            [Tooltip("Where the item is put, relative to this transform.")]
            public Vector3 localPosition;
            [Tooltip("Yaw of the item about the shelf's up, in degrees.")]
            public float yaw;
        }

        [SerializeField] private Slot[] slots = Array.Empty<Slot>();

        /// <summary>Whether this shelf has already been laid in this world.</summary>
        public bool Stocked { get; private set; }

        /// <summary>Restore-only: called by the save system before the first <c>Start</c>.</summary>
        public void RestoreStocked(bool stocked) => Stocked = stocked;

        private void Start()
        {
            if (Stocked || !Network.Simulates(this) || GameServices.World == null) return;

            Stocked = true;
            foreach (Slot slot in slots)
            {
                if (slot.item == null || slot.item.itemPrefab == null)
                {
                    Debug.LogWarning($"[ShelfStock] {name}: a slot has no item prefab and stays empty.", this);
                    continue;
                }
                Vector3 at = transform.TransformPoint(slot.localPosition);
                Quaternion turn = transform.rotation * Quaternion.Euler(0f, slot.yaw, 0f);
                GameServices.World.Spawn(slot.item.itemPrefab, at, turn);
            }
        }
    }
}
