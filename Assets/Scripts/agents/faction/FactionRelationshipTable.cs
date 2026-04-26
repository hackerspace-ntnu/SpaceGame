// ScriptableObject that maps faction pairs to a relationship (Hostile / Neutral / Allied).
// One global instance referenced by EntityFaction components.
// Create via Assets > Create > Factions > Relationship Table.
using System;
using System.Collections.Generic;
using UnityEngine;

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

    public FactionRelationship Get(FactionDefinition a, FactionDefinition b)
    {
        if (a == null || b == null)
            return FactionRelationship.Neutral;

        if (a == b)
            return FactionRelationship.Allied;

        foreach (FactionPairRelationship pair in relationships)
        {
            if ((pair.factionA == a && pair.factionB == b) ||
                (pair.factionA == b && pair.factionB == a))
                return pair.relationship;
        }

        return FactionRelationship.Neutral;
    }

    public bool IsHostile(FactionDefinition a, FactionDefinition b) => Get(a, b) == FactionRelationship.Hostile;
    public bool IsAllied(FactionDefinition a, FactionDefinition b) => Get(a, b) == FactionRelationship.Allied;
}
