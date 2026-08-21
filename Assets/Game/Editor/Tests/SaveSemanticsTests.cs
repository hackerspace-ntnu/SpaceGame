// Regression tests for the save system's RULES, as opposed to any particular saver.
//
// Each one pins a decision that was wrong before, was silent about being wrong, and would be easy to
// reintroduce because the wrong version reads perfectly well.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class SaveSemanticsTests
    {
        // ─────────────────────────────────────────────
        //  "No entry" is a value, not an absence
        // ─────────────────────────────────────────────

        /// <summary>A saver that records nothing writes no key. That half was already right.</summary>
        [Test]
        public void CaptureStateReturningNullRemovesTheKey()
        {
            var bag = new StateBag();
            bag.Set("thing", new { a = 1 });
            Assert.IsTrue(bag.TryGetRaw("thing", out _), "precondition: the key was written");

            bag.Set("thing", null);

            Assert.IsFalse(bag.TryGetRaw("thing", out _),
                "A null payload must clear the key so that 'produced nothing' and 'was never present' " +
                "read identically on the way back in.");
        }

        /// <summary>
        /// <b>Every saver is called on a restore, including ones with no stored key.</b>
        ///
        /// Skipping them looked free and was not. Two things depend on it: a saver cannot reset to its
        /// default unless it is told there was nothing stored, and every deferred saver stages its
        /// pending work in RestoreState and clears it there — so MountSaveable.pendingRider,
        /// AgentStateSaveable.hasPending and OrnithopterSaveable.pendingFlying were all cleared in the
        /// one method that was not being called. A craft flying at one save and grounded at the next
        /// was re-launched into the air on load.
        /// </summary>
        [Test]
        public void RestoreCallsEverySaverEvenWithNoStoredKey()
        {
            var go = new GameObject(nameof(RestoreCallsEverySaverEvenWithNoStoredKey));
            try
            {
                var entity = go.AddComponent<SaveableEntity>();
                var spy = go.AddComponent<RecordingSaveable>();

                entity.InvalidateSavers();
                entity.Restore(new StateBag());          // deliberately empty

                Assert.AreEqual(1, spy.RestoreCalls,
                    "RestoreState must be invoked even when the record has no entry for this saver.");
                Assert.IsNull(spy.LastPayload,
                    "The absent case must arrive as null, which is the saver's cue to reset.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RestoreHandsTheStoredPayloadToItsOwnSaver()
        {
            var go = new GameObject(nameof(RestoreHandsTheStoredPayloadToItsOwnSaver));
            try
            {
                var entity = go.AddComponent<SaveableEntity>();
                var spy = go.AddComponent<RecordingSaveable>();

                var bag = new StateBag();
                bag.Set(RecordingSaveable.Key, new { marker = 7 });

                entity.InvalidateSavers();
                entity.Restore(bag);

                Assert.IsNotNull(spy.LastPayload);
                Assert.AreEqual(7, spy.LastPayload["marker"].Value<int>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// A key that is present reports present, whatever it deserializes to.
        ///
        /// <c>TryGet</c> used to end in <c>return value != null</c>, which made a stored value that
        /// legitimately reads back as null indistinguishable from a key nobody wrote — the exact
        /// distinction the method exists to report.
        /// </summary>
        [Test]
        public void TryGetReportsPresenceNotEmptiness()
        {
            var bag = new StateBag();
            bag.Set("empty", new List<string>());

            Assert.IsTrue(bag.TryGet("empty", out List<string> value));
            Assert.IsNotNull(value);
            Assert.IsEmpty(value);
        }

        // ─────────────────────────────────────────────
        //  Tombstones
        // ─────────────────────────────────────────────

        [Test]
        public void TombstonesCanBeLifted()
        {
            var world = new WorldRecord();
            world.Normalize();

            world.MarkDestroyed("abc");
            Assert.IsTrue(world.IsDestroyed("abc"));

            Assert.IsTrue(world.ClearDestroyed("abc"),
                "Nothing in the project could remove a tombstone, so the list grew for the life of " +
                "the world and a re-derived identity could inherit a dead object's grave.");

            Assert.IsFalse(world.IsDestroyed("abc"));
            CollectionAssert.DoesNotContain(world.Destroyed, "abc",
                "The serialized list and its lookup index must not disagree.");
        }

        [Test]
        public void MarkingTheSameObjectTwiceDoesNotDuplicateIt()
        {
            var world = new WorldRecord();
            world.Normalize();

            world.MarkDestroyed("abc");
            world.MarkDestroyed("abc");

            Assert.AreEqual(1, world.Destroyed.Count);
        }

        [Test]
        public void IsDestroyedWorksOnAFreshlyDeserializedRecord()
        {
            // Deserialization fills the list directly and never calls Normalize, so the lookup index
            // has to build itself on demand or every tombstone in a loaded file is ignored.
            var world = new WorldRecord { Destroyed = new List<string> { "abc" } };

            Assert.IsTrue(world.IsDestroyed("abc"));
        }

        // ─────────────────────────────────────────────
        //  Poses and scale
        // ─────────────────────────────────────────────

        /// <summary>
        /// A zero scale is a scale.
        ///
        /// The old sentinel treated <c>Vector3.zero</c> as "no payload", which it could never actually
        /// be — a record with no scale field deserializes to the field initializer, not to zero — while
        /// silently overriding an object deliberately scaled to zero, which is how several props here
        /// hide without being disabled. They came back full size.
        /// </summary>
        [Test]
        public void ARecordCanExpressAnIntentionalZeroScale()
        {
            var record = new EntityRecord { Scale = Vector3.zero, HasScale = true };

            Assert.IsTrue(record.HasScale);
            Assert.AreEqual(Vector3.zero, record.Scale);
        }

        [Test]
        public void RecordsDefaultToHavingAScaleSoOldFilesKeepRestoringIt()
        {
            Assert.IsTrue(new EntityRecord().HasScale);
        }

        // ─────────────────────────────────────────────
        //  File safety
        // ─────────────────────────────────────────────

        /// <summary>
        /// A save from a newer build is not overwritten.
        ///
        /// <c>SaveMigrator</c> already refused to LOAD one, and its comment says why: a partially
        /// understood world that then gets saved back over the good file. But nothing checked on the
        /// way out, so the refusal only protected half the round trip — launch an older build, wait
        /// one autosave interval, and the newer save is gone.
        /// </summary>
        [Test]
        public void SavingRefusesToDowngradeAFileFromANewerBuild()
        {
            string path = Path.Combine(Path.GetTempPath(), $"sg-downgrade-{Guid.NewGuid():N}.json");

            try
            {
                var future = new SaveDocument
                {
                    Header = new SaveHeader { Version = SaveDocument.CurrentVersion + 1 },
                };
                SaveFileStore.Write(path, future, pretty: false);

                var current = new SaveDocument
                {
                    Header = new SaveHeader { Version = SaveDocument.CurrentVersion },
                };

                Assert.IsTrue(SaveFileStore.WouldDowngradeFormat(path, current));
            }
            finally
            {
                SaveFileStore.Delete(path);
            }
        }

        [Test]
        public void SavingOverAnEqualOrOlderFileIsAllowed()
        {
            string path = Path.Combine(Path.GetTempPath(), $"sg-samever-{Guid.NewGuid():N}.json");

            try
            {
                var existing = new SaveDocument
                {
                    Header = new SaveHeader { Version = SaveDocument.CurrentVersion },
                };
                SaveFileStore.Write(path, existing, pretty: false);

                Assert.IsFalse(SaveFileStore.WouldDowngradeFormat(path, existing));
            }
            finally
            {
                SaveFileStore.Delete(path);
            }
        }

        /// <summary>
        /// Reading a corrupt file reports corruption instead of throwing.
        ///
        /// <c>Read</c> is documented as never throwing for a bad file, because the load menu has to
        /// render a "corrupt save" row rather than take an unhandled exception. It caught three
        /// exception types, and the migration ladder — which runs inside the read and does unchecked
        /// casts on hand-editable tokens — could throw a fourth straight through it.
        /// </summary>
        [Test]
        public void ReadingAMalformedV1FileReportsCorruptRatherThanThrowing()
        {
            string path = Path.Combine(Path.GetTempPath(), $"sg-corrupt-{Guid.NewGuid():N}.json");

            try
            {
                // A v1 file whose instanceId is an object, not a string: the shape that used to throw
                // InvalidCastException out of V1GlobalEntities and past every guard above it.
                File.WriteAllText(path, @"{
                    ""header"": { ""version"": 1 },
                    ""players"": [],
                    ""world"": {
                        ""scenes"": {
                            ""persistent"": {
                                ""entities"": [ { ""instanceId"": { ""nope"": true } } ]
                            }
                        }
                    }
                }");

                var result = default(SaveFileStore.ReadResult);
                Assert.DoesNotThrow(() => result = SaveFileStore.Read(path));
                Assert.AreNotEqual(SaveFileStore.ReadOutcome.Ok, result.Outcome,
                    "A file that cannot be understood must not be reported as read successfully.");
            }
            finally
            {
                SaveFileStore.Delete(path);
            }
        }

        // ─────────────────────────────────────────────
        //  Deferred ordering
        // ─────────────────────────────────────────────

        /// <summary>
        /// The deferred pass runs in declared order, not in component order.
        ///
        /// Component order is the order somebody happened to add things to a prefab.
        /// <c>OrnithopterSaveable</c> had to route around depending on it by abandoning its own
        /// OnLoadComplete and subscribing to MountModule.Mounted instead — a workaround invisible to
        /// the next person with the same problem.
        /// </summary>
        [Test]
        public void DeferredSaversRunInLoadOrder()
        {
            var go = new GameObject(nameof(DeferredSaversRunInLoadOrder));
            try
            {
                var entity = go.AddComponent<SaveableEntity>();

                // Added late-first, so component order is the opposite of the intended order.
                var late = go.AddComponent<LateDeferredSaveable>();
                var early = go.AddComponent<EarlyDeferredSaveable>();

                var order = new List<string>();
                late.Log = order;
                early.Log = order;

                entity.InvalidateSavers();
                entity.NotifyLoadComplete();

                CollectionAssert.AreEqual(new[] { "early", "late" }, order,
                    "LoadOrder must decide the deferred pass, or a saver that needs another's result " +
                    "is at the mercy of how a prefab was assembled.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ─────────────────────────────────────────────
        //  Fixtures
        // ─────────────────────────────────────────────

        private class RecordingSaveable : MonoBehaviour, ISaveable
        {
            public const string Key = "spy";

            public int RestoreCalls;
            public JObject LastPayload;

            public string SaveKey => Key;
            public object CaptureState() => null;

            public void RestoreState(JObject state)
            {
                RestoreCalls++;
                LastPayload = state;
            }
        }

        private class EarlyDeferredSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
        {
            public List<string> Log;
            public string SaveKey => "early";
            public object CaptureState() => null;
            public void RestoreState(JObject state) { }
            public int LoadOrder => IDeferredSaveable.Early;
            public void OnLoadComplete() => Log?.Add("early");
        }

        private class LateDeferredSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
        {
            public List<string> Log;
            public string SaveKey => "late";
            public object CaptureState() => null;
            public void RestoreState(JObject state) { }
            public int LoadOrder => IDeferredSaveable.Late;
            public void OnLoadComplete() => Log?.Add("late");
        }
    }
}
