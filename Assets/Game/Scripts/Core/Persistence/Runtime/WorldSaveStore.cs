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
    /// Server-only. Clients receive world state through the replication that already exists.
    /// </summary>
    public class WorldSaveStore
    {
        private readonly WorldRecord world;

        /// <summary>Scene keys currently live, so a save knows which records to refresh from memory.</summary>
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
        /// Restores <paramref name="sceneKey"/>'s recorded state into a scene that has just loaded.
        ///
        /// Order matters and is not arbitrary: destroyed authored objects go first, so a runtime
        /// object standing where one used to be is not deleted along with it; authored state next,
        /// while the scene still holds only its authored population; runtime spawns last, so the
        /// entity walk that follows cannot see them and is not asked to match them to an authored
        /// record they do not have.
        /// </summary>
        public void Hydrate(string sceneKey, Scene scene)
        {
            if (string.IsNullOrEmpty(sceneKey) || !scene.IsValid() || !scene.isLoaded) return;

            loadedScenes[sceneKey] = scene;

            if (!world.TryGet(sceneKey, out SceneRecord record))
            {
                OnSceneHydrated?.Invoke(sceneKey, scene);
                return;
            }

            Dictionary<string, SaveableEntity> authored = CollectAuthored(scene);

            RemoveDestroyed(record, authored);
            RestoreAuthored(record, authored);
            SpawnEntities(record, scene);

            OnSceneHydrated?.Invoke(sceneKey, scene);
        }

        /// <summary>
        /// Captures a scene's live state into its record. Call before the scene unloads, and again
        /// for every loaded scene when writing a save.
        ///
        /// The record is rebuilt rather than merged: an entity that was destroyed since the last
        /// capture has to disappear from the record too, and a merge would keep resurrecting it.
        /// </summary>
        public void Dehydrate(string sceneKey, Scene scene)
        {
            if (string.IsNullOrEmpty(sceneKey) || !scene.IsValid() || !scene.isLoaded) return;

            SceneRecord record = world.GetOrCreate(sceneKey);

            var previouslyDestroyed = new List<string>(record.DestroyedAuthored);

            record.Entities.Clear();
            record.Authored.Clear();
            record.DestroyedAuthored.Clear();

            // An authored object destroyed in an earlier session is not in this scene to be seen
            // now — the record is the only thing that remembers it, so that memory carries forward.
            record.DestroyedAuthored.AddRange(previouslyDestroyed);

            var seenAuthored = new HashSet<string>();

            foreach (SaveableEntity entity in EntitiesIn(scene))
            {
                // Someone else's record. Players sit in the persistent scene and would otherwise be
                // captured here as well as by PlayerSaveService — and then re-instantiated from
                // their prefab on load, beside the player Netcode spawns.
                if (!entity.BelongsToWorld) continue;

                if (entity.IsAuthored)
                {
                    if (string.IsNullOrEmpty(entity.InstanceId)) continue;

                    seenAuthored.Add(entity.InstanceId);

                    // Authored objects are already in the scene file, so only the delta is stored.
                    // An object whose savers all return nothing has no delta and needs no record.
                    var bag = new StateBag();
                    entity.Capture(bag);
                    if (bag.Count > 0) record.Authored[entity.InstanceId] = bag;

                    continue;
                }

                record.Entities.Add(CaptureEntity(entity));
            }

            // An authored object recorded as destroyed that IS present again — because the record
            // predates a scene edit that re-added it — would otherwise stay marked forever and be
            // deleted on every subsequent load.
            record.DestroyedAuthored.RemoveAll(seenAuthored.Contains);
        }

        /// <summary>
        /// Drops records that describe nothing, and reports how many went.
        ///
        /// Without this the store keeps a record for every chunk the player has ever set foot in,
        /// for the rest of the session and in every save file after it — most of them empty, because
        /// walking across a chunk creates its record whether or not anything in it changed. On a
        /// 48-chunk world that is merely untidy; it is the kind of untidy that turns into a
        /// megabyte of nothing on a world several times the size.
        ///
        /// A record holding only tombstones is NOT empty: that a crate was destroyed is exactly the
        /// state the scene file would otherwise undo.
        /// </summary>
        public int PruneEmpty()
        {
            if (world.Scenes == null) return 0;

            var doomed = new List<string>();

            foreach (KeyValuePair<string, SceneRecord> entry in world.Scenes)
            {
                if (entry.Value == null || entry.Value.IsEmpty) doomed.Add(entry.Key);
            }

            foreach (string key in doomed)
                world.Scenes.Remove(key);

            return doomed.Count;
        }

        /// <summary>Refreshes every loaded scene's record. The first half of writing a save.</summary>
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
        /// The record key for a live scene, so a caller holding only a GameObject can find out which
        /// record it belongs to. False for a scene the store was never told about — a menu, an
        /// arena, anything outside the streamed world.
        /// </summary>
        public bool TryGetSceneKey(Scene scene, out string sceneKey)
        {
            foreach (KeyValuePair<string, Scene> entry in loadedScenes)
            {
                if (entry.Value != scene) continue;
                sceneKey = entry.Key;
                return true;
            }

            sceneKey = null;
            return false;
        }

        /// <summary>
        /// Records that an authored object was destroyed, so the chunk scene stops putting it back.
        ///
        /// Only authored objects need this. A runtime object simply stops being captured once it no
        /// longer exists, whereas an authored one is re-created by the scene file on every load and
        /// an explicit tombstone is the only thing that can override that.
        /// </summary>
        public void RecordDestroyed(string sceneKey, SaveableEntity entity)
        {
            if (entity == null || !entity.IsAuthored || string.IsNullOrEmpty(entity.InstanceId)) return;

            SceneRecord record = world.GetOrCreate(sceneKey);

            if (!record.DestroyedAuthored.Contains(entity.InstanceId))
                record.DestroyedAuthored.Add(entity.InstanceId);

            record.Authored.Remove(entity.InstanceId);
        }

        // ─────────────────────────────────────────────
        //  Capture / restore
        // ─────────────────────────────────────────────

        private static EntityRecord CaptureEntity(SaveableEntity entity)
        {
            var record = new EntityRecord
            {
                PrefabId = entity.PrefabId,
                InstanceId = entity.InstanceId,
                Position = entity.transform.position,
                Rotation = entity.transform.rotation,
            };

            entity.Capture(record.EnsureState());
            return record;
        }

        private void SpawnEntities(SceneRecord record, Scene scene)
        {
            foreach (EntityRecord entity in record.Entities)
            {
                if (entity == null) continue;

                // Already live — the object migrated out of this chunk and back, or the scene was
                // hydrated twice. Re-instantiating would double it.
                if (!string.IsNullOrEmpty(entity.InstanceId) &&
                    SaveableEntity.LiveEntities.TryGetValue(entity.InstanceId, out SaveableEntity existing) &&
                    existing != null)
                {
                    continue;
                }

                if (!SaveablePrefabRegistry.TryGet(entity.PrefabId, out GameObject prefab))
                {
                    Debug.LogWarning($"[Save] No prefab registered for id '{entity.PrefabId}' — one " +
                                     "saved object could not be restored. Is it missing from " +
                                     $"Resources/{SaveablePrefabRegistry.ResourcesFolder}?");
                    continue;
                }

                GameObject instance = UnityEngine.Object.Instantiate(prefab, entity.Position, entity.Rotation);

                // Into the chunk's own scene, not the active one. Left in the active scene it would
                // survive that chunk unloading and pile up a duplicate on every reload.
                if (scene.IsValid() && scene.isLoaded)
                    SceneManager.MoveGameObjectToScene(instance, scene);

                SaveableEntity saveable = SaveableEntity.EnsureRuntime(instance, entity.PrefabId);
                saveable.AdoptIdentity(entity.PrefabId, entity.InstanceId);
                saveable.Restore(entity.State);

                SaveNetworking.SpawnIfNetworked(instance);
            }
        }

        private static void RemoveDestroyed(SceneRecord record, Dictionary<string, SaveableEntity> authored)
        {
            foreach (string instanceId in record.DestroyedAuthored)
            {
                if (!authored.TryGetValue(instanceId, out SaveableEntity entity) || entity == null) continue;

                SaveNetworking.DespawnAndDestroy(entity.gameObject);
                authored.Remove(instanceId);
            }
        }

        private static void RestoreAuthored(SceneRecord record, Dictionary<string, SaveableEntity> authored)
        {
            foreach (KeyValuePair<string, StateBag> entry in record.Authored)
            {
                if (!authored.TryGetValue(entry.Key, out SaveableEntity entity) || entity == null)
                {
                    // The scene no longer contains an object with this id — usually because the
                    // chunk scene was edited since the save. Dropping the record silently would
                    // hide a real content change, so it is reported and skipped.
                    Debug.LogWarning($"[Save] Saved state for authored object '{entry.Key}' has no " +
                                     "matching object in the scene. Was the scene edited since the save?");
                    continue;
                }

                entity.Restore(entry.Value);
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
