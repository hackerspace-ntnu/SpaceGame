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

        /// <summary>
        /// Runtime records whose <c>prefabId</c> could not be resolved on the last attempt.
        ///
        /// They are absent from the world for now, which is indistinguishable from destroyed as far
        /// as <see cref="DropVanishedRuntime"/> can tell — so without this set it deleted them, and
        /// a save file that merely named a prefab nobody had registered became a save file with the
        /// object permanently missing. Membership is cleared the moment the id resolves.
        /// </summary>
        private readonly HashSet<string> unresolved = new();

        /// <summary>How many records are being held back because their prefab could not be found.</summary>
        public int UnresolvedCount => unresolved.Count;

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

                // Tombstoned this frame and not yet actually destroyed. Capturing it would undo the
                // burial — see SaveableEntity.IsBuried. Not added to `seen` either: a runtime object
                // on its way out SHOULD be dropped from the record by DropVanishedRuntime.
                if (entity.IsBuried) continue;

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

                // Failed to spawn rather than ceased to exist. Deleting it here is how a missing
                // prefab registration became irreversible — see SpawnEntities.
                if (unresolved.Contains(entry.Key)) continue;

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

        /// <summary>
        /// Removes records that can no longer refer to anything, and returns how many went.
        ///
        /// <b>Nothing in this system ever removed a record before.</b> Authored records are
        /// deliberately never dropped (a scene that is not loaded looks exactly like a scene whose
        /// objects are gone), tombstones had no removal path at all, and unresolvable runtime
        /// records were the one thing that WAS dropped — the only case where dropping was wrong. So
        /// a save file only ever grew, for the life of the world.
        ///
        /// This is deliberately conservative. It deletes only the two shapes that are provably
        /// meaningless, and reports rather than guessing about the rest:
        ///
        ///   • a runtime record with an EMPTY prefabId. Not "one we could not resolve" — that is
        ///     held, see <see cref="unresolved"/> — but one that names nothing at all, so no future
        ///     wiring can ever bring it back. These are the residue of objects spawned before their
        ///     prefab carried a stamped id;
        ///   • a record for an id that is also tombstoned. The two contradict each other, and the
        ///     tombstone is the later statement. A pair like this is the fingerprint of the
        ///     deferred-destroy race that <see cref="SaveableEntity.IsBuried"/> now closes.
        ///
        /// Authored records for objects a designer has since deleted from a scene are NOT removed:
        /// this store cannot tell them apart from records for chunks that simply are not loaded, and
        /// guessing wrong deletes a player's progress. They are counted instead, so the growth is at
        /// least visible.
        /// </summary>
        public int Compact()
        {
            List<string> doomed = null;
            int emptyPrefab = 0;
            int contradicted = 0;

            foreach (KeyValuePair<string, EntityRecord> entry in world.Entities)
            {
                EntityRecord record = entry.Value;
                if (record == null) { (doomed ??= new List<string>()).Add(entry.Key); continue; }

                if (world.IsDestroyed(entry.Key))
                {
                    (doomed ??= new List<string>()).Add(entry.Key);
                    contradicted++;
                    continue;
                }

                if (!record.Authored && string.IsNullOrEmpty(record.PrefabId))
                {
                    (doomed ??= new List<string>()).Add(entry.Key);
                    emptyPrefab++;
                }
            }

            if (doomed == null) return 0;

            foreach (string id in doomed)
            {
                world.Entities.Remove(id);
                unresolved.Remove(id);
            }

            if (emptyPrefab > 0)
                Debug.LogWarning($"[Save] Dropped {emptyPrefab} runtime record(s) that named no prefab " +
                                 "at all and could never have been restored. This is what an object " +
                                 "spawned from an unstamped prefab leaves behind — run Tools ▸ Save " +
                                 "System ▸ Wire Saveable Prefabs so new spawns carry an id.");

            if (contradicted > 0)
                Debug.LogWarning($"[Save] Dropped {contradicted} record(s) for objects that are also " +
                                 "tombstoned. A record and a tombstone for one id contradict each " +
                                 "other; the tombstone wins.");

            return doomed.Count;
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

            world.MarkDestroyed(entity.InstanceId);
            world.Entities.Remove(entity.InstanceId);

            // Object.Destroy is deferred to the end of the frame, so between this call and the
            // object actually going away it is still in its scene and still in LiveEntities. A
            // Dehydrate landing in that window — a save taken right after a chunk load, which
            // CaptureLoadedScenes does on every single save — walked straight past the tombstone and
            // re-created the record this line just removed. That record is Authored, and authored
            // records are never dropped by DropVanishedRuntime, so it became a permanent orphan
            // describing an object that no longer exists.
            entity.MarkBuried();
        }

        /// <summary>
        /// Lifts a tombstone, so an authored object may exist again.
        ///
        /// Needed because tombstones are otherwise permanent and keyed by an identity that can be
        /// re-derived: <see cref="SaveableEntity.DeriveAuthoredId"/> includes sibling index, so
        /// deleting one prop from a chunk scene shifts a different object into the dead one's id and
        /// <see cref="RemoveDestroyed"/> would delete it on every load, silently. The compaction
        /// pass calls this for tombstones whose object is demonstrably a different one.
        /// </summary>
        public bool ForgetDestroyed(string instanceId) => world.ClearDestroyed(instanceId);

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
            record.HasScale = true;

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
                    // Marked, not merely skipped. DropVanishedRuntime deletes any runtime record for
                    // this scene that it cannot see alive — and a record that failed to spawn is by
                    // definition not alive, so the next chunk unload used to erase it. That turned a
                    // recoverable wiring problem into permanent data loss: fix the wiring afterwards
                    // and the objects were still gone, because the records naming them had been
                    // thrown away one unload later.
                    unresolved.Add(record.InstanceId);

                    Debug.LogWarning($"[Save] No prefab registered for id '{record.PrefabId}' — one " +
                                     "saved object could not be restored, and its record is being " +
                                     "KEPT so it can come back once the prefab is reachable. Register " +
                                     "it with NetworkManager, put it under " +
                                     $"Resources/{SaveablePrefabRegistry.ResourcesFolder}, or run " +
                                     "Tools ▸ Save System ▸ Wire Saveable Prefabs to stamp its id.");
                    continue;
                }

                // It resolved, so any earlier failure for this id is history.
                unresolved.Remove(record.InstanceId);

                // HasPose is honoured for runtime records too, not only authored ones. It used to be
                // read nowhere on this path, so a record that genuinely did not know where its object
                // belonged — the only source is a v1 file whose runtime entry had no position — was
                // instantiated at the world origin, i.e. under the terrain, several kilometres from
                // anything. Holding the record is the better answer: nothing is lost, and the object
                // is not silently relocated to a place it has never been.
                if (!record.HasPose)
                {
                    unresolved.Add(record.InstanceId);
                    Debug.LogWarning($"[Save] Record '{record.InstanceId}' names a runtime object with " +
                                     "no saved position, so there is nowhere to put it. Keeping the " +
                                     "record rather than spawning it at the world origin.");
                    continue;
                }

                GameObject instance = UnityEngine.Object.Instantiate(prefab, record.Position, record.Rotation);
                if (record.HasScale) instance.transform.localScale = record.Scale;

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
            // Driven from the objects this scene actually has, not from the tombstone list. The list
            // only ever grows — one entry per authored object ever destroyed, for the life of the
            // world — so walking it per hydrate made chunk loading cost more the longer the save had
            // been played. The scene's authored population is bounded; the graveyard is not.
            List<string> doomed = null;

            foreach (KeyValuePair<string, SaveableEntity> entry in authored)
            {
                if (entry.Value == null || !world.IsDestroyed(entry.Key)) continue;
                (doomed ??= new List<string>()).Add(entry.Key);
            }

            if (doomed == null) return;

            foreach (string instanceId in doomed)
            {
                SaveableEntity entity = authored[instanceId];
                authored.Remove(instanceId);

                if (entity != null)
                {
                    entity.MarkBuried();
                    SaveNetworking.DespawnAndDestroy(entity.gameObject);
                }
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

                    if (record.HasScale) entity.transform.localScale = record.Scale;
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
