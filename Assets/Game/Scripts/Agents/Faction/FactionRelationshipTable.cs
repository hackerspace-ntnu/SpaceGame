// ScriptableObject that maps faction pairs to a relationship (Hostile / Neutral / Allied).
// One global instance referenced by EntityFaction components.
// Create via Assets > Create > Factions > Relationship Table.
using System;
using System.Collections.Generic;
using UnityEngine;

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
    public class FactionRelationshipTable : ScriptableObject
    {
        [SerializeField] private List<FactionPairRelationship> relationships;

        // Get() is called per candidate per targeting query, and the table grew to
        // ~130 pairs once the 16 solo deathmatch factions were added (every pair of
        // them is explicitly hostile). A linear scan there costs thousands of
        // comparisons per frame with a full arena, so pairs are indexed on first use.
        private Dictionary<long, FactionRelationship> lookup;
        private int indexedCount = -1;

        private void OnEnable() => lookup = null;
        private void OnValidate() => lookup = null;

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
