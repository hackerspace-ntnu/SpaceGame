using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Server-side book-keeping for per-player saved state: which profile owns which record, and
    /// which live GameObject is currently speaking for it.
    ///
    /// Kept apart from <see cref="WorldSaveStore"/> because players are not world objects. They are
    /// not in a chunk (a chunk unload must never take a player's state with it), they are not
    /// re-instantiated from a prefab id on load (Netcode spawns them, at a time this system does not
    /// control), and they are matched by profile rather than by instance.
    /// </summary>
    public class PlayerSaveService
    {
        private readonly Dictionary<string, PlayerRecord> records = new();

        /// <summary>Profile id -> the live player object currently bound to it.</summary>
        private readonly Dictionary<string, GameObject> boundPlayers = new();

        public PlayerSaveService() { }

        public PlayerSaveService(IEnumerable<PlayerRecord> existing)
        {
            if (existing == null) return;

            foreach (PlayerRecord record in existing)
            {
                if (record == null || string.IsNullOrEmpty(record.ProfileId)) continue;
                records[record.ProfileId] = record;
            }
        }

        public int RecordCount => records.Count;

        public bool TryGetRecord(string profileId, out PlayerRecord record)
        {
            record = null;
            return !string.IsNullOrEmpty(profileId) && records.TryGetValue(profileId, out record);
        }

        /// <summary>
        /// Where a returning player should be spawned. Answered before the player object exists,
        /// because the spawn position also decides which chunks the world has to preload — a player
        /// spawned into a chunk that was never loaded falls through the floor.
        /// </summary>
        public bool TryGetSpawnPosition(string profileId, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!TryGetRecord(profileId, out PlayerRecord record)) return false;

            position = record.Position;
            rotation = record.Rotation;
            return true;
        }

        /// <summary>
        /// Binds a live player object to a profile and applies its saved state, if any.
        ///
        /// Returns true when saved state was applied, false for a profile seen for the first time —
        /// which is not a failure, just a new player who keeps whatever the prefab gave them.
        /// </summary>
        public bool Bind(string profileId, GameObject player, bool applyPosition)
        {
            if (string.IsNullOrEmpty(profileId) || player == null) return false;

            boundPlayers[profileId] = player;

            if (!records.TryGetValue(profileId, out PlayerRecord record)) return false;

            if (applyPosition)
                SaveTeleport.Move(player, record.Position, record.Rotation);

            SaveableEntity entity = player.GetComponent<SaveableEntity>();

            if (entity == null)
            {
                Debug.LogWarning($"[Save] Player '{player.name}' has no SaveableEntity, so only its " +
                                 "position was restored. Add one to the player prefab.", player);
                return true;
            }

            entity.Restore(record.EnsureState());
            entity.NotifyLoadComplete();
            return true;
        }

        public void Unbind(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;

            // Captured on the way out so a player who disconnects mid-session is still in the next
            // save with the state they left in, rather than the state they joined with.
            CaptureOne(profileId);
            boundPlayers.Remove(profileId);
        }

        /// <summary>Refreshes every bound player's record from the live object. The first half of writing a save.</summary>
        public void CaptureAll()
        {
            foreach (string profileId in new List<string>(boundPlayers.Keys))
                CaptureOne(profileId);
        }

        /// <summary>The records to write out, including players not currently connected.</summary>
        public List<PlayerRecord> Snapshot() => new(records.Values);

        private void CaptureOne(string profileId)
        {
            if (!boundPlayers.TryGetValue(profileId, out GameObject player) || player == null)
            {
                // The object is gone but the record is not — a player who died and is mid-respawn,
                // or one who disconnected. Their last known state stays exactly as it was.
                boundPlayers.Remove(profileId);
                return;
            }

            if (!records.TryGetValue(profileId, out PlayerRecord record) || record == null)
            {
                record = new PlayerRecord { ProfileId = profileId };
                records[profileId] = record;
            }

            record.Position = player.transform.position;
            record.Rotation = player.transform.rotation;

            SaveableEntity entity = player.GetComponent<SaveableEntity>();
            if (entity == null) return;

            // A fresh bag rather than the existing one: a saver removed since the last capture must
            // drop out of the record, and merging into the old bag would preserve it forever.
            var bag = new StateBag();
            entity.Capture(bag);
            record.State = bag;
        }
    }
}
