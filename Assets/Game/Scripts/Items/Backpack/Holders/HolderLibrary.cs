using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Which prefab holds a given <see cref="HolderKind"/> down.
    ///
    /// <para>
    /// One asset rather than five fields on the pack, because the mapping is art data that outlives
    /// any one rig: the expedition pack and anything later that lays gear out on surfaces want the
    /// same five holders, and a second pack should not mean a second set of drag-and-drop.
    /// </para>
    /// <para>
    /// The lookup is <b>total</b>. <see cref="ItemFootprint.HolderFor"/> is a guess made from
    /// measured proportions, so it can name a kind nobody has modelled yet, and it is asked on
    /// every layout rebuild — which happens on every placement, every save restore and every wire
    /// update. A throw there would take out the whole display for a cosmetic gap, and a warning
    /// per call would bury the console. So an unmapped kind returns null, once with a warning and
    /// silently thereafter, and <see cref="HolderBuilder"/> treats null as "this item lies bare".
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "HolderLibrary", menuName = "Items/Holder Library")]
    public class HolderLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public HolderKind kind;

            [Tooltip("Modelled 1.00 m along +X (the stretch axis) and 1.00 m across +Z, origin at " +
                     "the gripping centre. Every rigid part — buckles, hooks, tensioners, snap " +
                     "gates — parented under a HARD_ empty, which HolderBuilder counter-scales.")]
            public GameObject prefab;
        }

        [Tooltip("One row per holder kind. A kind with no row, or a row with no prefab, means " +
                 "items of that shape are laid out with nothing over them.")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        /// <summary>
        /// Kind to prefab, built on first use. A <see cref="List{T}"/> is what the inspector can
        /// author; a dictionary is what a per-item lookup on every rebuild should cost.
        /// </summary>
        private Dictionary<HolderKind, GameObject> lookup;

        /// <summary>
        /// Kinds already complained about. An instance field rather than a static one so a test
        /// that mints its own library gets its own slate, and so reloading the asset re-arms the
        /// warning after the author has had a chance to fix it.
        /// </summary>
        [NonSerialized] private HashSet<HolderKind> warned;

        /// <summary>
        /// The prefab for <paramref name="kind"/>, or null if this library does not map it.
        /// Never throws — see the note on the class about why.
        /// </summary>
        public GameObject PrefabFor(HolderKind kind)
        {
            if (lookup == null) BuildLookup();

            if (lookup.TryGetValue(kind, out GameObject prefab) && prefab != null) return prefab;

            WarnOnce(kind);
            return null;
        }

        /// <summary>Does this library have art for <paramref name="kind"/>?</summary>
        public bool Has(HolderKind kind)
        {
            if (lookup == null) BuildLookup();

            return lookup.TryGetValue(kind, out GameObject prefab) && prefab != null;
        }

        private void BuildLookup()
        {
            lookup = new Dictionary<HolderKind, GameObject>();

            if (entries == null) return;

            foreach (Entry entry in entries)
            {
                if (entry == null || entry.prefab == null) continue;

                // First row wins. A duplicate is an authoring slip rather than an intent to
                // override, and silently taking the last one makes which prefab is live depend on
                // list order, which nothing in the inspector suggests it should.
                if (lookup.ContainsKey(entry.kind))
                {
                    Debug.LogWarning($"HolderLibrary '{name}': {entry.kind} is mapped more than " +
                                     $"once. Keeping the first row and ignoring " +
                                     $"'{entry.prefab.name}'.", this);
                    continue;
                }

                lookup[entry.kind] = entry.prefab;
            }
        }

        private void WarnOnce(HolderKind kind)
        {
            warned ??= new HashSet<HolderKind>();

            if (!warned.Add(kind)) return;

            Debug.LogWarning($"HolderLibrary '{name}': nothing is mapped for {kind}, so items of " +
                             $"that shape lie bare on the surface. This message appears once per " +
                             $"kind per load.", this);
        }

        /// <summary>Re-read the entries. For the inspector, and for tests that fill one in code.</summary>
        private void OnValidate()
        {
            lookup = null;
            warned = null;
        }
    }
}
