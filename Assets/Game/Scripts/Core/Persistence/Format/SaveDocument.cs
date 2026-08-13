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
        public const int CurrentVersion = 1;

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
        /// Keyed by <see cref="SceneKey"/>, not by scene name alone, so a chunk coordinate, an
        /// interior scene and the persistent scene can share one dictionary without colliding.
        /// </summary>
        [JsonProperty("scenes")] public Dictionary<string, SceneRecord> Scenes = new();

        public void Normalize()
        {
            Global ??= new StateBag();
            Scenes ??= new Dictionary<string, SceneRecord>();

            foreach (SceneRecord record in Scenes.Values)
                record?.Normalize();
        }

        /// <summary>The record for a key, created empty on first use.</summary>
        public SceneRecord GetOrCreate(string sceneKey)
        {
            Scenes ??= new Dictionary<string, SceneRecord>();

            if (!Scenes.TryGetValue(sceneKey, out SceneRecord record) || record == null)
            {
                record = new SceneRecord();
                Scenes[sceneKey] = record;
            }

            return record.Normalize();
        }

        public bool TryGet(string sceneKey, out SceneRecord record)
        {
            record = null;
            if (Scenes == null || sceneKey == null) return false;
            if (!Scenes.TryGetValue(sceneKey, out record) || record == null) return false;

            record.Normalize();
            return true;
        }
    }

    /// <summary>
    /// One scene's worth of persisted world state.
    ///
    /// The three collections exist because a scene holds two populations that behave differently.
    /// Runtime objects (<see cref="Entities"/>) do not exist until something spawns them, so their
    /// record carries everything needed to recreate them. Authored objects are already in the scene
    /// file when it loads, so their record carries only the delta — the state they were left in
    /// (<see cref="Authored"/>), or the fact that they are gone (<see cref="DestroyedAuthored"/>).
    /// Recreating an authored object from a record would duplicate it; recording an authored object
    /// as destroyed is the only way to keep the scene file from putting it back.
    /// </summary>
    public class SceneRecord
    {
        [JsonProperty("entities")] public List<EntityRecord> Entities = new();
        [JsonProperty("authored")] public Dictionary<string, StateBag> Authored = new();
        [JsonProperty("destroyedAuthored")] public List<string> DestroyedAuthored = new();

        [JsonIgnore]
        public bool IsEmpty =>
            (Entities == null || Entities.Count == 0) &&
            (Authored == null || Authored.Count == 0) &&
            (DestroyedAuthored == null || DestroyedAuthored.Count == 0);

        public SceneRecord Normalize()
        {
            Entities ??= new List<EntityRecord>();
            Authored ??= new Dictionary<string, StateBag>();
            DestroyedAuthored ??= new List<string>();
            return this;
        }
    }

    /// <summary>A runtime-spawned object: what to instantiate, where to put it, and how to set it up.</summary>
    public class EntityRecord
    {
        /// <summary>Asset GUID of the prefab, resolved through the saveable-prefab registry.</summary>
        [JsonProperty("prefabId")] public string PrefabId = string.Empty;

        /// <summary>Identity of this particular instance, stable across save/load.</summary>
        [JsonProperty("instanceId")] public string InstanceId = string.Empty;

        [JsonProperty("position")] public Vector3 Position;
        [JsonProperty("rotation")] public Quaternion Rotation = Quaternion.identity;
        [JsonProperty("state")] public StateBag State = new();

        public StateBag EnsureState() => State ??= new StateBag();
    }

    /// <summary>
    /// Builds the keys of <see cref="WorldRecord.Scenes"/>. Centralised because the key is written
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
