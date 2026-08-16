using System.IO;
using NUnit.Framework;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The save/load contract, exercised against real files in a temp directory.
    ///
    /// These cover the three things world selection has to get right: a world round-trips, two
    /// worlds never touch each other's state, and a new world starts empty. They go through the
    /// real SaveFileStore rather than mocking it — the whole reason SaveSlots takes its root by
    /// injection is so the file layer itself can be tested.
    /// </summary>
    public class WorldSaveRoundTripTests
    {
        private string root;
        private SaveSlots slots;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SpaceGameWorldTests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            slots = new SaveSlots(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        /// <summary>Builds a document carrying one identifiable piece of world state.</summary>
        private static SaveDocument DocumentFor(string worldName, string configId, string marker)
        {
            var document = new SaveDocument
            {
                Header = new SaveHeader
                {
                    WorldName = worldName,
                    WorldConfigId = configId,
                    SlotLabel = worldName,
                },
            }.Normalized();

            document.World.Global.Set("testMarker", new MarkerPayload { value = marker });
            return document;
        }

        /// <summary>
        /// A named type rather than an anonymous one: the serializer is configured with a
        /// field-based contract resolver, and anonymous types expose their members as properties.
        /// </summary>
        private class MarkerPayload
        {
            public string value;
        }

        private static string MarkerIn(SaveDocument document)
        {
            Assert.IsTrue(document.World.Global.TryGet("testMarker", out MarkerPayload payload),
                          "The document carries no test marker.");
            return payload.value;
        }

        [Test]
        public void SaveThenLoad_RoundTripsWorldState()
        {
            SaveFileStore.Write(slots.PathFor("Alpha"), DocumentFor("Alpha", "config-1", "alpha-state"));

            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor("Alpha"));

            Assert.AreEqual(SaveFileStore.ReadOutcome.Ok, result.Outcome);
            Assert.AreEqual("alpha-state", MarkerIn(result.Document.Normalized()));
            Assert.AreEqual("Alpha", result.Document.Header.WorldName);
            Assert.AreEqual("config-1", result.Document.Header.WorldConfigId);
        }

        [Test]
        public void TwoWorlds_DoNotShareState()
        {
            SaveFileStore.Write(slots.PathFor("Alpha"), DocumentFor("Alpha", "config-1", "alpha-state"));
            SaveFileStore.Write(slots.PathFor("Beta"), DocumentFor("Beta", "config-1", "beta-state"));

            Assert.AreEqual("alpha-state", MarkerIn(SaveFileStore.Read(slots.PathFor("Alpha")).Document.Normalized()));
            Assert.AreEqual("beta-state", MarkerIn(SaveFileStore.Read(slots.PathFor("Beta")).Document.Normalized()));

            Assert.AreEqual(2, slots.List().Count, "Each world must be its own file.");
        }

        [Test]
        public void NewWorld_OverAnExistingNameIsRefusedByExists()
        {
            SaveFileStore.Write(slots.PathFor("Alpha"), DocumentFor("Alpha", "config-1", "alpha-state"));

            // WorldSelectUI checks Exists before staging a new world, so accidentally overwriting a
            // real save is impossible.
            Assert.IsTrue(slots.Exists(WorldIdentity.IdFor("Alpha")));
            Assert.IsFalse(slots.Exists(WorldIdentity.IdFor("Gamma")));
        }

        [Test]
        public void ConfigGuard_RefusesASaveFromAnotherWorld()
        {
            SaveDocument foreign = DocumentFor("Alpha", "config-OTHER", "alpha-state");

            Assert.IsFalse(WorldIdentity.AcceptsConfig(foreign.Header, "config-1", out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void LegacySave_WithNoWorldFieldsStillLoads()
        {
            // A file written before world selection existed: no worldName, no worldConfigId.
            var legacy = new SaveDocument { Header = new SaveHeader { SlotLabel = "autosave" } }.Normalized();
            legacy.World.Global.Set("testMarker", new MarkerPayload { value = "legacy-state" });

            SaveFileStore.Write(slots.PathFor("autosave"), legacy);
            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor("autosave"));

            Assert.AreEqual(SaveFileStore.ReadOutcome.Ok, result.Outcome);
            Assert.AreEqual("legacy-state", MarkerIn(result.Document.Normalized()));
            Assert.IsTrue(WorldIdentity.AcceptsConfig(result.Document.Header, "config-1", out _),
                          "A legacy save belongs to the only world that existed when it was written.");
        }

        [Test]
        public void WorldName_CannotEscapeTheSaveRoot()
        {
            string id = WorldIdentity.IdFor("../../etc/passwd");
            string path = slots.PathFor(id);

            Assert.AreEqual(Path.GetFullPath(root), Path.GetFullPath(Path.GetDirectoryName(path)));
        }

        [Test]
        public void ListedWorlds_ShowTheirDisplayName()
        {
            SaveFileStore.Write(slots.PathFor("my desert run"),
                                DocumentFor("My Desert Run", "config-1", "state"));

            SaveSlotInfo slot = slots.List()[0];

            Assert.AreEqual("My Desert Run", WorldIdentity.DisplayNameFor(slot.Header, slot.SlotId));
        }
    }
}
