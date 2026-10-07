using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// The root of a save file. Everything persisted hangs off this one object, and it is the only
    /// type <see cref="SaveFileStore"/> reads or writes.
    /// </summary>
    public class SaveDocument
    {
        /// <summary>
        /// Bump whenever a change to the shape below cannot be absorbed by a saver's own
        /// <see cref="StateBag.TryGet{T}"/> returning false — and add the matching
        /// <see cref="ISaveMigration"/> in the same commit. See <see cref="SaveMigrator"/>.
        /// </summary>
        public const int CurrentVersion = 2;

        [JsonProperty("header")] public SaveHeader Header = new();
        [JsonProperty("players")] public List<PlayerRecord> Players = new();
        [JsonProperty("world")] public WorldRecord World = new();

        /// <summary>Repairs a document whose optional sections are missing, so consumers never null-check.</summary>
        public SaveDocument Normalized()
        {
            Header ??= new SaveHeader();
            Players ??= new List<PlayerRecord>();
            World ??= new WorldRecord();
            World.Normalize();
            return this;
        }

        public PlayerRecord FindPlayer(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || Players == null) return null;

            foreach (PlayerRecord record in Players)
                if (record != null && record.ProfileId == profileId)
                    return record;

            return null;
        }
    }

    /// <summary>
    /// The part of a save that a slot listing can show without loading the rest. Kept deliberately
    /// small and free of game types: a corrupt or future-versioned body must still let the menu
    /// print a row rather than fail to draw the screen.
    /// </summary>
    public class SaveHeader
    {
        [JsonProperty("version")] public int Version = SaveDocument.CurrentVersion;
        [JsonProperty("savedAtUtc")] public DateTime SavedAtUtc = DateTime.UtcNow;
        [JsonProperty("playtimeSeconds")] public double PlaytimeSeconds;
        [JsonProperty("gameVersion")] public string GameVersion = string.Empty;
        [JsonProperty("slotLabel")] public string SlotLabel = string.Empty;

        /// <summary>The world's player-facing name. Empty on saves written before world selection.</summary>
        [JsonProperty("worldName")] public string WorldName = string.Empty;

        /// <summary>
        /// The GUID of the WorldStreamingConfig this world's chunk deltas were recorded against.
        /// Empty on legacy saves, which are taken to belong to the main world. Checked on load by
        /// <see cref="WorldIdentity.AcceptsConfig"/>.
        /// </summary>
        [JsonProperty("worldConfigId")] public string WorldConfigId = string.Empty;
    }

    /// <summary>
    /// One player's persisted state, keyed by a per-machine profile GUID rather than by Netcode's
    /// client id — client ids are handed out per connection and mean nothing across sessions.
    /// </summary>
    public class PlayerRecord
    {
        [JsonProperty("profileId")] public string ProfileId = string.Empty;
        [JsonProperty("displayName")] public string DisplayName = string.Empty;
        [JsonProperty("position")] public Vector3 Position;
        [JsonProperty("rotation")] public Quaternion Rotation = Quaternion.identity;
        [JsonProperty("state")] public StateBag State = new();

        public StateBag EnsureState() => State ??= new StateBag();
    }

    /// <summary>Everything outside the players: the streamed world, plus state that belongs to no scene.</summary>
    public class WorldRecord
    {
        /// <summary>Session-wide state with no scene of its own — the game timer, global flags.</summary>
        [JsonProperty("global")] public StateBag Global = new();

        /// <summary>
        /// Every persisted world object, keyed by its own stable instance id.
        ///
        /// Flat and global, NOT partitioned by scene — and that is the whole point. The scene an
        /// object stands in is not a property of the object, it is a property of where it happens to
        /// be RIGHT NOW: <c>WorldStreamer.UpdateSceneMembership</c> moves every SceneTracked entity
        /// between the persistent scene and whichever chunk it has wandered into. Filing an object's
        /// record under that scene meant a creature captured in the chunk it walked to was looked
        /// for, on load, in the chunk it walked to — while the scene file had put it back where it
        /// was authored. The lookup missed, the record was dropped, and every creature in the world
        /// reappeared at its authored position while the player (keyed by profile, not by scene)
        /// restored perfectly.
        ///
        /// Identity is the only thing about an object that does not move, so identity is the key.
        /// <see cref="EntityRecord.Scene"/> keeps the routing information that streaming still needs,
        /// as a field that can be re-stamped rather than as the address of the record.
        /// </summary>
        [JsonProperty("entities")] public Dictionary<string, EntityRecord> Entities = new();

        /// <summary>
        /// Authored objects the player destroyed for good.
        ///
        /// Global for the same reason <see cref="Entities"/> is: a creature killed in a chunk it had
        /// wandered into must stay dead when its home scene loads it again.
        /// </summary>
        [JsonProperty("destroyed")] public List<string> Destroyed = new();

        /// <summary>
        /// <see cref="Destroyed"/> as a set, rebuilt by <see cref="Normalize"/>.
        ///
        /// The list is the serialized shape and stays a list, because a JSON array is the honest
        /// representation and changing it would need a migration for nothing. But
        /// <see cref="IsDestroyed"/> is asked once per authored object per chunk hydrate, and
        /// answering it with <c>List.Contains</c> made chunk loading cost O(objects × kills). In a
        /// world that is played rather than tested, the tombstone list only ever grows, so that is a
        /// load time that gets worse for as long as the save is used.
        /// </summary>
        [JsonIgnore] private HashSet<string> destroyedSet;

        [JsonIgnore] public int Count => Entities?.Count ?? 0;

        public void Normalize()
        {
            Global ??= new StateBag();
            Entities ??= new Dictionary<string, EntityRecord>();
            Destroyed ??= new List<string>();

            destroyedSet = new HashSet<string>(Destroyed);

            foreach (KeyValuePair<string, EntityRecord> entry in Entities)
            {
                if (entry.Value == null) continue;
                entry.Value.EnsureState();

                // The key is the identity. A record whose own field disagrees with it — hand-edited
                // file, a migration that moved records around — would spawn under one id and be
                // looked up under another.
                if (entry.Value.InstanceId != entry.Key) entry.Value.InstanceId = entry.Key;
            }
        }

        /// <summary>The record for an id, created empty on first use.</summary>
        public EntityRecord GetOrCreate(string instanceId)
        {
            Entities ??= new Dictionary<string, EntityRecord>();

            if (!Entities.TryGetValue(instanceId, out EntityRecord record) || record == null)
            {
                record = new EntityRecord { InstanceId = instanceId };
                Entities[instanceId] = record;
            }

            return record;
        }

        public bool TryGet(string instanceId, out EntityRecord record)
        {
            record = null;
            if (Entities == null || instanceId == null) return false;
            return Entities.TryGetValue(instanceId, out record) && record != null;
        }

        /// <summary>
        /// The records currently routed to one scene. Used to decide what a scene load has to spawn,
        /// which is the one question that still needs the scene partition.
        /// </summary>
        public IEnumerable<EntityRecord> InScene(string sceneKey)
        {
            if (Entities == null || sceneKey == null) yield break;

            foreach (EntityRecord record in Entities.Values)
                if (record != null && record.Scene == sceneKey)
                    yield return record;
        }

        public bool IsDestroyed(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;

            // Deserialization fills the list directly and never calls Normalize, so the set can be
            // null here on a freshly read document.
            destroyedSet ??= new HashSet<string>(Destroyed ?? new List<string>());
            return destroyedSet.Contains(instanceId);
        }

        /// <summary>Buries an authored object, keeping the list and its index in step.</summary>
        public void MarkDestroyed(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return;

            Destroyed ??= new List<string>();
            destroyedSet ??= new HashSet<string>(Destroyed);

            if (destroyedSet.Add(instanceId)) Destroyed.Add(instanceId);
        }

        /// <summary>
        /// Lifts a tombstone. The counterpart to <see cref="MarkDestroyed"/>, and the thing whose
        /// absence made the list grow forever: nothing in the project could ever remove one.
        /// </summary>
        public bool ClearDestroyed(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || Destroyed == null) return false;

            destroyedSet ??= new HashSet<string>(Destroyed);
            if (!destroyedSet.Remove(instanceId)) return false;

            Destroyed.Remove(instanceId);
            return true;
        }
    }

    /// <summary>
    /// One persisted world object — authored or runtime-spawned.
    ///
    /// Both populations share this shape but are restored differently, which is what
    /// <see cref="Authored"/> selects. A runtime object does not exist until something spawns it, so
    /// its record carries everything needed to recreate it. An authored object is already in the
    /// scene file when it loads, so re-creating it would duplicate it — its record is a delta applied
    /// to the object already standing there.
    /// </summary>
    public class EntityRecord
    {
        /// <summary>Asset GUID of the prefab, resolved through the saveable-prefab registry.</summary>
        [JsonProperty("prefabId")] public string PrefabId = string.Empty;

        /// <summary>Identity of this particular instance, stable across save/load.</summary>
        [JsonProperty("instanceId")] public string InstanceId = string.Empty;

        /// <summary>
        /// The <see cref="SceneKey"/> this object was in when it was last captured. Routing, not
        /// identity: it decides which scene load spawns a runtime object, and is re-stamped every
        /// time the object is captured somewhere else. Authored objects ignore it entirely — they
        /// are restored wherever the scene file puts them.
        /// </summary>
        [JsonProperty("scene")] public string Scene = string.Empty;

        /// <summary>True for objects the scene file already contains. Restored in place, never spawned.</summary>
        [JsonProperty("authored")] public bool Authored;

        [JsonProperty("position")] public Vector3 Position;
        [JsonProperty("rotation")] public Quaternion Rotation = Quaternion.identity;
        [JsonProperty("scale")] public Vector3 Scale = Vector3.one;

        /// <summary>
        /// Whether <see cref="Position"/> means anything.
        ///
        /// True for everything this build writes — a capture always records where the object was.
        /// It exists for the one case that cannot: a v1 authored record whose object had no
        /// transform saver never stored a pose anywhere, and a zero <see cref="Position"/> is
        /// indistinguishable from an object that really was at the origin. Without this the
        /// migration would teleport every such object to (0,0,0) on the first load.
        /// </summary>
        [JsonProperty("hasPose")] public bool HasPose = true;

        /// <summary>
        /// Whether <see cref="Scale"/> means anything.
        ///
        /// Replaces a <c>scale != Vector3.zero</c> sentinel that was repeated in three places and
        /// was wrong in both directions. It could not actually detect the case it was written for —
        /// a record with no scale field deserializes to the field initializer, <c>Vector3.one</c>,
        /// never to zero — and it silently overrode the one thing it could detect: an object
        /// deliberately scaled to zero, which is a common way to hide something without disabling
        /// it. Such an object came back at full size.
        ///
        /// Defaults true so every record already written keeps restoring its scale exactly as before.
        /// </summary>
        [JsonProperty("hasScale")] public bool HasScale = true;

        [JsonProperty("state")] public StateBag State = new();

        public StateBag EnsureState() => State ??= new StateBag();
    }

    /// <summary>
    /// Builds the values of <see cref="EntityRecord.Scene"/>. Centralised because the key is written
    /// into save files: changing how one is spelled orphans every record already stored under it.
    /// </summary>
    public static class SceneKey
    {
        public const string Persistent = "persistent";

        public static string ForChunk(Vector2Int coord) => $"chunk:{coord.x},{coord.y}";

        public static string ForScene(string sceneName) => $"scene:{sceneName}";

        public static bool TryParseChunk(string key, out Vector2Int coord)
        {
            coord = default;
            if (key == null || !key.StartsWith("chunk:", StringComparison.Ordinal)) return false;

            string[] parts = key.Substring("chunk:".Length).Split(',');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out int x)) return false;
            if (!int.TryParse(parts[1], out int y)) return false;

            coord = new Vector2Int(x, y);
            return true;
        }
    }
}
