// Attach to any entity to declare its faction and give access to relationship queries.
// Targeting modules check this instead of hardcoding tags.
using UnityEngine;

public class EntityFaction : MonoBehaviour
{
    [SerializeField] private FactionDefinition faction;
    [SerializeField] private FactionRelationshipTable relationshipTable;

    public FactionDefinition Faction => faction;

    public FactionRelationship GetRelationshipWith(EntityFaction other)
    {
        if (other == null || relationshipTable == null)
            return FactionRelationship.Neutral;
        return relationshipTable.Get(faction, other.Faction);
    }

    public bool IsHostileTo(EntityFaction other) => GetRelationshipWith(other) == FactionRelationship.Hostile;
    public bool IsAlliedWith(EntityFaction other) => GetRelationshipWith(other) == FactionRelationship.Allied;

    // Convenience: check a Transform without requiring a cached reference.
    public bool IsHostileTo(Transform other)
    {
        if (!other)
            return false;
        EntityFaction otherFaction = other.GetComponentInParent<EntityFaction>();
        return IsHostileTo(otherFaction);
    }

    // Shared guard used by targeting modules.
    // Returns true when the candidate is a valid target given the owner's faction settings.
    // requiredRelationship: Hostile = only target enemies, Allied = only target friends, Neutral = target anyone.
    public static bool IsValidTarget(Transform owner, Transform candidate, FactionRelationship requiredRelationship)
    {
        if (!candidate)
            return false;

        EntityFaction ownerFaction = owner ? owner.GetComponentInParent<EntityFaction>() : null;
        if (ownerFaction == null)
            return true; // No faction — target anyone.

        EntityFaction candidateFaction = candidate.GetComponentInParent<EntityFaction>();
        if (candidateFaction == null)
            return requiredRelationship != FactionRelationship.Allied; // Unfactioned entities are not allies.

        return ownerFaction.GetRelationshipWith(candidateFaction) == requiredRelationship;
    }
}
