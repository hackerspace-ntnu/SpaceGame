// Faction-keyed registry of all targetable entities in the scene.
// Entities self-register via EntityFaction on enable. Targeting modules call
// ResolveNearest to find the nearest entity whose faction relationship matches
// what the module needs (Hostile / Allied / Neutral).
//
// Factions are the SOLE definition of who targets whom — there is no string-tag
// fallback. An entity without an EntityFaction is invisible to the targeting system.
//
// One exception, and it is per-asker rather than per-faction: EntityFaction.Ignores lets a
// single entity be blanked out for a single other entity. Every query below honours it, which
// is the point — AgentTargeting is not the only thing that hunts, and an exemption the dormant,
// flee, watch and approach modules could not see would be one they act on anyway.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class EntityTargetRegistry
    {
        private static readonly List<EntityFaction> entities = new List<EntityFaction>();

        // Read-only view for systems that need to enumerate every registered entity
        // (e.g. the helmet AR HUD building markers per frame).
        public static IReadOnlyList<EntityFaction> All => entities;

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
                if (owner.Ignores(e))
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

        // Collects every registered entity within maxRange whose relationship to `owner` equals
        // `required`, into a caller-owned list. Compares squared distances and rejects on range
        // before the relationship lookup, so a distant crowd costs one subtraction per entity
        // instead of a dictionary probe and a sqrt.
        //
        // Use this instead of calling ResolveNearest once per module: AgentTargeting runs one
        // query per agent per interval and shares the result with every behaviour module.
        public static void Query(EntityFaction owner, FactionRelationship required, Vector3 position,
                                 float maxRange, List<EntityFaction> results)
        {
            results.Clear();
            if (owner == null)
                return;

            float maxRangeSqr = maxRange > 0f ? maxRange * maxRange : float.MaxValue;

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
                if ((e.transform.position - position).sqrMagnitude > maxRangeSqr)
                    continue;
                if (owner.Ignores(e))
                    continue;
                if (owner.GetRelationshipWith(e) != required)
                    continue;

                results.Add(e);
            }
        }

        // The same query for something that has a side but is not itself an entity — a settlement
        // alarm, a territory. It owns no EntityFaction (registering one would make the town a
        // target), so it asks by definition and table instead.
        public static void Query(FactionDefinition owner, FactionRelationshipTable table,
                                 FactionRelationship required, Vector3 position, float maxRange,
                                 List<EntityFaction> results)
        {
            results.Clear();
            if (owner == null || table == null)
                return;

            float maxRangeSqr = maxRange > 0f ? maxRange * maxRange : float.MaxValue;

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                EntityFaction e = entities[i];
                if (e == null)
                {
                    entities.RemoveAt(i);
                    continue;
                }
                if ((e.transform.position - position).sqrMagnitude > maxRangeSqr)
                    continue;
                if (table.Get(owner, e.Faction) != required)
                    continue;

                results.Add(e);
            }
        }

        public static bool HasAny(EntityFaction owner, FactionRelationship required)
        {
            if (owner == null)
                return false;
            foreach (EntityFaction e in entities)
            {
                if (e == null || e == owner)
                    continue;
                if (owner.Ignores(e))
                    continue;
                if (owner.GetRelationshipWith(e) == required)
                    return true;
            }
            return false;
        }
    }
}
