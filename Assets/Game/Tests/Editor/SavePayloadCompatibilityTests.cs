using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// Frozen samples of what every saver has ever written, checked against the structs this build
    /// reads them into.
    ///
    /// These exist because of the one way this save format fails quietly. A payload whose shape no
    /// longer matches is reported ABSENT — <c>StateBag.TryGet</c> returns false and the saver keeps
    /// its defaults — which is what lets savers be added and reshaped without a migration. The cost
    /// is that renaming a field looks exactly like "this saver is new": no compile error, no
    /// exception, no warning. Health silently comes back full; the hotbar silently comes back empty.
    ///
    /// So each saver gets a literal below, copied from a real save file, and a test that it still
    /// deserializes with its values intact. Rename a field and this fixture fails, which is the only
    /// place that failure can be made loud.
    ///
    /// WHEN ONE OF THESE FAILS: do not edit the literal to match the new shape — that just re-hides
    /// the break. Either restore the field name, or add a migration and KEEP the old literal as a
    /// second case, so both the old and new shapes are proven to load.
    ///
    /// The keys are frozen for the same reason: a saver's key is written into the file, so renaming
    /// it orphans everything saved under the old spelling.
    /// </summary>
    public class SavePayloadCompatibilityTests
    {
        private static T Read<T>(string json) => JObject.Parse(json).ToObject<T>(SaveSerializer.Serializer);

        // ─────────────────────────────────────────────
        //  Keys
        // ─────────────────────────────────────────────

        [Test]
        public void SaveKeys_AreUnchanged()
        {
            Assert.AreEqual("health", HealthSaveable.Key);
            Assert.AreEqual("inventory", InventorySaveCodec.Key);
            Assert.AreEqual("backpack", BackpackSaveable.Key);
            Assert.AreEqual("wallInventory", WallInventorySaveable.Key);
            Assert.AreEqual("rigidbody", RigidbodySaveable.Key);
            Assert.AreEqual("transform", TransformSaveable.Key);
            Assert.AreEqual("gameState", GameStateSaveable.Key);
        }

        [Test]
        public void SceneKeys_AreUnchanged()
        {
            Assert.AreEqual("persistent", SceneKey.Persistent);
            Assert.AreEqual("chunk:3,2", SceneKey.ForChunk(new Vector2Int(3, 2)));
            Assert.AreEqual("scene:AlgeaCave", SceneKey.ForScene("AlgeaCave"));
        }

        /// <summary>
        /// The contract this fixture actually defends is not that the format never moves — it is
        /// that a file written by ANY shipped build still opens in this one. So it asserts the
        /// ladder reaches the top from every rung below it, rather than pinning a version literal
        /// that has to be edited on each bump (and, being edited, stops meaning anything).
        ///
        /// The whole-document sample below is a real version-1 file and is read through the
        /// migration. Add another beside it whenever a version changes the SHAPE of the document,
        /// so the new shape is pinned too.
        /// </summary>
        [Test]
        public void AFileFromEveryShippedVersion_CanStillBeRead()
        {
            for (int version = 1; version <= SaveDocument.CurrentVersion; version++)
            {
                JObject root = JObject.Parse($@"{{""header"":{{""version"":{version}}}}}");

                Assert.DoesNotThrow(() => SaveMigrator.Migrate(root),
                    $"a save written at version {version} can no longer be read — the migration " +
                    "ladder has a gap, and every player still on that version loses their world");
            }
        }

        /// <summary>
        /// The version-2 shape, pinned the same way the version-1 body below is: one global,
        /// id-keyed entity registry, with the scene as a field rather than as the address.
        /// </summary>
        [Test]
        public void Version2Document_ReadsWithoutMigration()
        {
            const string Json = @"{
              ""header"": { ""version"": 2, ""slotLabel"": ""Quicksave"", ""worldName"": ""zombies"" },
              ""players"": [],
              ""world"": {
                ""global"": { ""entries"": {} },
                ""entities"": {
                  ""golem-1"": {
                    ""prefabId"": """",
                    ""instanceId"": ""golem-1"",
                    ""scene"": ""chunk:7,5"",
                    ""authored"": true,
                    ""position"": { ""x"": 3791.0, ""y"": 88.0, ""z"": 1562.0 },
                    ""rotation"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0, ""w"": 1.0 },
                    ""scale"": { ""x"": 1.0, ""y"": 1.0, ""z"": 1.0 },
                    ""hasPose"": true,
                    ""state"": { ""entries"": { ""health"": { ""current"": 90, ""max"": 420 } } }
                  }
                },
                ""destroyed"": [ ""dead-1"" ]
              }
            }";

            SaveDocument doc = SaveSerializer.FromJson(Json);

            Assert.AreEqual(2, doc.Header.Version);
            Assert.IsTrue(doc.World.TryGet("golem-1", out EntityRecord golem));
            Assert.IsTrue(golem.Authored);
            Assert.AreEqual("chunk:7,5", golem.Scene);
            Assert.AreEqual(new Vector3(3791f, 88f, 1562f), golem.Position);
            Assert.IsTrue(golem.State.TryGet(HealthSaveable.Key, out HealthSaveable.State health));
            Assert.AreEqual(90, health.current);
            Assert.IsTrue(doc.World.IsDestroyed("dead-1"));
        }

        // ─────────────────────────────────────────────
        //  Per-saver payloads
        // ─────────────────────────────────────────────

        [Test]
        public void HealthPayload_v1_StillLoads()
        {
            var state = Read<HealthSaveable.State>(@"{""current"":73,""max"":100}");

            Assert.AreEqual(73, state.current);
            Assert.AreEqual(100, state.max);
        }

        [Test]
        public void InventoryPayload_v1_StillLoads()
        {
            var state = Read<InventorySaveCodec.State>(
                @"{""itemIds"":[""aaaa1111"",null,""cccc3333"",null],""selectedSlot"":2}");

            Assert.AreEqual(4, state.itemIds.Count);
            Assert.AreEqual("aaaa1111", state.itemIds[0]);
            Assert.IsNull(state.itemIds[1], "a hole in the hotbar must survive as a hole");
            Assert.AreEqual("cccc3333", state.itemIds[2]);
            Assert.AreEqual(2, state.selectedSlot);
        }

        [Test]
        public void BackpackPayload_v1_StillLoads()
        {
            var state = Read<PackSaveCodec.State>(
                @"{""strapItemIds"":[""s1"",null],""mainItemIds"":[null,""m2"",""m3""]}");

            Assert.AreEqual(new[] { "s1", null }, state.strapItemIds);
            Assert.AreEqual(new[] { null, "m2", "m3" }, state.mainItemIds);
        }

        /// <summary>
        /// Every shipped file carries an <c>isKinematic</c> the build no longer has a field for —
        /// it was dropped once it turned out to be a netcode teardown artefact rather than anything
        /// about the entity. The motion beside it must still read, and the stray field must not be
        /// what stops a player's world from loading.
        /// </summary>
        [Test]
        public void RigidbodyPayload_v1_StillLoads()
        {
            var state = Read<RigidbodySaveable.State>(
                @"{""velocity"":{""x"":1.5,""y"":-2.0,""z"":3.25},
                    ""angularVelocity"":{""x"":0.0,""y"":0.5,""z"":0.0},
                    ""isKinematic"":true}");

            Assert.AreEqual(new Vector3(1.5f, -2f, 3.25f), state.velocity);
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), state.angularVelocity);
        }

        [Test]
        public void TransformPayload_v1_StillLoads()
        {
            var state = Read<TransformSaveable.State>(
                @"{""position"":{""x"":10.0,""y"":20.0,""z"":30.0},
                    ""rotation"":{""x"":0.0,""y"":0.7071068,""z"":0.0,""w"":0.7071068},
                    ""scale"":{""x"":2.0,""y"":2.0,""z"":2.0}}");

            Assert.AreEqual(new Vector3(10f, 20f, 30f), state.position);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), state.scale);
            Assert.That(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), state.rotation), Is.LessThan(0.05f));
        }

        [Test]
        public void GameStatePayload_v1_StillLoads()
        {
            var state = Read<GameStateSaveable.State>(@"{""gameTimer"":1234.5}");

            Assert.AreEqual(1234.5f, state.gameTimer, 0.001f);
        }

        // ─────────────────────────────────────────────
        //  Whole-document shape
        // ─────────────────────────────────────────────

        /// <summary>
        /// A complete v1 file, of the shape <c>SaveFileStore</c> writes. Guards the field names of
        /// the document itself, which no per-saver test covers — rename <c>profileId</c> and every
        /// player silently becomes a new player.
        /// </summary>
        [Test]
        public void Document_v1_StillLoads()
        {
            const string Json = @"{
              ""header"": {
                ""version"": 1,
                ""savedAtUtc"": ""2026-08-13T22:00:00Z"",
                ""playtimeSeconds"": 3600.0,
                ""gameVersion"": ""0.1"",
                ""slotLabel"": ""Quicksave""
              },
              ""players"": [{
                ""profileId"": ""profile-1"",
                ""position"": { ""x"": 100.0, ""y"": 5.0, ""z"": 200.0 },
                ""rotation"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0, ""w"": 1.0 },
                ""state"": { ""entries"": { ""health"": { ""current"": 42, ""max"": 100 } } }
              }],
              ""world"": {
                ""global"": { ""entries"": { ""gameState"": { ""gameTimer"": 60.0 } } },
                ""scenes"": {
                  ""chunk:3,2"": {
                    ""entities"": [{
                      ""prefabId"": ""prefab-guid"",
                      ""instanceId"": ""instance-guid"",
                      ""position"": { ""x"": 1.0, ""y"": 2.0, ""z"": 3.0 },
                      ""rotation"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0, ""w"": 1.0 },
                      ""state"": { ""entries"": { ""rigidbody"": { ""isKinematic"": true } } }
                    }],
                    ""authored"": { ""authored-guid"": { ""entries"": {} } },
                    ""destroyedAuthored"": [""gone-guid""]
                  }
                }
              }
            }";

            SaveDocument doc = SaveSerializer.FromJson(Json);

            // Read through the v1 → v2 migration, which is the point: this payload is a real file
            // written by the shipped build, and a player with one must not lose their world to a
            // format change.
            Assert.AreEqual(2, doc.Header.Version);
            Assert.AreEqual("Quicksave", doc.Header.SlotLabel);
            Assert.AreEqual(3600.0, doc.Header.PlaytimeSeconds, 0.001);

            PlayerRecord player = doc.FindPlayer("profile-1");
            Assert.IsNotNull(player, "player records are keyed by profileId — did that field move?");
            Assert.AreEqual(new Vector3(100f, 5f, 200f), player.Position);
            Assert.IsTrue(player.State.TryGet(HealthSaveable.Key, out HealthSaveable.State health));
            Assert.AreEqual(42, health.current);

            Assert.IsTrue(doc.World.Global.TryGet(GameStateSaveable.Key, out GameStateSaveable.State game));
            Assert.AreEqual(60f, game.gameTimer, 0.001f);

            // The v1 per-scene record has become one global entry per object, with the scene it was
            // filed under preserved as routing information rather than as the address.
            Assert.IsTrue(doc.World.TryGet("instance-guid", out EntityRecord runtime),
                          "a v1 runtime entity did not survive the migration");
            Assert.AreEqual("chunk:3,2", runtime.Scene);
            Assert.IsFalse(runtime.Authored);
            Assert.AreEqual("prefab-guid", runtime.PrefabId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), runtime.Position);
            // Asked of the raw entry rather than a typed field: this v1 payload holds only
            // isKinematic, which the build no longer has a field for, so a typed read would come
            // back empty-but-successful and prove nothing. What the migration owes us is that the
            // entry arrived at all.
            Assert.IsTrue(runtime.State.TryGetRaw(RigidbodySaveable.Key, out JObject body),
                          "the runtime entity's payload was lost in the migration");
            Assert.IsNotNull(body[nameof(RigidbodySaveable.State.velocity)] ?? body["isKinematic"],
                             "the payload arrived empty, so its contents were dropped en route");

            Assert.IsTrue(doc.World.TryGet("authored-guid", out EntityRecord authored),
                          "a v1 authored record did not survive the migration");
            Assert.IsTrue(authored.Authored, "a migrated authored object would be respawned as a duplicate");
            Assert.AreEqual("chunk:3,2", authored.Scene);

            Assert.Contains("gone-guid", doc.World.Destroyed);
        }

        /// <summary>
        /// The tolerance this whole design rests on, stated as a test: a payload carrying a field
        /// this build has never heard of loads anyway, and the fields it does know still arrive.
        /// </summary>
        [Test]
        public void UnknownFieldsInAPayload_AreIgnored()
        {
            var state = Read<HealthSaveable.State>(
                @"{""current"":50,""max"":100,""stamina"":25,""nested"":{""a"":1}}");

            Assert.AreEqual(50, state.current);
        }
    }
}
