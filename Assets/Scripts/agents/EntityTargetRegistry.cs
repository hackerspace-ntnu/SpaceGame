// Lightweight tag-based target registry that replaces GameObject.FindGameObjectWithTag in modules.
// Entities register/unregister themselves by tag. Modules resolve targets from here — O(1) lookup,
// survives respawn, and never returns Unity fake-null dead references.
//
// Usage — on the Player (or any targetable):
//   EntityTargetRegistry.Register("Player", this.transform);
//   EntityTargetRegistry.Unregister("Player", this.transform);  // in OnDestroy / OnDisable
//
// Modules call:
//   Transform t = EntityTargetRegistry.Resolve("Player");
using System.Collections.Generic;
using UnityEngine;

public static class EntityTargetRegistry
{
    // Supports multiple registered transforms per tag (e.g. multiple players in co-op).
    private static readonly Dictionary<string, List<Transform>> registry = new Dictionary<string, List<Transform>>();

    public static void Register(string tag, Transform target)
    {
        if (string.IsNullOrEmpty(tag) || !target)
            return;

        if (!registry.TryGetValue(tag, out List<Transform> list))
        {
            list = new List<Transform>();
            registry[tag] = list;
        }

        if (!list.Contains(target))
            list.Add(target);
    }

    public static void Unregister(string tag, Transform target)
    {
        if (string.IsNullOrEmpty(tag) || !registry.TryGetValue(tag, out List<Transform> list))
            return;

        list.Remove(target);
    }

    // Returns the closest live transform registered under this tag, or null if none.
    public static Transform Resolve(string tag, Vector3? nearestTo = null)
    {
        if (!registry.TryGetValue(tag, out List<Transform> list))
            return null;

        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            Transform t = list[i];
            if (!t) // destroyed — prune
            {
                list.RemoveAt(i);
                continue;
            }

            if (nearestTo == null)
                return t; // first live entry

            float dist = Vector3.Distance(nearestTo.Value, t.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = t;
            }
        }

        return best;
    }

    public static bool HasAny(string tag) => registry.TryGetValue(tag, out var list) && list.Count > 0;
}
