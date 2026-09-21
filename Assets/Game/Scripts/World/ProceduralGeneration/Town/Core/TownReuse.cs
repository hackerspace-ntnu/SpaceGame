// Pairing the town you asked for with the town that is already standing there.
//
// Pressing Generate a second time is the normal case, not the exception: you widen the spacing, you
// look, you widen it again. The naive implementation — wipe the Generated child and rebuild — is
// wrong in a way that does not show up until much later, because every re-created object is minted
// a fresh SaveableEntity identity and every save record about the old one is orphaned for good
// (Towns.md, Gotchas).
//
// So instead: work out which of the objects already there can be the thing each new slot asks for,
// move those, and only create or destroy the difference. Change the spacing and nothing is
// destroyed at all — the same huts simply stand further apart, which is the whole point.
//
// Pure, and that is deliberate: it takes a Transform and reads components, but it instantiates
// nothing, destroys nothing and touches no Physics, so the pairing rules are testable in EditMode
// without a terrain or a prefab — the same split TownLayout already makes.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Towns
{
    /// <summary>
    /// What makes two placements "the same thing in a different place": the same prefab, placed by
    /// the same group of the same section.
    ///
    /// The prefab is held by REFERENCE, not by its index in the group's array. Reordering that array
    /// is an ordinary thing to do in the inspector, and an index key would quietly decide every hut
    /// was now a water tower.
    /// </summary>
    public readonly struct TownReuseKey : IEquatable<TownReuseKey>
    {
        public readonly TownSection Section;
        public readonly int GroupIndex;
        public readonly GameObject Source;

        public TownReuseKey(TownSection section, int groupIndex, GameObject source)
        {
            Section = section;
            GroupIndex = groupIndex;
            Source = source;
        }

        public bool Equals(TownReuseKey other) =>
            Section == other.Section &&
            GroupIndex == other.GroupIndex &&
            ReferenceEquals(Source, other.Source);

        public override bool Equals(object obj) => obj is TownReuseKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine((int)Section, GroupIndex,
                             Source == null ? 0 : Source.GetInstanceID());
    }

    /// <summary>
    /// The answer: for each slot, the object that should become it (or null — make a new one), and
    /// everything left over that nothing asked for.
    /// </summary>
    public readonly struct TownReusePlan
    {
        /// <summary>One entry per slot, in slot order. Null means "nothing suitable exists".</summary>
        public readonly GameObject[] Reused;

        /// <summary>Already-placed objects no slot wants. These are what Generate destroys.</summary>
        public readonly List<GameObject> Surplus;

        public TownReusePlan(GameObject[] reused, List<GameObject> surplus)
        {
            Reused = reused;
            Surplus = surplus;
        }

        public int ReuseCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Reused.Length; i++)
                    if (Reused[i] != null) n++;

                return n;
            }
        }
    }

    public static class TownReuse
    {
        /// <summary>
        /// Match the slots against what is already under <paramref name="root"/>.
        ///
        /// A child with no <see cref="TownInstance"/> is surplus by definition. That covers three
        /// things at once: foundation pads, which are re-measured against the new ground every time
        /// and so are always rebuilt; anything dropped in by hand, which does not belong under a
        /// generated root; and towns generated before the marker existed, which cannot be matched
        /// and whose wholesale replacement is exactly what <c>confirmRegenerate</c> is there to warn
        /// about.
        /// </summary>
        public static TownReusePlan Match(IReadOnlyList<TownSlot> slots,
                                          Func<TownSlot, GameObject> prefabOf,
                                          Transform root)
        {
            int count = slots?.Count ?? 0;
            var reused = new GameObject[count];
            var surplus = new List<GameObject>();

            if (root == null || prefabOf == null) return new TownReusePlan(reused, surplus);

            var pool = new Dictionary<TownReuseKey, Queue<GameObject>>();

            for (int i = 0; i < root.childCount; i++)
            {
                GameObject child = root.GetChild(i).gameObject;
                TownInstance stamp = child.GetComponent<TownInstance>();

                if (stamp == null || stamp.source == null)
                {
                    surplus.Add(child);
                    continue;
                }

                if (!pool.TryGetValue(stamp.Key, out Queue<GameObject> queue))
                    pool[stamp.Key] = queue = new Queue<GameObject>();

                queue.Enqueue(child);
            }

            for (int i = 0; i < count; i++)
            {
                GameObject prefab = prefabOf(slots[i]);
                if (prefab == null) continue;

                var key = new TownReuseKey(slots[i].Section, slots[i].GroupIndex, prefab);
                if (pool.TryGetValue(key, out Queue<GameObject> queue) && queue.Count > 0)
                    reused[i] = queue.Dequeue();
            }

            foreach (Queue<GameObject> queue in pool.Values)
                while (queue.Count > 0)
                    surplus.Add(queue.Dequeue());

            return new TownReusePlan(reused, surplus);
        }
    }
}
