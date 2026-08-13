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
            Assert.AreEqual("backpack", BackpackSaveCodec.Key);
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

        [Test]
        public void CurrentVersion_MatchesTheGoldenSamplesBelow()
        {
            // The samples in this fixture were written at version 1. If this fails, the samples are
            // stale: add a migration, then add a version-N literal beside each version-1 one.
            Assert.AreEqual(1, SaveDocument.CurrentVersion,
                "Format version moved past the golden samples in this fixture.");
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
            var state = Read<BackpackSaveCodec.State>(
                @"{""strapItemIds"":[""s1"",null],""mainItemIds"":[null,""m2"",""m3""]}");

            Assert.AreEqual(new[] { "s1", null }, state.strapItemIds);
            Assert.AreEqual(new[] { null, "m2", "m3" }, state.mainItemIds);
        }

        [Test]
        public void RigidbodyPayload_v1_StillLoads()
        {
            var state = Read<RigidbodySaveable.State>(
                @"{""velocity"":{""x"":1.5,""y"":-2.0,""z"":3.25},
                    ""angularVelocity"":{""x"":0.0,""y"":0.5,""z"":0.0},
                    ""isKinematic"":true}");

            Assert.AreEqual(new Vector3(1.5f, -2f, 3.25f), state.velocity);
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), state.angularVelocity);
            Assert.IsTrue(state.isKinematic);
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

            Assert.AreEqual(1, doc.Header.Version);
            Assert.AreEqual("Quicksave", doc.Header.SlotLabel);
            Assert.AreEqual(3600.0, doc.Header.PlaytimeSeconds, 0.001);

            PlayerRecord player = doc.FindPlayer("profile-1");
            Assert.IsNotNull(player, "player records are keyed by profileId — did that field move?");
            Assert.AreEqual(new Vector3(100f, 5f, 200f), player.Position);
            Assert.IsTrue(player.State.TryGet(HealthSaveable.Key, out HealthSaveable.State health));
            Assert.AreEqual(42, health.current);

            Assert.IsTrue(doc.World.Global.TryGet(GameStateSaveable.Key, out GameStateSaveable.State game));
            Assert.AreEqual(60f, game.gameTimer, 0.001f);

            Assert.IsTrue(doc.World.TryGet("chunk:3,2", out SceneRecord chunk));
            Assert.AreEqual(1, chunk.Entities.Count);
            Assert.AreEqual("instance-guid", chunk.Entities[0].InstanceId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), chunk.Entities[0].Position);
            Assert.IsTrue(chunk.Authored.ContainsKey("authored-guid"));
            Assert.Contains("gone-guid", chunk.DestroyedAuthored);
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
