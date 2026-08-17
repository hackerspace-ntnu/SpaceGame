using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// How an entity decides which savers it speaks for, and what happens when one misbehaves.
    ///
    /// Lives in an Editor folder rather than the EditMode asmdef because SaveableEntity is in
    /// Assembly-CSharp, and an asmdef cannot reference Assembly-CSharp.
    /// </summary>
    public class SaveableEntityTests
    {
        /// <summary>Records what it was asked to do, so a test can assert on the conversation.</summary>
        private class SpySaver : MonoBehaviour, ISaveable
        {
            public string Key = "spy";
            public int Value;
            public int CaptureCalls;
            public int RestoreCalls;

            public string SaveKey => Key;

            public object CaptureState()
            {
                CaptureCalls++;
                return new Payload { value = Value };
            }

            public void RestoreState(JObject state)
            {
                RestoreCalls++;
                if (state?["value"] is { } v) Value = v.Value<int>();
            }

            public struct Payload { public int value; }
        }

        private class ThrowingSaver : MonoBehaviour, ISaveable
        {
            public string SaveKey => "throws";
            public object CaptureState() => throw new System.InvalidOperationException("boom");
            public void RestoreState(JObject state) => throw new System.InvalidOperationException("boom");
        }

        private GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            return child;
        }

        [Test]
        public void Savers_GathersFromTheOwnGameObjectAndChildren()
        {
            root = new GameObject("root");
            root.AddComponent<SaveableEntity>();
            root.AddComponent<SpySaver>().Key = "own";
            Child(root, "child").AddComponent<SpySaver>().Key = "child";

            Assert.AreEqual(2, root.GetComponent<SaveableEntity>().Savers().Count);
        }

        /// <summary>
        /// The cut-off that keeps a player and the backpack they are carrying from both claiming the
        /// same twenty-two slots. Without it the contents are captured twice, into two records that
        /// diverge as soon as either is restored.
        /// </summary>
        [Test]
        public void Savers_StopsAtANestedSaveableEntity()
        {
            root = new GameObject("player");
            root.AddComponent<SaveableEntity>();
            root.AddComponent<SpySaver>().Key = "player";

            GameObject pack = Child(root, "backpack");
            pack.AddComponent<SaveableEntity>();
            pack.AddComponent<SpySaver>().Key = "pack";

            Child(pack, "deeper").AddComponent<SpySaver>().Key = "deeper";

            var savers = root.GetComponent<SaveableEntity>().Savers();

            Assert.AreEqual(1, savers.Count);
            Assert.AreEqual("player", savers[0].SaveKey);
        }

        [Test]
        public void CaptureAndRestore_RoundTripsThroughTheBag()
        {
            root = new GameObject("root");
            var entity = root.AddComponent<SaveableEntity>();
            var spy = root.AddComponent<SpySaver>();
            spy.Value = 77;

            var bag = new StateBag();
            entity.Capture(bag);

            spy.Value = 0;
            entity.Restore(bag);

            Assert.AreEqual(77, spy.Value);
            Assert.AreEqual(1, spy.CaptureCalls);
            Assert.AreEqual(1, spy.RestoreCalls);
        }

        /// <summary>A saver with no stored payload must be left alone, not handed a null and reset.</summary>
        [Test]
        public void Restore_SkipsSaversWithNoStoredPayload()
        {
            root = new GameObject("root");
            var entity = root.AddComponent<SaveableEntity>();
            var spy = root.AddComponent<SpySaver>();
            spy.Value = 5;

            entity.Restore(new StateBag());

            Assert.AreEqual(5, spy.Value);
            Assert.AreEqual(0, spy.RestoreCalls);
        }

        /// <summary>
        /// One broken saver must not cost the player the other two hundred objects in the chunk, so
        /// the failure is reported and the walk continues.
        /// </summary>
        [Test]
        public void Capture_ContinuesPastASaverThatThrows()
        {
            root = new GameObject("root");
            var entity = root.AddComponent<SaveableEntity>();
            root.AddComponent<ThrowingSaver>();
            var spy = root.AddComponent<SpySaver>();
            spy.Value = 9;

            var bag = new StateBag();

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            entity.Capture(bag);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(bag.Has("spy"), "the healthy saver was skipped because a neighbour threw");
            Assert.IsFalse(bag.Has("throws"));
        }

        [Test]
        public void EnsureRuntime_AddsIdentityToAnObjectThatHadNone()
        {
            root = new GameObject("dropped");

            SaveableEntity entity = SaveableEntity.EnsureRuntime(root, "prefab-guid");

            Assert.IsNotNull(entity);
            Assert.AreEqual("prefab-guid", entity.PrefabId);
            Assert.IsNotEmpty(entity.InstanceId);
            Assert.IsFalse(entity.IsAuthored);
        }

        [Test]
        public void EnsureRuntime_KeepsTheInstanceIdItAlreadyHad()
        {
            root = new GameObject("dropped");
            SaveableEntity first = SaveableEntity.EnsureRuntime(root, "a");
            string id = first.InstanceId;

            SaveableEntity second = SaveableEntity.EnsureRuntime(root, "b");

            Assert.AreSame(first, second);
            Assert.AreEqual(id, second.InstanceId, "identity must survive a second spawn-path call");
            Assert.AreEqual("b", second.PrefabId, "the spawn site's prefab id should win");
        }

        [Test]
        public void AdoptIdentity_TakesOverTheSavedRecordsIdentity()
        {
            root = new GameObject("restored");
            SaveableEntity entity = SaveableEntity.EnsureRuntime(root, "spawned-guid");

            entity.AdoptIdentity("saved-prefab", "saved-instance");

            Assert.AreEqual("saved-prefab", entity.PrefabId);
            Assert.AreEqual("saved-instance", entity.InstanceId);
            Assert.AreSame(entity, SaveableEntity.LiveEntities["saved-instance"]);
        }

        // ─────────────────────────────────────────────
        //  Health adapter
        // ─────────────────────────────────────────────

        [Test]
        public void HealthSaveable_RoundTripsCurrentHealth()
        {
            root = new GameObject("mob");
            HealthComponent health = root.AddComponent<HealthComponent>();
            var saver = root.AddComponent<HealthSaveable>();

            health.Damage(30);
            int expected = health.GetHealth;

            var bag = new StateBag();
            bag.Set(saver.SaveKey, saver.CaptureState());

            health.ResetToFull();
            Assert.AreNotEqual(expected, health.GetHealth);

            bag.TryGetRaw(saver.SaveKey, out JObject payload);
            saver.RestoreState(payload);

            Assert.AreEqual(expected, health.GetHealth);
        }

        /// <summary>
        /// maxHealth belongs to the prefab, so a save written when it was higher must not push an
        /// entity above the ceiling this build gives it.
        /// </summary>
        [Test]
        public void HealthSaveable_ClampsAValueAboveTheCurrentMaximum()
        {
            root = new GameObject("mob");
            HealthComponent health = root.AddComponent<HealthComponent>();
            var saver = root.AddComponent<HealthSaveable>();

            saver.RestoreState(JObject.Parse(@"{""current"":9999,""max"":9999}"));

            Assert.AreEqual(health.GetMaxHealth, health.GetHealth);
        }

        [Test]
        public void HealthSaveable_RestoringZeroRaisesDeath()
        {
            root = new GameObject("mob");
            HealthComponent health = root.AddComponent<HealthComponent>();
            var saver = root.AddComponent<HealthSaveable>();

            bool died = false;
            health.OnDeath += () => died = true;

            saver.RestoreState(JObject.Parse(@"{""current"":0}"));

            Assert.IsFalse(health.Alive);
            Assert.IsTrue(died, "a listener tracking alive/dead would be left holding the wrong answer");
        }

        [Test]
        public void HealthSaveable_MalformedPayloadLeavesHealthAlone()
        {
            root = new GameObject("mob");
            HealthComponent health = root.AddComponent<HealthComponent>();
            var saver = root.AddComponent<HealthSaveable>();

            health.Damage(10);
            int before = health.GetHealth;

            saver.RestoreState(JObject.Parse(@"{""current"":""not a number""}"));

            Assert.AreEqual(before, health.GetHealth);
        }
    }
}
