
using System.Collections.Generic;

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
}

