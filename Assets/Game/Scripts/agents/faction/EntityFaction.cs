// Attach to any entity to declare its faction and give access to relationship queries.
// Self-registers in EntityTargetRegistry on enable so targeting modules can find it.
//
// Factions are the sole definition of who targets whom — modules look up candidates
// by faction relationship, not by string tag.
using System.Collections.Generic;
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

        private void OnDisable()
        {
            EntityTargetRegistry.Unregister(this);

            // Whoever this entity was told to overlook is forgotten with it. An ignore is a
            // relationship between two live objects — a creature that is despawned, streamed out
            // and brought back has no business still holding a grudge-shaped hole for somebody who
            // may have got off in the meantime, and the seat that granted it re-grants it on the
            // way back in.
            ignored.Clear();
        }

        // ── Individual exemptions ────────────────────────────────────────────────

        /// <summary>
        /// Entities this one cannot see, whatever the faction table says about them.
        ///
        /// <para>
        /// A per-entity exemption on top of the faction answer, not a second faction system. The
        /// case it exists for is a rider being carried: a player sitting on a robot's shoulder is
        /// still a hostile member of HumansFaction to every other robot in the world, and must stay
        /// one — but the machine carrying them cannot be allowed to turn round and fight its own
        /// passenger. Faction cannot express that, because it is a statement about the two SIDES
        /// and this is a statement about these two INDIVIDUALS.
        /// </para>
        /// <para>
        /// It lives here rather than on <see cref="AgentTargeting"/> because that is not the only
        /// thing that hunts. <c>DormantModule</c>, <c>FleeModule</c>, <c>WatchModule</c> and
        /// <c>ApproachModule</c> all ask <see cref="EntityTargetRegistry"/> directly, and an
        /// exemption those cannot see is one a sleeping conjurer wakes up in spite of. Every
        /// registry query already takes the asking entity's <see cref="EntityFaction"/>, so putting
        /// the list here is what makes one check cover all of them.
        /// </para>
        /// </summary>
        private readonly List<EntityFaction> ignored = new List<EntityFaction>();

        /// <summary>Stop seeing <paramref name="other"/>. Idempotent.</summary>
        public void Ignore(EntityFaction other)
        {
            if (other == null || other == this || ignored.Contains(other))
                return;
            ignored.Add(other);
        }

        /// <summary>See <paramref name="other"/> again. Idempotent.</summary>
        public void StopIgnoring(EntityFaction other)
        {
            if (other == null)
                return;
            ignored.Remove(other);
        }

        /// <summary>
        /// Is <paramref name="other"/> currently invisible to this entity?
        ///
        /// Cheap on the overwhelmingly common path — the list is empty for every entity that is not
        /// carrying somebody, so this is one count check per candidate.
        /// </summary>
        public bool Ignores(EntityFaction other)
        {
            if (ignored.Count == 0 || other == null)
                return false;

            // Destroyed entries are dropped as they are met rather than swept on a timer: the list
            // is at most a handful long and only ever walked by an entity that has one.
            for (int i = ignored.Count - 1; i >= 0; i--)
            {
                if (ignored[i] == null)
                    ignored.RemoveAt(i);
                else if (ignored[i] == other)
                    return true;
            }

            return false;
        }

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

        /// <summary>
        /// What this entity thinks of <paramref name="other"/>: grudge, then goodwill, then the
        /// faction table. See <see cref="FactionRelations.Resolve"/> for why that order.
        ///
        /// This is the ONLY way to ask. Every hunting module in the project comes through here —
        /// <c>EntityTargetRegistry</c>, <c>AgentTargeting</c>, <c>FleeModule</c>,
        /// <c>AlertBroadcaster</c> — and nothing else may call <c>FactionRelationshipTable.Get</c>
        /// directly, or an agent ends up chasing what it will not shoot.
        /// </summary>
        public FactionRelationship GetRelationshipWith(EntityFaction other) =>
            FactionRelations.Resolve(this, other);

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
