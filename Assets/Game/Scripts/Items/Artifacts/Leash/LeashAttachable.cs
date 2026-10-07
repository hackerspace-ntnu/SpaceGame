using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Marks a GameObject that has ropes tied to it, and answers "what is tied to me".
    ///
    /// <para>
    /// Added at runtime, never authored — anything with a collider can be leashed, and a component
    /// on every prop in the game for a case most of them never hit is not worth the prefab churn.
    /// Multiple ropes per object are allowed, including several between the same pair.
    /// </para>
    /// </summary>
    public class LeashAttachable : MonoBehaviour
    {
        private readonly List<Leash> leashes = new();

        public IReadOnlyList<Leash> Leashes => leashes;

        public bool HasLeashes => leashes.Count > 0;

        public static LeashAttachable GetOrAdd(GameObject go)
        {
            var existing = go.GetComponent<LeashAttachable>();
            return existing != null ? existing : go.AddComponent<LeashAttachable>();
        }

        public void AddLeash(Leash l)
        {
            if (l != null && !leashes.Contains(l)) leashes.Add(l);
        }

        public void RemoveLeash(Leash l)
        {
            leashes.Remove(l);

            // Deliberately not destroying this component when the list empties. Destroy is deferred
            // to the end of the frame while the C# reference goes Unity-null immediately, so a rope
            // tied to the same object later in the same frame would make GetOrAdd add a SECOND
            // marker. An empty marker costs nothing and avoids the race entirely.
        }

        private void OnDestroy()
        {
            // The thing the ropes were tied to is going. Take them with it — a rope tied to nothing
            // is not a rope, and one left hanging in the air is the bug this used to have: it logged
            // its own destruction and then deliberately left every rope behind so they could be
            // inspected in the Hierarchy. That was scaffolding, and it shipped.
            for (int i = leashes.Count - 1; i >= 0; i--) leashes[i]?.Dispose();
            leashes.Clear();
        }
    }
}
