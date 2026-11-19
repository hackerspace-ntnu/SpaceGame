using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// A spot in the arena that always has a weapon on it, give or take a wait: put one down, and
    /// once somebody takes it, put down another after <see cref="respawnSeconds"/>.
    ///
    /// <para>
    /// Server only. The item it spawns is an ordinary world item — a registered network prefab
    /// spawned through <c>GameServices.World</c> — so every peer sees it, and picking it up is the
    /// ordinary pickup. The pad only has to notice that its item has been despawned.
    /// </para>
    ///
    /// <para>
    /// Driven by <see cref="ArenaMatch"/>: <see cref="Arm"/> when a round goes live,
    /// <see cref="Disarm"/> when it is reset. Between rounds the match sweeps every loose item off
    /// the floor itself, the pad's included.
    /// </para>
    /// </summary>
    public class ArenaWeaponPad : MonoBehaviour
    {
        [SerializeField] private ArenaWeaponTable table;

        [Tooltip("Seconds between a weapon being taken and the next one appearing.")]
        [SerializeField, Min(0f)] private float respawnSeconds = 20f;

        [Tooltip("How high above the pad the weapon is put down. It has a rigidbody and settles " +
                 "onto whatever is below.")]
        [SerializeField, Min(0f)] private float dropHeight = 0.6f;

        private NetworkObject current;
        private bool armed;
        private float nextSpawnTime;

        /// <summary>Starts handing out weapons, beginning with one straight away.</summary>
        public void Arm()
        {
            armed = true;
            current = null;
            nextSpawnTime = Time.time;
        }

        /// <summary>Stops handing out weapons and forgets the one it put down — the round reset sweeps it.</summary>
        public void Disarm()
        {
            armed = false;
            current = null;
        }

        private void Update()
        {
            if (!armed || !Network.Decides) return;

            if (current != null && current.IsSpawned) return;

            // Taken since the last frame: start the wait from now.
            if (current != null)
            {
                current = null;
                nextSpawnTime = Time.time + respawnSeconds;
            }

            if (Time.time < nextSpawnTime) return;

            SpawnWeapon();
        }

        private void SpawnWeapon()
        {
            InventoryItem item = table != null ? table.Pick() : null;
            if (item == null)
            {
                Debug.LogWarning($"[Arena] Pad '{name}' has no weapon table, or one with nothing in " +
                                 "it, so it stays empty.", this);
                armed = false;
                return;
            }

            GameObject spawned = GameServices.World.Spawn(item.itemPrefab,
                transform.position + Vector3.up * dropHeight, transform.rotation);

            current = spawned != null ? spawned.GetComponent<NetworkObject>() : null;

            // Something that could not be spawned is retried after the usual wait rather than
            // every frame, so a broken prefab is one warning per wait instead of a flood.
            if (current == null) nextSpawnTime = Time.time + respawnSeconds;
        }
    }
}
