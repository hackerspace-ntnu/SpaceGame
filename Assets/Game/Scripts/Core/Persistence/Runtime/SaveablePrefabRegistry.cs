using System.Collections.Generic;
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

        public static int Count => Prefabs.Count;

        public static void Register(string prefabId, GameObject prefab)
        {
            if (string.IsNullOrEmpty(prefabId) || prefab == null) return;
            Prefabs[prefabId] = prefab;
        }

        public static bool TryGet(string prefabId, out GameObject prefab)
        {
            prefab = null;
            return !string.IsNullOrEmpty(prefabId) && Prefabs.TryGetValue(prefabId, out prefab) && prefab != null;
        }

        public static void Clear() => Prefabs.Clear();

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
    }
}
