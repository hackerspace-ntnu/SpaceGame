using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists which side an entity is on.
    ///
    /// <b>This is the one on the list whose failure mode is the world turning on itself.</b>
    /// <c>EntityFaction.SetFaction</c> writes the serialized <c>faction</c> and
    /// <c>relationshipTable</c> fields at runtime — <c>MatchManager</c> calls it for every bot and
    /// every player it spawns, because which team they are on depends on the gamemode and not on the
    /// prefab — and <c>EntityFaction.Ensure</c> will even AddComponent one at spawn. Nothing captured
    /// either field. So a re-teamed entity reloads on its prefab's faction and is then wrong in one
    /// of two directions: it attacks the side it was fighting for, or its relationship to everything
    /// resolves as Neutral and nothing can target it at all. Factions are the SOLE definition of who
    /// targets whom here, so getting this wrong is not a degraded fight, it is a different one.
    ///
    /// <b>Keyed by GUID, like every other ScriptableObject this project persists.</b> Both types are
    /// assets, and a save file cannot hold an object reference. <c>FactionDefinition.ID</c> and
    /// <c>FactionRelationshipTable.ID</c> are stamped from the asset GUID in OnValidate and
    /// self-register into <c>Registry&lt;T&gt;</c> on load — the same mechanism <c>InventoryItem</c>
    /// and <c>TargetingProfile</c> already use, chosen over the display name (a designer-facing string
    /// that is expected to change) and over a list index (which moves the moment a faction is added).
    ///
    /// <b>Not deferred, and the registry is why.</b> There is no scene object to wait for; there is an
    /// asset that must have been loaded. A relationship table registers every faction it arbitrates
    /// between as it loads, so one table in the session seeds the whole roster — including the arena
    /// teams that nothing else in a loaded chunk happens to reference.
    /// </summary>
    [RequireComponent(typeof(EntityFaction))]
    public class EntityFactionSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "faction";

        private EntityFaction entityFaction;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private EntityFaction Faction =>
            entityFaction != null ? entityFaction : entityFaction = GetComponent<EntityFaction>();

        public string SaveKey => Key;

        public struct State
        {
            public string factionId;
            public string tableId;
        }

        public object CaptureState()
        {
            if (Faction == null) return null;

            FactionDefinition faction = Faction.Faction;
            FactionRelationshipTable table = Faction.RelationshipTable;

            string factionId = faction != null ? faction.ID : null;
            string tableId = table != null ? table.ID : null;

            // Nothing nameable, nothing stored. An entity holding only assets with no stamped id —
            // or a faction built at runtime with CreateInstance — has no faction a file can describe,
            // and the honest record is the absent one, which reads back as "leave the prefab's".
            if (string.IsNullOrEmpty(factionId) && string.IsNullOrEmpty(tableId)) return null;

            if (faction != null && string.IsNullOrEmpty(factionId))
            {
                Debug.LogWarning($"[Save] '{name}' is on faction '{faction.name}', which has no id, so " +
                                 "its team cannot be saved and it will reload on its prefab's faction. " +
                                 "Re-import the faction asset — OnValidate stamps the id.", this);
            }

            return new State { factionId = factionId, tableId = tableId };
        }

        public void RestoreState(JObject state)
        {
            if (Faction == null) return;

            // No record means the entity was on the faction its prefab ships with, which the scene
            // has already put there. Deliberately NOT cleared to null: an entity with no faction is
            // invisible to every targeting module, so guessing wrong in that direction silently
            // removes it from the world's combat rather than merely mis-teaming it.
            if (state == null) return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            FactionDefinition faction = string.IsNullOrEmpty(restored.factionId)
                ? Faction.Faction
                : Registry<FactionDefinition>.Get(restored.factionId);

            FactionRelationshipTable table = string.IsNullOrEmpty(restored.tableId)
                ? Faction.RelationshipTable
                : Registry<FactionRelationshipTable>.Get(restored.tableId);

            // A named asset that is not in the registry is a real failure and worth saying out loud:
            // the entity keeps what it has, which is its prefab's side, and that is exactly the bug
            // this saver exists to fix — so it must not fail silently.
            if (faction == null && !string.IsNullOrEmpty(restored.factionId))
            {
                Debug.LogWarning($"[Save] Faction '{restored.factionId}' is not in the registry — " +
                                 $"'{name}' is reloading on its prefab's faction. Was the asset " +
                                 "deleted, or is nothing in the session loading it?", this);
                return;
            }

            if (table == null && !string.IsNullOrEmpty(restored.tableId))
            {
                Debug.LogWarning($"[Save] Relationship table '{restored.tableId}' is not in the " +
                                 $"registry — '{name}' is reloading on its prefab's table.", this);
                return;
            }

            Faction.RestoreFaction(faction, table);
        }
    }
}
