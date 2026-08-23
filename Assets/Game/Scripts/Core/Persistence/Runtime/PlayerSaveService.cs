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
        /// <summary>
        /// Raised whenever a live player object takes up a profile, whether or not it had saved state.
        ///
        /// "A player exists" and "a player was restored" are different facts, and the world's deferred
        /// pass needs the first. A client joining a saved world for the first time has no record, so
        /// keying the world pass off a successful restore would leave every mount in that world holding
        /// a rider reference it never resolves.
        /// </summary>
        public event System.Action<string, GameObject> PlayerBound;

        public bool Bind(string profileId, GameObject player, bool applyPosition)
        {
            if (string.IsNullOrEmpty(profileId) || player == null) return false;

            boundPlayers[profileId] = player;

            EnsureMomentumSaver(player);

            bool hasRecord = records.TryGetValue(profileId, out PlayerRecord record);

            if (hasRecord)
            {
                if (applyPosition)
                {
                    // Through NetworkedTeleport, not SaveTeleport directly.
                    //
                    // The player's NetworkTransform is OWNER-authoritative. A remote client's body is
                    // written by that client, so a server-side transform write on it is overwritten
                    // within a tick — silently. This line was that write, and the consequence was that
                    // a returning client got their health, hotbar and backpack back (all server state,
                    // all replicated) and spawned at the SpawnPoint anyway. It reads to the player as
                    // "everything came back except where I was", and it is invisible in solo testing
                    // because the host owns its own body and so the direct write happens to work.
                    //
                    // NetworkedTeleport exists for exactly this and is already on the player prefab;
                    // PlayerRespawn uses it and works. It degrades to a direct SaveTeleport.Move when
                    // there is no NetworkObject, no spawn, or no network — so the offline and host
                    // paths are unchanged.
                    NetworkedTeleport.Move(player, record.Position, record.Rotation);
                }

                SaveableEntity entity = player.GetComponent<SaveableEntity>();

                if (entity == null)
                {
                    Debug.LogWarning($"[Save] Player '{player.name}' has no SaveableEntity, so only its " +
                                     "position was restored. Add one to the player prefab.", player);
                }
                else
                {
                    // After the teleport, never before. SaveTeleport zeroes the body on arrival — a
                    // teleported body carrying its old velocity keeps travelling in a direction that no
                    // longer means anything — so a momentum saver running first would have its work
                    // wiped by the placement that follows it.
                    entity.Restore(record.EnsureState());
                    entity.NotifyLoadComplete();
                }
            }

            // Last, and this ordering is load-bearing. The event means "a player is here to be
            // referenced", which is the precondition every deferred world saver waits on — and the
            // first thing they do is re-seat riders. Raised before the restore above, a mount would
            // put this player in its seat and the SaveTeleport a few lines later would drag them back
            // out of it. Raised for a player with no record too: a client joining a saved world for
            // the first time still makes every rider reference in that world resolvable.
            PlayerBound?.Invoke(profileId, player);

            return hasRecord;
        }

        /// <summary>
        /// Gives the player a momentum saver if its prefab does not carry one.
        ///
        /// <b>Why this is here rather than only on the prefab.</b> The player is
        /// <see cref="SaveScope.External"/>, so <c>SaveablePolicy</c> steps over it — deliberately,
        /// since the world store must never capture a player. But that also means the policy's rule
        /// "a non-kinematic Rigidbody has momentum worth keeping" was never applied to the one object
        /// in the game most likely to be moving fast when a player quits. Falling, sprinting or
        /// diving off a cliff, they came back at the right coordinates and completely still.
        ///
        /// Wiring it here rather than requiring a prefab edit follows the same principle as
        /// <c>SaveablePolicy.EnsureScene</c>: forgetting should cost nothing. Adding
        /// <c>RigidbodySaveable</c> to the player prefab makes this a no-op.
        /// </summary>
        private static void EnsureMomentumSaver(GameObject player)
        {
            if (!player.TryGetComponent(out Rigidbody body) || body.isKinematic) return;
            if (player.GetComponent<RigidbodySaveable>() != null) return;

            player.AddComponent<RigidbodySaveable>();

            // The entity caches its saver list on first use, and this player may already have been
            // captured once this session — a rejoin, or a respawn rebinding the same profile.
            if (player.TryGetComponent(out SaveableEntity entity)) entity.InvalidateSavers();
        }

        /// <summary>The live object currently speaking for a profile, if that player is here.</summary>
        public bool TryGetBoundPlayer(string profileId, out GameObject player)
        {
            player = null;
            if (string.IsNullOrEmpty(profileId)) return false;

            return boundPlayers.TryGetValue(profileId, out player) && player != null;
        }

        /// <summary>
        /// The profile a live object speaks for, or false if it is not a player.
        ///
        /// The reverse direction of <see cref="TryGetBoundPlayer"/>, needed by
        /// <see cref="SaveRefBinder"/> to tell "this is the player" from "this is a world entity"
        /// when describing a reference. A linear walk over the bound players is the right shape here:
        /// there is one per connected client, and a second dictionary would be one more thing that
        /// can fall out of step with the first.
        /// </summary>
        public bool TryGetProfileFor(GameObject candidate, out string profileId)
        {
            profileId = null;
            if (candidate == null) return false;

            foreach (KeyValuePair<string, GameObject> entry in boundPlayers)
            {
                if (entry.Value != candidate) continue;

                profileId = entry.Key;
                return true;
            }

            return false;
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
