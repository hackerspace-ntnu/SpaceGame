// Attach to any entity to declare its faction and give access to relationship queries.
// Self-registers in EntityTargetRegistry on enable so targeting modules can find it.
//
// Factions are the sole definition of who targets whom — modules look up candidates
// by faction relationship, not by string tag.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public class EntityFaction : MonoBehaviour
    {
        [SerializeField] private FactionDefinition faction;
        [SerializeField] private FactionRelationshipTable relationshipTable;

        public FactionDefinition Faction => faction;

        /// <summary>
        /// The table this entity arbitrates its relationships through. Exposed so a save can record
        /// which one was in force — <see cref="SetFaction"/> swaps it as well as the faction.
        /// </summary>
        public FactionRelationshipTable RelationshipTable => relationshipTable;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// Separate from <see cref="SetFaction"/> only in that a null <paramref name="restoredTable"/>
        /// CLEARS the table rather than leaving the serialized one — a restore has to be able to put
        /// this entity back exactly as it was, including back to having no table.
        /// </para>
        /// <para>
        /// No re-registration is needed: <see cref="EntityTargetRegistry"/> holds the component, not
        /// the faction, and every relationship query reads these fields live.
        /// </para>
        /// </summary>
        public void RestoreFaction(FactionDefinition restoredFaction, FactionRelationshipTable restoredTable)
        {
            faction = restoredFaction;
            relationshipTable = restoredTable;
        }

        // Assigns faction at runtime, before OnEnable registers this entity into
        // EntityTargetRegistry. Used by MatchManager when spawning match entities
        // (bots and players) whose faction depends on chosen team/gamemode, not
        // on what's serialized in the prefab.
        public void SetFaction(FactionDefinition newFaction, FactionRelationshipTable table = null)
        {
            faction = newFaction;
            if (table != null)
                relationshipTable = table;
        }

        private void OnEnable() => EntityTargetRegistry.Register(this);
        private void OnDisable() => EntityTargetRegistry.Unregister(this);

        // Guarantees an entity is visible to targeting, adding the component if the prefab is
        // missing one. Every spawn path should go through here.
        //
        // A silent null check is not good enough: the networked player prefab shipped without an
        // EntityFaction, and because both MatchManager and the targeting modules simply skipped
        // entities that had none, no AI in either the open world or the arena could see a human
        // player — with no error anywhere to say so.
        public static EntityFaction Ensure(GameObject entity, FactionDefinition faction,
                                           FactionRelationshipTable table)
        {
            if (entity == null)
                return null;

            EntityFaction component = entity.GetComponent<EntityFaction>();
            if (component == null)
            {
                Debug.LogWarning($"[EntityFaction] {entity.name} has no EntityFaction — adding one at " +
                                 "runtime so it is targetable. Add the component to the prefab to " +
                                 "silence this.", entity);
                component = entity.AddComponent<EntityFaction>();
            }

            if (faction != null)
                component.SetFaction(faction, table);
            else if (component.Faction == null)
                Debug.LogError($"[EntityFaction] {entity.name} has no faction assigned and none was " +
                               "supplied. It will be invisible to every targeting module.", entity);

            return component;
        }

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
}
