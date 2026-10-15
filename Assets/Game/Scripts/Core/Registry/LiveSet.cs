using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The enabled instances of a scene component, and the nearest of them to a point — for the
    /// things a spawned creature has to FIND because its prefab cannot reference a scene: the
    /// witch's blink points, the clearing her fight happens in.
    ///
    /// <para>
    /// An instance rather than a static generic, so the owner keeps the one static and resets it
    /// from its own <c>[RuntimeInitializeOnLoadMethod]</c> — a generic class cannot carry that
    /// attribute, and a set left over from the last play session names destroyed objects.
    /// </para>
    /// </summary>
    public sealed class LiveSet<T> where T : Component
    {
        private readonly List<T> live = new();

        public void Add(T instance)
        {
            if (instance != null && !live.Contains(instance)) live.Add(instance);
        }

        public void Remove(T instance) => live.Remove(instance);

        public void Clear() => live.Clear();

        /// <summary>The nearest instance within <paramref name="radius"/> of <paramref name="position"/>.</summary>
        public bool TryFindNearest(Vector3 position, float radius, out T nearest)
        {
            nearest = null;
            float best = radius * radius;
            foreach (T candidate in live)
            {
                if (candidate == null) continue;

                float d = (candidate.transform.position - position).sqrMagnitude;
                if (d > best) continue;

                best = d;
                nearest = candidate;
            }

            return nearest != null;
        }
    }
}
