// ScriptableObject that maps faction pairs to a relationship (Hostile / Neutral / Allied).
// One global instance referenced by EntityFaction components.
// Create via Assets > Create > Factions > Relationship Table.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.Agents
{
    public enum FactionRelationship { Neutral, Allied, Hostile }

    [Serializable]
    public struct FactionPairRelationship
    {
        public FactionDefinition factionA;
        public FactionDefinition factionB;
        public FactionRelationship relationship;
    }

    [CreateAssetMenu(menuName = "Factions/Relationship Table")]
    public class FactionRelationshipTable : ScriptableObject, IRegistryEntry
    {
        /// <summary>
        /// The asset's GUID. Same mechanism and same reason as <see cref="FactionDefinition.ID"/>:
        /// <c>EntityFaction.SetFaction</c> can swap the table as well as the faction — arena entities
        /// are given the match's own table — so which table is in force is state a save must name.
        /// </summary>
        [field: SerializeField]
        public string ID { get; set; }

        [SerializeField] private List<FactionPairRelationship> relationships;

        // Get() is called per candidate per targeting query, and the table grew to
        // ~130 pairs once the 16 solo deathmatch factions were added (every pair of
        // them is explicitly hostile). A linear scan there costs thousands of
        // comparisons per frame with a full arena, so pairs are indexed on first use.
        private Dictionary<long, FactionRelationship> lookup;
        private int indexedCount = -1;

        /// <summary>
        /// Drop the index, and put this table — and every faction it names — into the registries the
        /// save system resolves ids through.
        ///
        /// <para>
        /// Registering the FACTIONS from here is the load-bearing half. A <see cref="ScriptableObject"/>
        /// only self-registers once Unity has loaded it, and Unity only loads it once something
        /// referencing it is loaded. A faction assigned at runtime — an arena team, a re-teamed bot —
        /// may be referenced by nothing else in the session, so its id would be unresolvable on the
        /// very reload that needed it. A relationship table names every faction it arbitrates
        /// between, so loading one table seeds the whole roster.
        /// </para>
        /// </summary>
        private void OnEnable()
        {
            lookup = null;
            RegisterRoster();
        }

        private void OnValidate()
        {
            lookup = null;

#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path)) return;

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (ID == guid) return;

            ID = guid;
            EditorUtility.SetDirty(this);
#endif
        }

        private void RegisterRoster()
        {
            if (!string.IsNullOrEmpty(ID))
                Registry<FactionRelationshipTable>.Register(this);

            if (relationships == null) return;

            foreach (FactionPairRelationship pair in relationships)
            {
                FactionDefinition.Register(pair.factionA);
                FactionDefinition.Register(pair.factionB);
            }
        }

        public FactionRelationship Get(FactionDefinition a, FactionDefinition b)
        {
            if (a == null || b == null)
                return FactionRelationship.Neutral;

            if (a == b)
                return FactionRelationship.Allied;

            if (lookup == null || indexedCount != (relationships?.Count ?? 0))
                BuildLookup();

            return lookup.TryGetValue(Key(a, b), out FactionRelationship relationship)
                ? relationship
                : FactionRelationship.Neutral;
        }

        private void BuildLookup()
        {
            lookup = new Dictionary<long, FactionRelationship>();
            indexedCount = relationships?.Count ?? 0;
            if (relationships == null) return;

            foreach (FactionPairRelationship pair in relationships)
            {
                if (pair.factionA == null || pair.factionB == null)
                    continue;

                // TryAdd, not [], so a duplicated pair resolves to its first entry —
                // the same one the previous linear scan would have returned.
                lookup.TryAdd(Key(pair.factionA, pair.factionB), pair.relationship);
                lookup.TryAdd(Key(pair.factionB, pair.factionA), pair.relationship);
            }
        }

        private static long Key(FactionDefinition a, FactionDefinition b)
        {
            return ((long)a.GetInstanceID() << 32) ^ (uint)b.GetInstanceID();
        }

        public bool IsHostile(FactionDefinition a, FactionDefinition b) => Get(a, b) == FactionRelationship.Hostile;
        public bool IsAllied(FactionDefinition a, FactionDefinition b) => Get(a, b) == FactionRelationship.Allied;
    }
}
