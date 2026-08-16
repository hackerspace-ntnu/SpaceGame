using System.IO;
using NUnit.Framework;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The pure world-naming and world-guard rules. No Unity types, no files — if one of these
    /// fails the wrong world can be loaded into, which is the failure that silently corrupts a
    /// session rather than announcing itself.
    /// </summary>
    public class WorldIdentityTests
    {
        [Test]
        public void IdFor_StripsPathSeparators()
        {
            Assert.AreEqual("evil", WorldIdentity.IdFor("../../evil"));
        }

        [Test]
        public void IdFor_EmptyNameFallsBackToSave()
        {
            Assert.AreEqual("save", WorldIdentity.IdFor("   "));
        }

        [Test]
        public void Accepts_MatchingConfigId()
        {
            var header = new SaveHeader { WorldConfigId = "abc123" };
            Assert.IsTrue(WorldIdentity.AcceptsConfig(header, "abc123", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void Rejects_MismatchedConfigId()
        {
            var header = new SaveHeader { WorldConfigId = "abc123" };
            Assert.IsFalse(WorldIdentity.AcceptsConfig(header, "different", out string error));
            Assert.IsNotNull(error);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Accepts_LegacySaveWithNoConfigId()
        {
            // A file written before world selection existed. It belongs to the only world that
            // existed at the time, so it must still load.
            var header = new SaveHeader { WorldConfigId = "" };
            Assert.IsTrue(WorldIdentity.AcceptsConfig(header, "abc123", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void DisplayNameFor_PrefersHeaderWorldName()
        {
            var header = new SaveHeader { WorldName = "My Desert Run" };
            Assert.AreEqual("My Desert Run", WorldIdentity.DisplayNameFor(header, "my desert run"));
        }

        [Test]
        public void DisplayNameFor_FallsBackToSlotIdOnLegacySave()
        {
            var header = new SaveHeader { WorldName = "" };
            Assert.AreEqual("autosave", WorldIdentity.DisplayNameFor(header, "autosave"));
        }

        // ------------------------------------------------------------ new-world names

        private string root;

        [SetUp]
        public void MakeTempRoot()
        {
            root = Path.Combine(Path.GetTempPath(), "SpaceGameWorldNames_" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void RemoveTempRoot()
        {
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        private SaveSlots SlotsWith(params string[] slotIds)
        {
            var slots = new SaveSlots(root);
            foreach (string id in slotIds) File.WriteAllText(slots.PathFor(id), "{}");
            return slots;
        }

        [Test]
        public void ValidateNewName_AcceptsAnUnusedName()
        {
            Assert.IsTrue(WorldIdentity.ValidateNewName("Dune Camp", SlotsWith(), out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void ValidateNewName_RejectsEmptyAndWhitespace()
        {
            Assert.IsFalse(WorldIdentity.ValidateNewName("   ", SlotsWith(), out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void ValidateNewName_RejectsAnExistingWorld()
        {
            Assert.IsFalse(WorldIdentity.ValidateNewName("Dune Camp", SlotsWith("Dune Camp"), out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void ValidateNewName_RejectsANameThatSanitisesOntoAnExistingFile()
        {
            // The collision the player cannot see: both names are the same file, so accepting this
            // would overwrite "Dune Camp" rather than make a second world.
            Assert.IsFalse(WorldIdentity.ValidateNewName("Dune Camp!!", SlotsWith("Dune Camp"), out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void ValidateNewName_SurvivesNoSaveDirectoryAtAll()
        {
            // A fresh install: the save root does not exist yet, and naming the first world must
            // not be blocked by that.
            var slots = new SaveSlots(Path.Combine(root, "not-created-yet"));
            Assert.IsTrue(WorldIdentity.ValidateNewName("First World", slots, out string error));
            Assert.IsNull(error);
        }
    }
}
