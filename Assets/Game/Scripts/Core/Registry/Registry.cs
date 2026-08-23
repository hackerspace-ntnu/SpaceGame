using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Core
{
    // `class` on the constraint so a bad entry can be reported against the asset that caused it:
    // Unity's logging overload takes an Object context, and neither `as` nor a null test is legal
    // on an unconstrained type parameter. Every registry entry is a ScriptableObject already, so
    // this narrows nothing that exists.
    public static class Registry<T> where T : class, IRegistryEntry
    {
        private static readonly Dictionary<string, T> entries = new();

        /// <summary>
        /// Add an entry under its own id.
        ///
        /// The guard is not defensive padding: indexing a dictionary by a null key throws
        /// <see cref="System.ArgumentNullException"/> from inside the framework, which names neither
        /// the registry nor the asset that caused it. An entry whose id failed to serialize is a
        /// wiring fault worth reporting by name — and worth surviving, because taking the whole
        /// registry down with it costs every other item too.
        /// </summary>
        public static void Register(T value)
        {
            if (value == null) return;

            if (string.IsNullOrEmpty(value.ID))
            {
                Debug.LogError(
                    $"[Registry] A {typeof(T).Name} has no ID and cannot be registered — everything " +
                    "keyed on it (hotbar replication, save records) will miss it. If this is an " +
                    "InventoryItem, its id serializes from [field: SerializeField]; re-import the " +
                    "asset so the value is written to disk.", value as Object);
                return;
            }

            entries[value.ID] = value;
        }

        public static T Get(string id)
        {
            return entries.TryGetValue(id, out var v) ? v : default;
        }

        /// <summary>
        /// Every registered entry. The save system derives its prefab table from the item registry,
        /// which needs to walk it rather than look one entry up by an ID it does not yet have.
        /// </summary>
        public static IEnumerable<T> All => entries.Values;
    }
}
