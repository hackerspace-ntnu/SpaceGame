using System.IO;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Tests.EditMode
{
    /// <summary>
    /// Durability. These are the tests that matter most in the whole save system: every case here
    /// is a real way a player loses a session, and none of them can be reproduced by playing the
    /// game — you have to crash the process at the right microsecond.
    /// </summary>
    public class SaveFileStoreTests
    {
        private string root;
        private string path;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SpaceGameSaveTests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            path = Path.Combine(root, "slot.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        private static SaveDocument DocumentWith(string label, Vector3 position)
        {
            var document = new SaveDocument();
            document.Header.SlotLabel = label;
            document.Players.Add(new PlayerRecord { ProfileId = "p", Position = position });
            return document;
        }

        [Test]
        public void Read_MissingFileIsReportedAsMissingNotAsAnError()
        {
            SaveFileStore.ReadResult result = SaveFileStore.Read(path);

            Assert.AreEqual(SaveFileStore.ReadOutcome.Missing, result.Outcome);
            Assert.IsFalse(result.HasDocument);
        }

        [Test]
        public void Write_ThenRead_RoundTrips()
        {
            SaveFileStore.Write(path, DocumentWith("first", new Vector3(1f, 2f, 3f)));

            SaveFileStore.ReadResult result = SaveFileStore.Read(path);

            Assert.AreEqual(SaveFileStore.ReadOutcome.Ok, result.Outcome);
            Assert.AreEqual("first", result.Document.Header.SlotLabel);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), result.Document.Players[0].Position);
        }

        [Test]
        public void Write_CreatesTheDirectoryItNeeds()
        {
            string nested = Path.Combine(root, "a", "b", "slot.json");

            SaveFileStore.Write(nested, DocumentWith("nested", Vector3.zero));

            Assert.IsTrue(File.Exists(nested));
        }

        /// <summary>
        /// The rotation that makes a crash survivable: the second write moves the first file to
        /// .bak rather than overwriting it.
        /// </summary>
        [Test]
        public void Write_MovesThePreviousSaveToBackup()
        {
            SaveFileStore.Write(path, DocumentWith("first", Vector3.zero));
            SaveFileStore.Write(path, DocumentWith("second", Vector3.one));

            Assert.IsTrue(File.Exists(path + SaveFileStore.BackupSuffix), "no backup was kept");

            Assert.AreEqual("second", SaveFileStore.Read(path).Document.Header.SlotLabel);
        }

        [Test]
        public void Write_LeavesNoTempFileBehind()
        {
            SaveFileStore.Write(path, DocumentWith("first", Vector3.zero));

            Assert.IsFalse(File.Exists(path + SaveFileStore.TempSuffix));
        }

        /// <summary>
        /// A truncated live file — what a crash mid-write used to leave behind — must cost the
        /// player the newest save, not the session.
        /// </summary>
        [Test]
        public void Read_FallsBackToTheBackupWhenTheLiveFileIsCorrupt()
        {
            SaveFileStore.Write(path, DocumentWith("good", new Vector3(5f, 6f, 7f)));
            SaveFileStore.Write(path, DocumentWith("newer", Vector3.zero));

            File.WriteAllText(path, "{ truncated mid-w");

            SaveFileStore.ReadResult result = SaveFileStore.Read(path);

            Assert.AreEqual(SaveFileStore.ReadOutcome.RecoveredFromBackup, result.Outcome);
            Assert.AreEqual("good", result.Document.Header.SlotLabel);
            Assert.IsNotNull(result.Error, "a recovery the player is not told about is a silent data loss");
        }

        [Test]
        public void Read_ZeroLengthLiveFileFallsBackToTheBackup()
        {
            SaveFileStore.Write(path, DocumentWith("good", Vector3.zero));
            SaveFileStore.Write(path, DocumentWith("newer", Vector3.zero));

            File.WriteAllText(path, string.Empty);

            Assert.AreEqual(SaveFileStore.ReadOutcome.RecoveredFromBackup, SaveFileStore.Read(path).Outcome);
        }

        [Test]
        public void Read_BothFilesCorruptReportsCorruptWithoutThrowing()
        {
            SaveFileStore.Write(path, DocumentWith("first", Vector3.zero));
            SaveFileStore.Write(path, DocumentWith("second", Vector3.zero));

            File.WriteAllText(path, "garbage");
            File.WriteAllText(path + SaveFileStore.BackupSuffix, "also garbage");

            SaveFileStore.ReadResult result = SaveFileStore.Read(path);

            Assert.AreEqual(SaveFileStore.ReadOutcome.Corrupt, result.Outcome);
            Assert.IsFalse(result.HasDocument);
            Assert.IsNotNull(result.Error);
        }

        [Test]
        public void Read_CorruptLiveFileWithNoBackupReportsCorrupt()
        {
            File.WriteAllText(path, "garbage");

            SaveFileStore.ReadResult result = SaveFileStore.Read(path);

            Assert.AreEqual(SaveFileStore.ReadOutcome.Corrupt, result.Outcome);
            Assert.IsNotNull(result.Error);
        }

        /// <summary>A deleted slot that comes back from its own backup is not deleted.</summary>
        [Test]
        public void Delete_RemovesTheBackupAndTempFilesToo()
        {
            SaveFileStore.Write(path, DocumentWith("first", Vector3.zero));
            SaveFileStore.Write(path, DocumentWith("second", Vector3.zero));
            File.WriteAllText(path + SaveFileStore.TempSuffix, "leftover");

            SaveFileStore.Delete(path);

            Assert.IsFalse(SaveFileStore.Exists(path));
            Assert.IsFalse(File.Exists(path + SaveFileStore.BackupSuffix));
            Assert.IsFalse(File.Exists(path + SaveFileStore.TempSuffix));
        }

        [Test]
        public void Exists_IsTrueWhenOnlyTheBackupSurvives()
        {
            SaveFileStore.Write(path, DocumentWith("first", Vector3.zero));
            SaveFileStore.Write(path, DocumentWith("second", Vector3.zero));
            File.Delete(path);

            Assert.IsTrue(SaveFileStore.Exists(path));
        }

        // ─────────────────────────────────────────────
        //  Never trade a save with players for one without
        // ─────────────────────────────────────────────

        private static SaveDocument WithPlayers(int count)
        {
            var document = new SaveDocument();
            for (int i = 0; i < count; i++)
                document.Players.Add(new PlayerRecord { ProfileId = "p" + i });

            return document;
        }

        /// <summary>
        /// The regression this guard exists for: a half-initialised SaveManager captured nothing,
        /// produced a valid but player-less document, and would have written it over a good
        /// autosave — ending the session's progress with no error anywhere.
        /// </summary>
        [Test]
        public void WouldDiscardAllPlayers_IsTrueWhenReplacingPlayersWithNone()
        {
            SaveFileStore.Write(path, WithPlayers(1));

            Assert.IsTrue(SaveFileStore.WouldDiscardAllPlayers(path, WithPlayers(0)));
        }

        [Test]
        public void WouldDiscardAllPlayers_IsFalseWhenTheNewSaveKeepsAPlayer()
        {
            SaveFileStore.Write(path, WithPlayers(2));

            Assert.IsFalse(SaveFileStore.WouldDiscardAllPlayers(path, WithPlayers(1)));
        }

        /// <summary>A first save must never be blocked — there is no progress to protect yet.</summary>
        [Test]
        public void WouldDiscardAllPlayers_IsFalseWhenNoSaveExistsYet()
        {
            Assert.IsFalse(SaveFileStore.WouldDiscardAllPlayers(path, WithPlayers(0)));
        }

        /// <summary>An existing save that had no players either is not progress worth protecting.</summary>
        [Test]
        public void WouldDiscardAllPlayers_IsFalseWhenTheExistingSaveHasNoPlayers()
        {
            SaveFileStore.Write(path, WithPlayers(0));

            Assert.IsFalse(SaveFileStore.WouldDiscardAllPlayers(path, WithPlayers(0)));
        }

        [Test]
        public void WouldDiscardAllPlayers_IsTrueForANullDocument()
        {
            Assert.IsTrue(SaveFileStore.WouldDiscardAllPlayers(path, null));
        }

        /// <summary>A corrupt file on disk must not block the write that would replace it.</summary>
        [Test]
        public void WouldDiscardAllPlayers_IsFalseWhenTheExistingSaveIsUnreadable()
        {
            File.WriteAllText(path, "garbage");

            Assert.IsFalse(SaveFileStore.WouldDiscardAllPlayers(path, WithPlayers(0)));
        }

        [Test]
        public void TryReadHeader_ReadsThroughToTheBackup()
        {
            SaveFileStore.Write(path, DocumentWith("readable", Vector3.zero));
            SaveFileStore.Write(path, DocumentWith("newer", Vector3.zero));
            File.WriteAllText(path, "garbage");

            Assert.IsTrue(SaveFileStore.TryReadHeader(path, out SaveHeader header));
            Assert.AreEqual("readable", header.SlotLabel);
        }
    }
}
