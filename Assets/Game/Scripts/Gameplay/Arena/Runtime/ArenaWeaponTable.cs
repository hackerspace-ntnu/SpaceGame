using System;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// The weapons an arena's pads hand out, and how often each one turns up. Which artifacts are
    /// in the pool is a balance decision per arena, so it is data rather than a list in code.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Arena/Weapon Table", fileName = "ArenaWeapons")]
    public class ArenaWeaponTable : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            [Tooltip("The item a pad puts down. Its itemPrefab must be a registered network prefab — " +
                     "every InventoryItem's already is.")]
            public InventoryItem item;

            [Tooltip("Relative chance against the other entries. 0 takes it out of the pool.")]
            [Min(0f)] public float weight;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        /// <summary>A weighted pick, or null when the table has nothing with a prefab and a weight.</summary>
        public InventoryItem Pick()
        {
            float total = 0f;
            foreach (Entry entry in entries)
                if (IsUsable(entry)) total += entry.weight;

            if (total <= 0f) return null;

            float roll = UnityEngine.Random.value * total;
            InventoryItem last = null;

            foreach (Entry entry in entries)
            {
                if (!IsUsable(entry)) continue;

                last = entry.item;
                roll -= entry.weight;
                if (roll <= 0f) return entry.item;
            }

            // Float rounding can leave a sliver past the last bucket; it belongs to the last usable entry.
            return last;
        }

        private static bool IsUsable(in Entry entry) =>
            entry.item != null && entry.item.itemPrefab != null && entry.weight > 0f;
    }
}
