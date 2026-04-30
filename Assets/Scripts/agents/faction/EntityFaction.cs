// Attach to any entity to declare its faction and give access to relationship queries.
// Self-registers in EntityTargetRegistry on enable so targeting modules can find it.
//
// Factions are the sole definition of who targets whom — modules look up candidates
// by faction relationship, not by string tag.
using UnityEngine;

public class EntityFaction : MonoBehaviour
{
    [SerializeField] private FactionDefinition faction;
    [SerializeField] private FactionRelationshipTable relationshipTable;

    public FactionDefinition Faction => faction;

    private void OnEnable() => EntityTargetRegistry.Register(this);
    private void OnDisable() => EntityTargetRegistry.Unregister(this);

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
}
