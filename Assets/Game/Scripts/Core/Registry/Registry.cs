using System.Collections.Generic;

namespace SpaceGame.Core
{
    public static class Registry<T> where T : IRegistryEntry
    {
        private static readonly Dictionary<string, T> entries = new();

        public static void Register(T value)
        {
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
