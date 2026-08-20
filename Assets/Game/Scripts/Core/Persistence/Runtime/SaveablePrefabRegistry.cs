using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Resolves a saved <c>prefabId</c> back to the prefab that produced it.
    ///
    /// Populated from two sources, because the objects that need re-creating arrive by two routes:
    ///
    ///   • every <see cref="InventoryItem"/> in the item registry contributes its
    ///     <c>itemPrefab</c> under the item's own asset GUID. This is what makes dropped items
    ///     persist without a single item prefab being edited — <c>PlayerDropService</c> already
    ///     knows the item's ID at the moment it spawns the pickup.
    ///
    ///   • every prefab under a <c>Resources/Saveable</c> folder contributes under the
    ///     <c>prefabId</c> its <see cref="SaveableEntity"/> was stamped with. This is the route for
    ///     anything that is not an inventory item.
    ///
    /// Both keys are asset GUIDs from the same namespace, so they cannot collide.
    /// </summary>
    public static class SaveablePrefabRegistry
    {
        public const string ResourcesFolder = "Saveable";

        private static readonly Dictionary<string, GameObject> Prefabs = new();

        /// <summary>
        /// Whether the network prefab list has been folded in yet. Scanned once, lazily, on the first
        /// miss — see <see cref="TryGet"/> for why not at startup.
        /// </summary>
        private static bool networkPrefabsScanned;

        public static int Count => Prefabs.Count;

        public static void Register(string prefabId, GameObject prefab)
        {
            if (string.IsNullOrEmpty(prefabId) || prefab == null) return;
            Prefabs[prefabId] = prefab;
        }

        public static bool TryGet(string prefabId, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(prefabId)) return false;

            if (Prefabs.TryGetValue(prefabId, out prefab) && prefab != null) return true;

            // A miss is the moment to try the network prefab list, not startup. NetworkManager sets
            // its Singleton in its own Awake and nothing orders that against RegistryLoader's, so a
            // scan at load time would silently find nothing on whichever machine happened to wake up
            // in the other order.
            if (networkPrefabsScanned) return false;

            LoadNetworkPrefabs();
            return Prefabs.TryGetValue(prefabId, out prefab) && prefab != null;
        }

        public static void Clear()
        {
            Prefabs.Clear();
            networkPrefabsScanned = false;
        }

        /// <summary>
        /// Fills the registry. Called by <see cref="RegistryLoader"/> after the item registry is
        /// populated, since half the entries are derived from it.
        /// </summary>
        public static void LoadAll()
        {
            Prefabs.Clear();

            foreach (InventoryItem item in Registry<InventoryItem>.All)
            {
                if (item == null || item.itemPrefab == null || string.IsNullOrEmpty(item.ID)) continue;

                Register(item.ID, item.itemPrefab);

                // A stamped item prefab also answers to its own GUID, so an object saved before the
                // item alias existed — or one spawned by something that knows only the prefab —
                // still resolves.
                SaveableEntity stamped = item.itemPrefab.GetComponent<SaveableEntity>();
                if (stamped != null) Register(stamped.PrefabId, item.itemPrefab);
            }

            foreach (GameObject prefab in Resources.LoadAll<GameObject>(ResourcesFolder))
            {
                SaveableEntity entity = prefab.GetComponent<SaveableEntity>();
                if (entity == null)
                {
                    Debug.LogWarning($"[Save] '{prefab.name}' is in Resources/{ResourcesFolder} but has " +
                                     "no SaveableEntity, so it can never be restored. Add one or move it out.",
                                     prefab);
                    continue;
                }

                if (string.IsNullOrEmpty(entity.PrefabId))
                {
                    Debug.LogWarning($"[Save] '{prefab.name}' has a SaveableEntity with no prefab id. " +
                                     "Re-import it so OnValidate can stamp one.", prefab);
                    continue;
                }

                Register(entity.PrefabId, prefab);
            }

            Debug.Log($"[Save] Registered {Prefabs.Count} saveable prefabs.");
        }

        /// <summary>
        /// Adds every prefab Netcode already knows how to spawn.
        ///
        /// <b>The third route, and the one that closes the hole the other two left.</b> An object
        /// spawned at runtime that is neither an inventory item nor filed under
        /// <c>Resources/Saveable</c> was captured perfectly and could never be rebuilt — its record
        /// sat in the save file naming a prefab id nothing could resolve, and the load reported one
        /// warning and moved on. That is what happened to the ornithopter the wing pack deploys: fly
        /// it, quit, and the craft is in the save but the wings are gone.
        ///
        /// Keying off the network prefab list is not a shortcut, it is the same rule stated once. A
        /// world object that can be spawned during play must be a registered network prefab or it
        /// exists on the host alone — so "the server can spawn this" and "the save system can rebuild
        /// this" describe the same population, and reading one list serves both.
        ///
        /// Additive: it never displaces an entry from the item table or the Resources folder, both of
        /// which carry ids this cannot know about (an item's registry ID is not its prefab's GUID).
        /// </summary>
        public static void LoadNetworkPrefabs()
        {
            networkPrefabsScanned = true;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            var config = manager.NetworkConfig;
            if (config?.Prefabs == null) return;

            int added = 0;

            if (config.Prefabs.Prefabs != null)
            {
                foreach (var entry in config.Prefabs.Prefabs)
                    added += RegisterIfSaveable(entry?.Prefab);
            }

            if (config.Prefabs.NetworkPrefabsLists != null)
            {
                foreach (var list in config.Prefabs.NetworkPrefabsLists)
                {
                    if (list?.PrefabList == null) continue;

                    foreach (var entry in list.PrefabList)
                        added += RegisterIfSaveable(entry?.Prefab);
                }
            }

            if (added > 0)
                Debug.Log($"[Save] Registered {added} more saveable prefab(s) from the network prefab list.");
        }

        /// <summary>
        /// Registers a prefab under the id its own <see cref="SaveableEntity"/> carries, if it has one.
        ///
        /// Prefabs with no SaveableEntity are skipped in silence rather than warned about: the network
        /// list is full of things that have no business persisting — projectiles, effects, the player —
        /// and warning about each would bury the one message that matters.
        /// </summary>
        private static int RegisterIfSaveable(GameObject prefab)
        {
            if (prefab == null) return 0;

            var entity = prefab.GetComponent<SaveableEntity>();
            if (entity == null || string.IsNullOrEmpty(entity.PrefabId)) return 0;
            if (Prefabs.ContainsKey(entity.PrefabId)) return 0;

            Prefabs[entity.PrefabId] = prefab;
            return 1;
        }
    }
}
