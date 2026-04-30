// Faction-keyed registry of all targetable entities in the scene.
// Entities self-register via EntityFaction on enable. Targeting modules call
// ResolveNearest to find the nearest entity whose faction relationship matches
// what the module needs (Hostile / Allied / Neutral).
//
// Factions are the SOLE definition of who targets whom — there is no string-tag
// fallback. An entity without an EntityFaction is invisible to the targeting system.
using System.Collections.Generic;
using UnityEngine;

public static class EntityTargetRegistry
{
    private static readonly List<EntityFaction> entities = new List<EntityFaction>();

    public static void Register(EntityFaction entity)
    {
        if (entity == null)
            return;
        if (!entities.Contains(entity))
            entities.Add(entity);
    }

    public static void Unregister(EntityFaction entity)
    {
        entities.Remove(entity);
    }

    // Returns the nearest registered entity whose relationship to `owner` equals `required`.
    // Returns null if no entity matches. Owner is required — factionless owners cannot target.
    public static Transform ResolveNearest(EntityFaction owner, FactionRelationship required, Vector3 position)
    {
        if (owner == null)
            return null;

        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = entities.Count - 1; i >= 0; i--)
        {
            EntityFaction e = entities[i];
            if (e == null)
            {
                entities.RemoveAt(i);
                continue;
            }
            if (e == owner)
                continue;
            if (owner.GetRelationshipWith(e) != required)
                continue;

            float d = Vector3.Distance(position, e.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = e.transform;
            }
        }

        return best;
    }

    public static bool HasAny(EntityFaction owner, FactionRelationship required)
    {
        if (owner == null)
            return false;
        foreach (EntityFaction e in entities)
        {
            if (e == null || e == owner)
                continue;
            if (owner.GetRelationshipWith(e) == required)
                return true;
        }
        return false;
    }
}
