using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpaceGame.Persistence;

namespace SpaceGame.Tests.EditMode
{
    /// <summary>
    /// Slot listing: what a load menu reads, and the path handling that stands between a
    /// player-typed save name and the rest of the filesystem.
    /// </summary>
    public class SaveSlotTests
    {
        private string root;
        private SaveSlots slots;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SpaceGameSlotTests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            slots = new SaveSlots(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        private void WriteSlot(string slotId, DateTime savedAtUtc)
        {
            var document = new SaveDocument();
            document.Header.SavedAtUtc = savedAtUtc;
            document.Header.SlotLabel = slotId;

            SaveFileStore.Write(slots.PathFor(slotId), document);
        }

        [Test]
        public void List_IsEmptyWhenNothingHasBeenSaved()
        {
            Assert.IsEmpty(slots.List());
        }

        [Test]
        public void List_IsEmptyWhenTheDirectoryDoesNotExist()
        {
            var missing = new SaveSlots(Path.Combine(root, "not-created"));
            Assert.IsEmpty(missing.List());
        }

        /// <summary>Newest first, so Continue is the first row and needs no sorting of its own.</summary>
        [Test]
        public void List_OrdersNewestFirst()
        {
            var now = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

            WriteSlot("oldest", now.AddHours(-5));
            WriteSlot("newest", now);
            WriteSlot("middle", now.AddHours(-1));

            List<SaveSlotInfo> listed = slots.List();

            Assert.AreEqual(new[] { "newest", "middle", "oldest" },
                            listed.ConvertAll(s => s.SlotId).ToArray());
        }

        [Test]
        public void TryGetMostRecent_ReturnsTheNewestReadableSlot()
        {
            var now = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

            WriteSlot("older", now.AddHours(-2));
            WriteSlot("newer", now);

            Assert.IsTrue(slots.TryGetMostRecent(out SaveSlotInfo slot));
            Assert.AreEqual("newer", slot.SlotId);
        }

        [Test]
        public void TryGetMostRecent_IsFalseWithNothingOnDisk()
        {
            Assert.IsFalse(slots.TryGetMostRecent(out _));
        }

        /// <summary>
        /// A save the player can see and delete beats one that silently is not there, so unreadable
        /// files are listed rather than hidden — but Continue must not try to load one.
        /// </summary>
        [Test]
        public void UnreadableSlots_AreListedButSkippedByContinue()
        {
            WriteSlot("good", new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc));

            File.WriteAllText(slots.PathFor("broken"), "not a save at all");

            List<SaveSlotInfo> listed = slots.List();

            Assert.AreEqual(2, listed.Count);
            Assert.IsTrue(listed.Exists(s => s.SlotId == "broken" && s.Unreadable));

            Assert.IsTrue(slots.TryGetMostRecent(out SaveSlotInfo slot));
            Assert.AreEqual("good", slot.SlotId);
        }

        /// <summary>
        /// The pre-world-selection files. A player who ever ran an older build still has an
        /// autosave.json sitting beside their real worlds, and it must not be offered as one.
        /// </summary>
        [Test]
        public void LegacySlots_AreNeverListed()
        {
            var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

            WriteSlot("autosave", now);
            WriteSlot("quicksave", now.AddHours(-1));
            WriteSlot("Dune Camp", now.AddHours(-2));

            List<SaveSlotInfo> listed = slots.List();

            Assert.AreEqual(new[] { "Dune Camp" }, listed.ConvertAll(s => s.SlotId).ToArray());
        }

        /// <summary>Newest on disk, so an unfiltered Continue would land straight on it.</summary>
        [Test]
        public void LegacySlots_AreSkippedByContinue()
        {
            var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

            WriteSlot("Dune Camp", now.AddHours(-2));
            WriteSlot("autosave", now);

            Assert.IsTrue(slots.TryGetMostRecent(out SaveSlotInfo slot));
            Assert.AreEqual("Dune Camp", slot.SlotId);
        }

        /// <summary>
        /// Case-insensitive, because the filesystems this ships on are: "Autosave" and "autosave"
        /// are one file, so hiding only the lowercase spelling would leave the same save visible
        /// under a different name.
        /// </summary>
        [Test]
        public void IsLegacySlotId_IgnoresCaseAndPunctuation()
        {
            Assert.IsTrue(SaveSlots.IsLegacySlotId("autosave"));
            Assert.IsTrue(SaveSlots.IsLegacySlotId("AutoSave"));
            Assert.IsTrue(SaveSlots.IsLegacySlotId("  autosave  "));
            Assert.IsTrue(SaveSlots.IsLegacySlotId("autosave!"));
            Assert.IsTrue(SaveSlots.IsLegacySlotId("quicksave"));

            Assert.IsFalse(SaveSlots.IsLegacySlotId("Dune Camp"));
            Assert.IsFalse(SaveSlots.IsLegacySlotId(null));
        }

        [Test]
        public void Delete_RemovesTheSlotEntirely()
        {
            WriteSlot("gone", DateTime.UtcNow);
            WriteSlot("gone", DateTime.UtcNow);   // a second write, so a .bak exists to survive

            slots.Delete("gone");

            Assert.IsFalse(slots.Exists("gone"));
            Assert.IsEmpty(slots.List());
        }

        [Test]
        public void Label_FallsBackToTheSlotIdWhenTheHeaderHasNoLabel()
        {
            var document = new SaveDocument();
            document.Header.SlotLabel = string.Empty;
            SaveFileStore.Write(slots.PathFor("unlabelled"), document);

            Assert.AreEqual("unlabelled", slots.List()[0].Label);
        }

        // ─────────────────────────────────────────────
        //  Path safety
        // ─────────────────────────────────────────────

        /// <summary>
        /// Slot ids reach this from player-typed save names. Without sanitizing, "../../autoexec"
        /// is a path outside the save folder that the game would happily write to.
        /// </summary>
        [Test]
        public void Sanitize_StripsPathTraversal()
        {
            Assert.AreEqual("etcpasswd", SaveSlots.Sanitize("../../etc/passwd"));
            Assert.AreEqual("Windowssystem32", SaveSlots.Sanitize(@"..\..\Windows\system32"));
        }

        [Test]
        public void Sanitize_KeepsOrdinaryNames()
        {
            Assert.AreEqual("My Save 3", SaveSlots.Sanitize("My Save 3"));
            Assert.AreEqual("auto_save-1", SaveSlots.Sanitize("auto_save-1"));
        }

        [Test]
        public void Sanitize_NeverProducesAnEmptyName()
        {
            Assert.AreEqual("save", SaveSlots.Sanitize(""));
            Assert.AreEqual("save", SaveSlots.Sanitize("   "));
            Assert.AreEqual("save", SaveSlots.Sanitize(null));
            Assert.AreEqual("save", SaveSlots.Sanitize("///"));
        }

        [Test]
        public void PathFor_StaysInsideTheSaveRoot()
        {
            string path = Path.GetFullPath(slots.PathFor("../escape"));

            StringAssert.StartsWith(Path.GetFullPath(root), path);
        }
    }
}
