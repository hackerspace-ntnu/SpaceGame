using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Tests.EditMode
{
    /// <summary>
    /// The save format's round trip. Everything here is pure C# — no scene, no Play mode — which is
    /// the point of keeping the format in its own assembly.
    /// </summary>
    public class SaveSerializationTests
    {
        private struct Payload
        {
            public int number;
            public string text;
            public Vector3 point;
            public List<string> items;
        }

        // ─────────────────────────────────────────────
        //  Unity structs
        // ─────────────────────────────────────────────

        [Test]
        public void Vector3_RoundTripsExactly()
        {
            var bag = new StateBag();
            bag.Set("k", new Payload { point = new Vector3(1.5f, -2.25f, 1024f) });

            Assert.IsTrue(bag.TryGet("k", out Payload restored));
            Assert.AreEqual(new Vector3(1.5f, -2.25f, 1024f), restored.point);
        }

        /// <summary>
        /// The reason every Unity struct needs an explicit converter: Vector3 exposes `normalized`,
        /// itself a Vector3, so Newtonsoft's default contract walks that chain until the stack ends.
        /// A serializer that gets this wrong does not produce bad data — it takes the process down.
        /// </summary>
        [Test]
        public void Vector3_SerializesOnlyItsComponents()
        {
            var document = new SaveDocument();
            document.Players.Add(new PlayerRecord { ProfileId = "p", Position = Vector3.one });

            string json = SaveSerializer.ToJson(document);

            StringAssert.DoesNotContain("normalized", json);
            StringAssert.DoesNotContain("magnitude", json);
        }

        [Test]
        public void Quaternion_RoundTripsExactly()
        {
            var expected = Quaternion.Euler(15f, 200f, -33f);

            var document = new SaveDocument();
            document.Players.Add(new PlayerRecord { ProfileId = "p", Rotation = expected });

            SaveDocument restored = SaveSerializer.FromJson(SaveSerializer.ToJson(document));

            Assert.That(Quaternion.Angle(expected, restored.Players[0].Rotation), Is.LessThan(0.01f));
        }

        /// <summary>
        /// An all-zero quaternion is what a truncated payload reads as, and Transform.rotation makes
        /// an object with one disappear. Identity is the only safe reading of "nothing was stored".
        /// </summary>
        [Test]
        public void Quaternion_MissingComponentsReadAsIdentity()
        {
            var document = SaveSerializer.FromJson(
                @"{""header"":{""version"":1},""players"":[{""profileId"":""p"",""rotation"":{}}]}");

            Assert.AreEqual(Quaternion.identity, document.Players[0].Rotation);
        }

        // ─────────────────────────────────────────────
        //  StateBag
        // ─────────────────────────────────────────────

        [Test]
        public void StateBag_RoundTripsAPayload()
        {
            var bag = new StateBag();
            bag.Set("inventory", new Payload
            {
                number = 42,
                text = "hello",
                items = new List<string> { "a", null, "c" },
            });

            Assert.IsTrue(bag.TryGet("inventory", out Payload restored));
            Assert.AreEqual(42, restored.number);
            Assert.AreEqual("hello", restored.text);
            Assert.AreEqual(new[] { "a", null, "c" }, restored.items);
        }

        [Test]
        public void StateBag_MissingKeyReportsAbsentRatherThanThrowing()
        {
            var bag = new StateBag();
            Assert.IsFalse(bag.TryGet("nothing-here", out Payload _));
        }

        /// <summary>
        /// The property that lets savers be added and reshaped without a format migration: a key
        /// whose stored shape no longer converts is reported absent, and the caller keeps its
        /// current state instead of the load failing.
        /// </summary>
        [Test]
        public void StateBag_IncompatibleShapeReportsAbsentRatherThanThrowing()
        {
            var bag = new StateBag();
            bag.Set("k", new { number = "not a number at all" });

            Assert.IsFalse(bag.TryGet("k", out Payload _));
        }

        [Test]
        public void StateBag_NullPayloadClearsTheKey()
        {
            var bag = new StateBag();
            bag.Set("k", new Payload { number = 1 });
            bag.Set("k", null);

            Assert.IsFalse(bag.Has("k"));
            Assert.AreEqual(0, bag.Count);
        }

        [Test]
        public void StateBag_KeysAreIndependent()
        {
            var bag = new StateBag();
            bag.Set("health", new Payload { number = 10 });
            bag.Set("inventory", new Payload { number = 20 });
            bag.Remove("health");

            Assert.IsFalse(bag.Has("health"));
            Assert.IsTrue(bag.TryGet("inventory", out Payload kept));
            Assert.AreEqual(20, kept.number);
        }

        // ─────────────────────────────────────────────
        //  Document
        // ─────────────────────────────────────────────

        [Test]
        public void Document_RoundTripsAFullWorld()
        {
            var document = new SaveDocument();
            document.Header.SlotLabel = "Slot One";
            document.Header.PlaytimeSeconds = 123.5;

            var player = new PlayerRecord { ProfileId = "profile-a", Position = new Vector3(10f, 20f, 30f) };
            player.EnsureState().Set("health", new Payload { number = 55 });
            document.Players.Add(player);

            string chunkKey = SceneKey.ForChunk(new Vector2Int(3, 2));

            EntityRecord entity = document.World.GetOrCreate("instance-guid");
            entity.PrefabId = "prefab-guid";
            entity.Scene = chunkKey;
            entity.Position = new Vector3(1f, 2f, 3f);
            entity.EnsureState().Set("rigidbody", new Payload { number = 7 });

            EntityRecord authored = document.World.GetOrCreate("authored-guid");
            authored.Scene = chunkKey;
            authored.Authored = true;

            document.World.Destroyed.Add("gone-guid");

            SaveDocument restored = SaveSerializer.FromJson(SaveSerializer.ToJson(document));

            Assert.AreEqual("Slot One", restored.Header.SlotLabel);
            Assert.AreEqual(123.5, restored.Header.PlaytimeSeconds, 0.0001);

            PlayerRecord restoredPlayer = restored.FindPlayer("profile-a");
            Assert.IsNotNull(restoredPlayer);
            Assert.AreEqual(new Vector3(10f, 20f, 30f), restoredPlayer.Position);
            Assert.IsTrue(restoredPlayer.State.TryGet("health", out Payload health));
            Assert.AreEqual(55, health.number);

            Assert.IsTrue(restored.World.TryGet("instance-guid", out EntityRecord restoredEntity));
            Assert.AreEqual(chunkKey, restoredEntity.Scene);
            Assert.IsFalse(restoredEntity.Authored);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), restoredEntity.Position);

            Assert.IsTrue(restored.World.TryGet("authored-guid", out EntityRecord restoredAuthored));
            Assert.IsTrue(restoredAuthored.Authored);

            Assert.Contains("gone-guid", restored.World.Destroyed);
        }

        /// <summary>
        /// Lists in the DTOs are initialized non-empty-capable, and Newtonsoft's default is to MERGE
        /// a file's entries into the existing instance rather than replace it. Without
        /// ObjectCreationHandling.Replace, loading a save twice doubles every collection in it.
        /// </summary>
        [Test]
        public void Document_CollectionsAreReplacedNotAppendedTo()
        {
            var document = new SaveDocument();
            document.Players.Add(new PlayerRecord { ProfileId = "a" });

            string json = SaveSerializer.ToJson(document);

            Assert.AreEqual(1, SaveSerializer.FromJson(json).Players.Count);
            Assert.AreEqual(1, SaveSerializer.FromJson(json).Players.Count);
        }

        [Test]
        public void Document_EmptyDocumentRoundTrips()
        {
            SaveDocument restored = SaveSerializer.FromJson(SaveSerializer.ToJson(new SaveDocument()));

            Assert.IsNotNull(restored.Players);
            Assert.IsNotNull(restored.World);
            Assert.IsNotNull(restored.World.Entities);
            Assert.IsNotNull(restored.World.Global);
        }

        /// <summary>A file whose optional sections are absent must load, not crash the load menu.</summary>
        [Test]
        public void Document_MissingSectionsNormalizeToEmpty()
        {
            SaveDocument restored = SaveSerializer.FromJson(@"{""header"":{""version"":1}}");

            Assert.IsNotNull(restored.Players);
            Assert.AreEqual(0, restored.Players.Count);
            Assert.IsNotNull(restored.World.Entities);
        }

        [Test]
        public void Document_MalformedJsonThrowsSaveFormatException()
        {
            Assert.Throws<SaveFormatException>(() => SaveSerializer.FromJson("{ not json"));
            Assert.Throws<SaveFormatException>(() => SaveSerializer.FromJson(""));
        }

        [Test]
        public void Header_ReadsWithoutParsingTheBody()
        {
            var document = new SaveDocument();
            document.Header.SlotLabel = "Readable";

            // A body this build cannot possibly type, so a header read that touched it would fail.
            var root = JObject.Parse(SaveSerializer.ToJson(document));
            root["world"] = "corrupted into a string";

            Assert.IsTrue(SaveSerializer.TryReadHeader(root.ToString(), out SaveHeader header));
            Assert.AreEqual("Readable", header.SlotLabel);
        }

        // ─────────────────────────────────────────────
        //  Scene keys
        // ─────────────────────────────────────────────

        [Test]
        public void SceneKey_ChunkKeyRoundTrips()
        {
            string key = SceneKey.ForChunk(new Vector2Int(-4, 7));

            Assert.IsTrue(SceneKey.TryParseChunk(key, out Vector2Int coord));
            Assert.AreEqual(new Vector2Int(-4, 7), coord);
        }

        [Test]
        public void SceneKey_NonChunkKeysAreNotParsedAsChunks()
        {
            Assert.IsFalse(SceneKey.TryParseChunk(SceneKey.ForScene("AlgeaCave"), out _));
            Assert.IsFalse(SceneKey.TryParseChunk(SceneKey.Persistent, out _));
            Assert.IsFalse(SceneKey.TryParseChunk("chunk:nonsense", out _));
        }

        /// <summary>Different namespaces must not collide — they share one dictionary.</summary>
        [Test]
        public void SceneKey_ChunkAndSceneKeysAreDistinct()
        {
            Assert.AreNotEqual(SceneKey.ForChunk(new Vector2Int(1, 1)), SceneKey.ForScene("1,1"));
        }
    }
}
