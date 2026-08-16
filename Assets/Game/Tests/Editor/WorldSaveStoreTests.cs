using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The piece that reconciles saving with chunk streaming.
    ///
    /// Every test here is a scenario the game produces on its own — a chunk unloading while the
    /// player walks away, and loading again when they come back — but which is slow and unreliable
    /// to reproduce by playing, because it depends on where the player stands and how long they
    /// stay away.
    /// </summary>
    public class WorldSaveStoreTests
    {
        private const string ChunkKey = "chunk:3,2";
        private const string PersistentKey = SceneKey.Persistent;

        private Scene scene;
        private GameObject prefab;
        private readonly List<GameObject> spawned = new();

        // A preview scene, not an additive one. Both are real loaded Scenes that report roots and
        // accept SceneManager.MoveGameObjectToScene — which is what the store needs — but a preview
        // scene is invisible to the editor's scene setup. An additive scene is not: opening one
        // rearranges whatever the person at the editor had open, and it refuses outright ("cannot
        // create a new scene additively with an untitled scene unsaved") whenever they have unsaved
        // work, which made this fixture fail for reasons that had nothing to do with saving.

        private class CounterSaver : MonoBehaviour, ISaveable
        {
            public int Value;
            public string SaveKey => "counter";
            public object CaptureState() => new Payload { value = Value };

            public void RestoreState(JObject state)
            {
                if (state?["value"] is { } v) Value = v.Value<int>();
            }

            public struct Payload { public int value; }
        }

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();

            // Not a prefab asset — Instantiate is happy with any GameObject, and a registry entry is
            // just "the thing to clone", so the test needs no asset on disk.
            prefab = new GameObject("SavedThingPrefab");
            prefab.AddComponent<CounterSaver>();
            prefab.SetActive(false);

            SaveablePrefabRegistry.Clear();
            SaveablePrefabRegistry.Register("prefab-a", prefab);
        }

        [TearDown]
        public void TearDown()
        {
            SaveablePrefabRegistry.Clear();

            foreach (GameObject go in spawned.Where(g => g != null))
                Object.DestroyImmediate(go);
            spawned.Clear();

            if (prefab != null) Object.DestroyImmediate(prefab);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        /// <summary>Creates an object inside the test scene so the store's scene walk can find it.</summary>
        private GameObject InScene(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            spawned.Add(go);
            return go;
        }

        private GameObject RuntimeEntity(string name, string prefabId, Vector3 position, int counter)
        {
            GameObject go = InScene(name);
            go.transform.position = position;
            go.AddComponent<CounterSaver>().Value = counter;
            SaveableEntity.EnsureRuntime(go, prefabId);
            return go;
        }

        // ─────────────────────────────────────────────
        //  Runtime objects
        // ─────────────────────────────────────────────

        /// <summary>
        /// The core loop: a chunk unloads, its contents are captured, the chunk loads again and the
        /// contents come back — position, state and all.
        /// </summary>
        [Test]
        public void DehydrateThenHydrate_RestoresARuntimeEntity()
        {
            var store = new WorldSaveStore();

            GameObject original = RuntimeEntity("crate", "prefab-a", new Vector3(5f, 1f, -3f), 42);
            string instanceId = original.GetComponent<SaveableEntity>().InstanceId;

            store.Dehydrate(ChunkKey, scene);

            // The chunk unloads.
            Object.DestroyImmediate(original);
            spawned.Remove(original);

            store.Hydrate(ChunkKey, scene);

            SaveableEntity restored = WorldSaveStore.EntitiesIn(scene).FirstOrDefault();
            Assert.IsNotNull(restored, "nothing was re-created for the chunk");
            spawned.Add(restored.gameObject);

            Assert.AreEqual(instanceId, restored.InstanceId, "the restored object took a new identity");
            Assert.AreEqual(new Vector3(5f, 1f, -3f), restored.transform.position);
            Assert.AreEqual(42, restored.GetComponent<CounterSaver>().Value);
            Assert.AreEqual(scene, restored.gameObject.scene, "restored into the wrong scene");
        }

        /// <summary>
        /// The record is rebuilt on every capture rather than merged, so an object destroyed since
        /// the last one stops existing instead of being resurrected on the next load.
        /// </summary>
        [Test]
        public void Dehydrate_ForgetsAnEntityThatWasDestroyed()
        {
            var store = new WorldSaveStore();

            GameObject first = RuntimeEntity("kept", "prefab-a", Vector3.zero, 1);
            GameObject second = RuntimeEntity("picked-up", "prefab-a", Vector3.one, 2);

            store.Dehydrate(ChunkKey, scene);
            Assert.AreEqual(2, RuntimeIn(store, ChunkKey).Count);

            Object.DestroyImmediate(second);
            spawned.Remove(second);

            store.Dehydrate(ChunkKey, scene);

            Assert.AreEqual(1, RuntimeIn(store, ChunkKey).Count);
            Assert.AreEqual(first.GetComponent<SaveableEntity>().InstanceId,
                            RuntimeIn(store, ChunkKey)[0].InstanceId);
        }

        /// <summary>The runtime records routed to one scene, which is what a scene load has to spawn.</summary>
        private static List<EntityRecord> RuntimeIn(WorldSaveStore store, string sceneKey) =>
            store.Record.InScene(sceneKey).Where(r => !r.Authored).ToList();

        /// <summary>Hydrating a chunk whose objects are already live must not double them.</summary>
        [Test]
        public void Hydrate_DoesNotDuplicateAnEntityThatIsStillAlive()
        {
            var store = new WorldSaveStore();

            RuntimeEntity("crate", "prefab-a", Vector3.zero, 1);

            store.Dehydrate(ChunkKey, scene);
            store.Hydrate(ChunkKey, scene);

            Assert.AreEqual(1, WorldSaveStore.EntitiesIn(scene).Count());
        }

        [Test]
        public void Hydrate_SkipsAnEntityWhosePrefabIsNotRegistered()
        {
            var store = new WorldSaveStore();

            GameObject orphan = RuntimeEntity("orphan", "prefab-that-was-deleted", Vector3.zero, 1);
            store.Dehydrate(ChunkKey, scene);

            Object.DestroyImmediate(orphan);
            spawned.Remove(orphan);

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => store.Hydrate(ChunkKey, scene));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsEmpty(WorldSaveStore.EntitiesIn(scene));
        }

        [Test]
        public void Hydrate_OnASceneWithNoRecordIsHarmless()
        {
            var store = new WorldSaveStore();

            Assert.DoesNotThrow(() => store.Hydrate("chunk:99,99", scene));
            Assert.IsEmpty(WorldSaveStore.EntitiesIn(scene));
        }

        /// <summary>
        /// Regression. A player carries a SaveableEntity and stands in the persistent scene, so the
        /// world store walks straight over it — and used to capture it as an ordinary world entity.
        /// The next load then instantiated a lifeless copy of the player from its prefab, next to
        /// the real one. Found by probing a live session, which is why the assertion is on the
        /// record rather than on anything visible.
        /// </summary>
        [Test]
        public void Dehydrate_IgnoresEntitiesOwnedByAnotherSystem()
        {
            var store = new WorldSaveStore();

            RuntimeEntity("crate", "prefab-a", Vector3.zero, 1);

            GameObject player = RuntimeEntity("player", "prefab-a", Vector3.one, 2);
            SetScopeExternal(player.GetComponent<SaveableEntity>());

            store.Dehydrate(ChunkKey, scene);

            Assert.AreEqual(1, RuntimeIn(store, ChunkKey).Count,
                            "an externally-owned entity was captured into the world record");
            Assert.AreEqual("prefab-a", RuntimeIn(store, ChunkKey)[0].PrefabId);
            Assert.AreEqual(1, RuntimeIn(store, ChunkKey)[0].State
                                   .TryGet("counter", out CounterSaver.Payload p) ? p.value : -1);
        }

        [Test]
        public void Hydrate_DoesNotRecreateAnEntityOwnedByAnotherSystem()
        {
            var store = new WorldSaveStore();

            GameObject player = RuntimeEntity("player", "prefab-a", Vector3.one, 2);
            SetScopeExternal(player.GetComponent<SaveableEntity>());

            store.Dehydrate(ChunkKey, scene);

            Object.DestroyImmediate(player);
            spawned.Remove(player);

            store.Hydrate(ChunkKey, scene);

            Assert.IsEmpty(WorldSaveStore.EntitiesIn(scene),
                           "the world store resurrected an object it does not own");
        }

        private static void SetScopeExternal(SaveableEntity entity)
        {
            var so = new UnityEditor.SerializedObject(entity);
            so.FindProperty("scope").enumValueIndex = (int)SaveScope.External;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─────────────────────────────────────────────
        //  Persistence across a whole save file
        // ─────────────────────────────────────────────

        /// <summary>
        /// The end-to-end shape: capture, serialize, parse, and restore into a store built from the
        /// parsed document — which is exactly what quitting and loading does.
        /// </summary>
        [Test]
        public void StoreState_SurvivesASerializationRoundTrip()
        {
            var store = new WorldSaveStore();

            GameObject original = RuntimeEntity("crate", "prefab-a", new Vector3(1f, 2f, 3f), 7);
            string instanceId = original.GetComponent<SaveableEntity>().InstanceId;

            store.Dehydrate(ChunkKey, scene);

            var document = new SaveDocument { World = store.Record };
            SaveDocument reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(document));

            Object.DestroyImmediate(original);
            spawned.Remove(original);

            var reloadedStore = new WorldSaveStore(reloaded.World);
            reloadedStore.Hydrate(ChunkKey, scene);

            SaveableEntity restored = WorldSaveStore.EntitiesIn(scene).FirstOrDefault();
            Assert.IsNotNull(restored);
            spawned.Add(restored.gameObject);

            Assert.AreEqual(instanceId, restored.InstanceId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), restored.transform.position);
            Assert.AreEqual(7, restored.GetComponent<CounterSaver>().Value);
        }

        // ─────────────────────────────────────────────
        //  Authored objects
        // ─────────────────────────────────────────────

        /// <summary>
        /// Authored objects are re-created by the chunk scene on every load, so a tombstone is the
        /// only thing that can keep one deleted.
        /// </summary>
        [Test]
        public void RecordDestroyed_RemovesAnAuthoredObjectOnTheNextHydrate()
        {
            var store = new WorldSaveStore();

            GameObject authored = InScene("authored-crate");
            SaveableEntity entity = AuthoredEntity(authored, "authored-1");

            store.RecordDestroyed(entity);
            store.Hydrate(ChunkKey, scene);

            Assert.IsTrue(authored == null, "the destroyed authored object came back with the scene");
            spawned.Remove(authored);
        }

        /// <summary>
        /// The tombstone must outlive the capture that cannot see the object, or reloading a chunk
        /// once would forget the deletion and put the object back forever after.
        /// </summary>
        [Test]
        public void Dehydrate_KeepsTombstonesForObjectsNoLongerInTheScene()
        {
            var store = new WorldSaveStore();

            GameObject authored = InScene("authored-crate");
            store.RecordDestroyed(AuthoredEntity(authored, "authored-1"));

            Object.DestroyImmediate(authored);
            spawned.Remove(authored);

            store.Dehydrate(ChunkKey, scene);

            Assert.Contains("authored-1", store.Record.Destroyed);
        }

        [Test]
        public void AuthoredState_IsRestoredInPlaceRatherThanRespawned()
        {
            var store = new WorldSaveStore();

            GameObject authored = InScene("authored-crate");
            AuthoredEntity(authored, "authored-1");
            CounterSaver counter = authored.AddComponent<CounterSaver>();
            authored.GetComponent<SaveableEntity>().InvalidateSavers();
            counter.Value = 55;

            store.Dehydrate(ChunkKey, scene);
            Assert.IsEmpty(RuntimeIn(store, ChunkKey),
                           "an authored object must never be recorded as something to instantiate");

            counter.Value = 0;
            store.Hydrate(ChunkKey, scene);

            Assert.AreEqual(55, counter.Value);
            Assert.AreEqual(1, WorldSaveStore.EntitiesIn(scene).Count(), "the authored object was duplicated");
        }

        // ─────────────────────────────────────────────
        //  Objects that change scene while the game runs
        // ─────────────────────────────────────────────

        /// <summary>
        /// The bug this whole rewrite exists for.
        ///
        /// WorldStreamer.UpdateSceneMembership moves every SceneTracked entity into whichever chunk
        /// it has wandered into, so a creature is routinely captured in a scene it was not authored
        /// in. Records used to be filed under that scene and looked up there on load — but the scene
        /// file puts the creature back in its HOME scene, so the lookup missed, the record was
        /// dropped with a warning, and every creature in the world reappeared at its authored
        /// position. Reported as "the player's position is the same, the other entities restart".
        /// </summary>
        [Test]
        public void AuthoredState_SurvivesTheObjectHavingWanderedIntoAnotherScene()
        {
            var store = new WorldSaveStore();

            GameObject creature = InScene("golem");
            AuthoredEntity(creature, "authored-golem");
            CounterSaver counter = creature.AddComponent<CounterSaver>();
            creature.GetComponent<SaveableEntity>().InvalidateSavers();
            counter.Value = 33;
            creature.transform.position = new Vector3(120f, 4f, -60f);

            // It wanders out of the scene it was authored in and into a streamed chunk, which is
            // where the save catches it.
            Scene wandered = EditorSceneManager.NewPreviewScene();
            try
            {
                SceneManager.MoveGameObjectToScene(creature, wandered);
                store.Dehydrate(ChunkKey, wandered);

                // Reload: the scene file puts it back where it was authored, at its authored pose.
                SceneManager.MoveGameObjectToScene(creature, scene);
                counter.Value = 0;
                creature.transform.position = Vector3.zero;

                store.Hydrate(PersistentKey, scene);

                Assert.AreEqual(33, counter.Value,
                    "state captured in the chunk the creature wandered into was dropped when its " +
                    "home scene loaded");
                Assert.AreEqual(new Vector3(120f, 4f, -60f), creature.transform.position,
                    "the creature was left at its authored position instead of where it was left");
            }
            finally
            {
                if (wandered.IsValid()) EditorSceneManager.ClosePreviewScene(wandered);
            }
        }

        /// <summary>
        /// Position is recorded for authored objects too, not only for the runtime ones that need it
        /// to be spawned at all — so an object persists where it was left whether or not anyone
        /// remembered to give it a TransformSaveable.
        /// </summary>
        [Test]
        public void AuthoredPosition_IsRestoredWithoutATransformSaver()
        {
            var store = new WorldSaveStore();

            GameObject prop = InScene("pushed-crate");
            AuthoredEntity(prop, "authored-crate");
            prop.transform.position = new Vector3(9f, 0f, 4f);

            store.Dehydrate(ChunkKey, scene);

            prop.transform.position = Vector3.zero;
            store.Hydrate(ChunkKey, scene);

            Assert.AreEqual(new Vector3(9f, 0f, 4f), prop.transform.position);
        }

        /// <summary>
        /// A creature killed in a chunk it had wandered into must stay dead when the scene it was
        /// authored in loads it again. Tombstones used to be filed per scene, so it did not.
        /// </summary>
        [Test]
        public void ATombstoneAppliesInWhicheverSceneTheObjectTurnsUp()
        {
            var store = new WorldSaveStore();

            GameObject creature = InScene("golem");
            store.RecordDestroyed(AuthoredEntity(creature, "authored-golem"));

            // Killed while it was three chunks away; the scene that re-creates it is its home one.
            store.Hydrate(PersistentKey, scene);

            Assert.IsTrue(creature == null, "the destroyed creature came back in its home scene");
            spawned.Remove(creature);
        }

        /// <summary>
        /// SaveableEntity marks itself authored from OnValidate at edit time, which does not run for
        /// an object built in a test — so the flag is set through the serialized field directly.
        /// </summary>
        private static SaveableEntity AuthoredEntity(GameObject target, string instanceId)
        {
            SaveableEntity entity = target.AddComponent<SaveableEntity>();

            var so = new UnityEditor.SerializedObject(entity);
            so.FindProperty("instanceId").stringValue = instanceId;
            so.FindProperty("authored").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            // OnEnable already registered the entity under the id it was born with; re-running it
            // is what makes the registry agree with the id just written.
            entity.enabled = false;
            entity.enabled = true;

            return entity;
        }
    }
}
