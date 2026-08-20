using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Holds the world's persisted state and keeps it in step with chunk streaming.
    ///
    /// This is the piece a plain "walk the scene and serialize it" save system does not have, and
    /// cannot work without here. Most of this world is not in memory: <c>WorldStreamer</c> keeps a
    /// few chunks around each player and unloads the rest, so a save that only looked at loaded
    /// scenes would write out a sliver of the map and quietly discard everything a player did
    /// anywhere else.
    ///
    /// The store fixes that by owning one invariant:
    ///
    ///     an object's state is EITHER live in a loaded scene OR recorded here — never neither,
    ///     and never both, where the two would drift apart.
    ///
    /// It is maintained by hooking the same two moments streaming already has:
    ///
    ///   • a chunk is about to unload → <see cref="Dehydrate"/> captures every entity in it into
    ///     the record, and only then does the scene go away;
    ///   • a chunk finished loading → <see cref="Hydrate"/> puts the record back: authored objects
    ///     recorded as destroyed are removed, authored objects get their state, and runtime objects
    ///     are instantiated into that scene.
    ///
    /// So a crate moved in a far-off chunk survives that chunk unloading mid-session, and a save
    /// written afterwards is a save of the whole world rather than of what happened to be resident.
    ///
    /// <b>Records are keyed by identity, not by scene.</b> That is the correction that made authored
    /// objects persist at all. Scene membership is not stable — <c>WorldStreamer</c> moves every
    /// SceneTracked entity into whichever chunk it has wandered into — so a record filed under the
    /// scene an object was captured in could not be found from the scene the object is authored in,
    /// and every creature in the world came back at its authored position. See
    /// <see cref="WorldRecord.Entities"/>.
    ///
    /// Server-only. Clients receive world state through the replication that already exists.
    /// </summary>
    public class WorldSaveStore
    {
        private readonly WorldRecord world;

        /// <summary>Scene keys currently live, so a save knows which scenes to re-read from memory.</summary>
        private readonly Dictionary<string, Scene> loadedScenes = new();

        public WorldSaveStore() : this(new WorldRecord()) { }

        public WorldSaveStore(WorldRecord existing)
        {
            world = existing ?? new WorldRecord();
            world.Normalize();
        }

        public WorldRecord Record => world;

        /// <summary>Raised after a scene's saved contents have been put back, for anything that needs to react.</summary>
        public event Action<string, Scene> OnSceneHydrated;

        // ─────────────────────────────────────────────
        //  Streaming hooks
        // ─────────────────────────────────────────────

        /// <summary>
        /// Restores the recorded state of everything belonging to a scene that has just loaded.
        ///
        /// Order matters and is not arbitrary: qualifying objects are wired first, so the passes
        /// below see the whole scene rather than the part somebody remembered to prepare; destroyed
        /// authored objects go next, so a runtime object spawned where one used to be is not deleted
        /// along with it; authored state after that, while the scene still holds only its authored
        /// population; runtime spawns last, once nothing else will be walking the scene.
        /// </summary>
        public void Hydrate(string sceneKey, Scene scene)
        {
            if (string.IsNullOrEmpty(sceneKey) || !scene.IsValid() || !scene.isLoaded) return;

            loadedScenes[sceneKey] = scene;

            SaveablePolicy.EnsureScene(scene);

            Dictionary<string, SaveableEntity> authored = CollectAuthored(scene);

            RemoveDestroyed(authored);
            RestoreAuthored(authored);
            SpawnEntities(sceneKey, scene);

            OnSceneHydrated?.Invoke(sceneKey, scene);
        }

        /// <summary>
        /// Captures a scene's live state into the record. Call before the scene unloads, and again
        /// for every loaded scene when writing a save.
        ///
        /// Each entity's record is overwritten in place rather than the scene's records being
        /// rebuilt wholesale: an entity may have wandered out of this scene into another one, and
        /// clearing by scene would throw away a record that another scene is about to re-stamp — or
        /// has already stamped this same pass.
        /// </summary>
        public void Dehydrate(string sceneKey, Scene scene)
        {
            if (string.IsNullOrEmpty(sceneKey) || !scene.IsValid() || !scene.isLoaded) return;

            // No EnsureScene here on purpose. Authored objects only arrive when their scene loads,
            // which Hydrate already covers, and runtime spawns get their identity from
            // SaveableEntity.EnsureRuntime — so a second full-scene walk on every chunk unload and
            // every save would cost the same as the capture itself and find nothing.
            var seen = new HashSet<string>();

            foreach (SaveableEntity entity in EntitiesIn(scene))
            {
                // Someone else's record. Players sit in the persistent scene and would otherwise be
                // captured here as well as by PlayerSaveService — and then re-instantiated from
                // their prefab on load, beside the player Netcode spawns.
                if (!entity.BelongsToWorld) continue;
                if (string.IsNullOrEmpty(entity.InstanceId)) continue;

                seen.Add(entity.InstanceId);
                CaptureEntity(entity, sceneKey);
            }

            DropVanishedRuntime(sceneKey, seen);
        }

        /// <summary>
        /// Forgets runtime objects that were in this scene and are not there any more.
        ///
        /// A runtime object leaves the record by being destroyed, and this is the only place that
        /// can tell: it is gone from the scene it was recorded in, and it is not alive anywhere else
        /// either — the second half is what stops an entity that merely migrated to another chunk
        /// from being deleted on the way past.
        ///
        /// Authored objects are never dropped here. One is missing from its scene whenever that
        /// scene is not loaded, which is most of the time on a streamed world; the only thing that
        /// removes an authored record is an explicit tombstone from <see cref="RecordDestroyed"/>.
        /// </summary>
        private void DropVanishedRuntime(string sceneKey, HashSet<string> seen)
        {
            List<string> doomed = null;

            foreach (KeyValuePair<string, EntityRecord> entry in world.Entities)
            {
                EntityRecord record = entry.Value;
                if (record == null || record.Authored) continue;
                if (record.Scene != sceneKey || seen.Contains(entry.Key)) continue;

                // Alive somewhere else — it migrated rather than died. The null check matters: a
                // destroyed object leaves a null behind if its OnDisable never ran.
                if (SaveableEntity.LiveEntities.TryGetValue(entry.Key, out SaveableEntity live) && live != null)
                    continue;

                (doomed ??= new List<string>()).Add(entry.Key);
            }

            if (doomed == null) return;

            foreach (string id in doomed)
                world.Entities.Remove(id);
        }

        /// <summary>Refreshes every loaded scene's records. The first half of writing a save.</summary>
        public void DehydrateLoaded()
        {
            // Copied, because Dehydrate can drop a scene that has since been unloaded without the
            // store hearing about it, which mutates the dictionary being walked.
            foreach (KeyValuePair<string, Scene> entry in new List<KeyValuePair<string, Scene>>(loadedScenes))
            {
                if (!entry.Value.IsValid() || !entry.Value.isLoaded)
                {
                    loadedScenes.Remove(entry.Key);
                    continue;
                }

                Dehydrate(entry.Key, entry.Value);
            }
        }

        /// <summary>Forgets that a scene is loaded. Call after its unload has completed.</summary>
        public void ForgetLoaded(string sceneKey) => loadedScenes.Remove(sceneKey);

        /// <summary>
        /// Records that an authored object was destroyed, so the scene file stops putting it back.
        ///
        /// Only authored objects need this. A runtime object simply stops being captured once it no
        /// longer exists, whereas an authored one is re-created by the scene file on every load and
        /// an explicit tombstone is the only thing that can override that.
        ///
        /// Takes no scene: the tombstone is global, because the object may well have been killed in
        /// a chunk it wandered into rather than the one it was authored in.
        /// </summary>
        public void RecordDestroyed(SaveableEntity entity)
        {
            if (entity == null || !entity.IsAuthored || string.IsNullOrEmpty(entity.InstanceId)) return;

            if (!world.Destroyed.Contains(entity.InstanceId))
                world.Destroyed.Add(entity.InstanceId);

            world.Entities.Remove(entity.InstanceId);
        }

        // ─────────────────────────────────────────────
        //  Capture / restore
        // ─────────────────────────────────────────────

        /// <summary>
        /// Writes one entity into the record, creating it on first sight and overwriting it after.
        ///
        /// The pose is stored on the record itself rather than left to a <c>TransformSaveable</c>,
        /// because where a thing is is the one piece of state every world object has. A runtime
        /// object needs it before it exists — the position is what it is spawned at — and an
        /// authored object needs it because the scene file will otherwise put it back where it was
        /// authored. Storing it once, here, means position persists for every saveable object
        /// whether or not anyone remembered to add the saver.
        /// </summary>
        private void CaptureEntity(SaveableEntity entity, string sceneKey)
        {
            EntityRecord record = world.GetOrCreate(entity.InstanceId);

            record.PrefabId = entity.PrefabId;
            record.Scene = sceneKey;
            record.Authored = entity.IsAuthored;
            record.Position = entity.transform.position;
            record.Rotation = entity.transform.rotation;
            record.Scale = entity.transform.localScale;

            // A fresh bag rather than the existing one: a saver removed since the last capture must
            // drop out of the record, and merging into the old bag would preserve it forever.
            var bag = new StateBag();
            entity.Capture(bag);
            record.State = bag;
        }

        private void SpawnEntities(string sceneKey, Scene scene)
        {
            // Materialised first: SpawnIfNetworked and the entity's own Awake can both touch the
            // store, and mutating the dictionary being walked would throw.
            var pending = new List<EntityRecord>();

            foreach (EntityRecord record in world.InScene(sceneKey))
            {
                if (record.Authored) continue;

                // Already live — the object migrated out of this chunk and back, or the scene was
                // hydrated twice. Re-instantiating would double it.
                if (!string.IsNullOrEmpty(record.InstanceId) &&
                    SaveableEntity.LiveEntities.TryGetValue(record.InstanceId, out SaveableEntity existing) &&
                    existing != null)
                {
                    continue;
                }

                pending.Add(record);
            }

            foreach (EntityRecord record in pending)
            {
                if (!SaveablePrefabRegistry.TryGet(record.PrefabId, out GameObject prefab))
                {
                    Debug.LogWarning($"[Save] No prefab registered for id '{record.PrefabId}' — one " +
                                     "saved object could not be restored. Is it missing from " +
                                     $"Resources/{SaveablePrefabRegistry.ResourcesFolder}?");
                    continue;
                }

                GameObject instance = UnityEngine.Object.Instantiate(prefab, record.Position, record.Rotation);
                if (record.Scale != Vector3.zero) instance.transform.localScale = record.Scale;

                // Into the chunk's own scene, not the active one. Left in the active scene it would
                // survive that chunk unloading and pile up a duplicate on every reload.
                if (scene.IsValid() && scene.isLoaded)
                    SceneManager.MoveGameObjectToScene(instance, scene);

                // Savers before identity and state, and the order is the whole point: Restore hands
                // each payload to the saver that owns its key, so a saver added afterwards is handed
                // nothing. A restored mount would come back riderless not because the record was
                // missing the rider, but because MountSaveable did not exist at the moment the record
                // was read. The spawn path adds these too — this covers a prefab that gained a saver
                // since the save was written.
                SaveablePolicy.EnsureSpawned(instance);

                SaveableEntity saveable = SaveableEntity.EnsureRuntime(instance, record.PrefabId);
                saveable.AdoptIdentity(record.PrefabId, record.InstanceId);
                saveable.Restore(record.State);

                SaveNetworking.SpawnIfNetworked(instance);
            }
        }

        /// <summary>
        /// Deletes the authored objects this scene just re-created that the player had destroyed.
        /// </summary>
        private void RemoveDestroyed(Dictionary<string, SaveableEntity> authored)
        {
            foreach (string instanceId in world.Destroyed)
            {
                if (!authored.TryGetValue(instanceId, out SaveableEntity entity) || entity == null) continue;

                SaveNetworking.DespawnAndDestroy(entity.gameObject);
                authored.Remove(instanceId);
            }
        }

        /// <summary>
        /// Puts the recorded state back onto the authored objects this scene contains.
        ///
        /// Driven from the objects present rather than from the records — the reverse of how this
        /// worked when records were filed per scene. That inversion is the fix: a record no longer
        /// has to know which scene its object will turn up in, so an object that spent last session
        /// three chunks away is still matched, and a record whose scene is simply not loaded yet
        /// waits quietly instead of being reported as missing.
        /// </summary>
        private void RestoreAuthored(Dictionary<string, SaveableEntity> authored)
        {
            foreach (KeyValuePair<string, SaveableEntity> entry in authored)
            {
                if (!world.TryGet(entry.Key, out EntityRecord record)) continue;
                if (!record.Authored) continue;

                SaveableEntity entity = entry.Value;

                // Pose before state, so a TransformSaveable in the bag — which holds the same values
                // — is what lands last and nothing depends on which of the two wins.
                if (record.HasPose)
                {
                    SaveTeleport.Move(entity.gameObject, record.Position, record.Rotation,
                                      zeroVelocity: entity.GetComponent<RigidbodySaveable>() == null);

                    if (record.Scale != Vector3.zero) entity.transform.localScale = record.Scale;
                }

                entity.Restore(record.State);
            }
        }

        // ─────────────────────────────────────────────
        //  Scene walking
        // ─────────────────────────────────────────────

        /// <summary>
        /// Every saveable entity in a scene, including inactive ones — a disabled object is still
        /// part of the world and its state still has to survive.
        /// </summary>
        public static IEnumerable<SaveableEntity> EntitiesIn(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SaveableEntity entity in root.GetComponentsInChildren<SaveableEntity>(true))
                {
                    if (entity != null) yield return entity;
                }
            }
        }

        private static Dictionary<string, SaveableEntity> CollectAuthored(Scene scene)
        {
            var authored = new Dictionary<string, SaveableEntity>();

            foreach (SaveableEntity entity in EntitiesIn(scene))
            {
                if (!entity.BelongsToWorld) continue;
                if (!entity.IsAuthored || string.IsNullOrEmpty(entity.InstanceId)) continue;
                authored[entity.InstanceId] = entity;
            }

            return authored;
        }
    }
}
