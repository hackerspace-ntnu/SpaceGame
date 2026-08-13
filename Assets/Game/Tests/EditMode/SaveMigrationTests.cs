using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpaceGame.Persistence;

namespace SpaceGame.Tests.EditMode
{
    /// <summary>
    /// The version ladder. Its job is to make a save written by last month's build load in this
    /// one, and — just as importantly — to refuse a save from next month's build rather than load
    /// half of it and then write the other half away.
    /// </summary>
    public class SaveMigrationTests
    {
        /// <summary>A step that stamps a marker, so a test can see whether and when it ran.</summary>
        private class MarkerMigration : ISaveMigration
        {
            private readonly string marker;

            public MarkerMigration(int fromVersion, string marker)
            {
                FromVersion = fromVersion;
                this.marker = marker;
            }

            public int FromVersion { get; }

            public void Apply(JObject root)
            {
                var trail = root["trail"] as JArray;
                if (trail == null)
                {
                    trail = new JArray();
                    root["trail"] = trail;
                }

                trail.Add(marker);
            }
        }

        private static JObject FileAtVersion(int version) =>
            JObject.Parse($@"{{""header"":{{""version"":{version}}}}}");

        [Test]
        public void CurrentVersion_IsLeftAlone()
        {
            JObject root = FileAtVersion(SaveDocument.CurrentVersion);

            Assert.DoesNotThrow(() => SaveMigrator.Migrate(root));
            Assert.AreEqual(SaveDocument.CurrentVersion, SaveMigrator.ReadVersion(root));
            Assert.IsNull(root["trail"]);
        }

        /// <summary>
        /// The dangerous case. A partially-understood future save that then gets AUTOSAVED back over
        /// the good file destroys progress, so the load is refused outright.
        /// </summary>
        [Test]
        public void FutureVersion_IsRefused()
        {
            JObject root = FileAtVersion(SaveDocument.CurrentVersion + 1);

            var error = Assert.Throws<SaveFormatException>(() => SaveMigrator.Migrate(root));
            StringAssert.Contains("newer build", error.Message);
        }

        [Test]
        public void FutureVersion_IsRefusedThroughTheSerializerToo()
        {
            string json = FileAtVersion(SaveDocument.CurrentVersion + 5).ToString();

            Assert.Throws<SaveFormatException>(() => SaveSerializer.FromJson(json));
        }

        /// <summary>A file that predates the version field is the first format by definition.</summary>
        [Test]
        public void MissingVersion_ReadsAsVersionOne()
        {
            Assert.AreEqual(1, SaveMigrator.ReadVersion(JObject.Parse(@"{""header"":{}}")));
            Assert.AreEqual(1, SaveMigrator.ReadVersion(JObject.Parse("{}")));
        }

        /// <summary>Zero and negatives would otherwise start the ladder below any migration that can exist.</summary>
        [Test]
        public void NonPositiveVersion_ReadsAsVersionOne()
        {
            Assert.AreEqual(1, SaveMigrator.ReadVersion(FileAtVersion(0)));
            Assert.AreEqual(1, SaveMigrator.ReadVersion(FileAtVersion(-3)));
        }

        /// <summary>
        /// Exercises the ladder with temporary steps against an explicit target, so the mechanism is
        /// proven now rather than on the day the first real migration depends on it.
        /// </summary>
        [Test]
        public void RegisteredSteps_RunInOrderAndAdvanceTheVersion()
        {
            using (SaveMigrator.RegisterScoped(new MarkerMigration(1, "first")))
            using (SaveMigrator.RegisterScoped(new MarkerMigration(2, "second")))
            {
                JObject root = FileAtVersion(1);

                SaveMigrator.MigrateTo(root, 3);

                Assert.AreEqual(3, SaveMigrator.ReadVersion(root));
                Assert.AreEqual(new[] { "first", "second" }, root["trail"]?.ToObject<string[]>());
            }
        }

        [Test]
        public void RegisteredSteps_StopAtTheTargetVersion()
        {
            using (SaveMigrator.RegisterScoped(new MarkerMigration(1, "first")))
            using (SaveMigrator.RegisterScoped(new MarkerMigration(2, "second")))
            {
                JObject root = FileAtVersion(1);

                SaveMigrator.MigrateTo(root, 2);

                Assert.AreEqual(2, SaveMigrator.ReadVersion(root));
                Assert.AreEqual(new[] { "first" }, root["trail"]?.ToObject<string[]>());
            }
        }

        [Test]
        public void MissingStep_ThrowsRatherThanLoadingAnUnmigratedFile()
        {
            // Only the step out of 2 exists, so a file at version 1 cannot reach 3.
            using (SaveMigrator.RegisterScoped(new MarkerMigration(2, "second")))
            {
                JObject root = FileAtVersion(1);

                var error = Assert.Throws<SaveFormatException>(() => SaveMigrator.MigrateTo(root, 3));
                StringAssert.Contains("no migration is", error.Message);
            }
        }

        [Test]
        public void RegisteredSteps_AreRemovedWhenTheirScopeEnds()
        {
            using (SaveMigrator.RegisterScoped(new MarkerMigration(1, "temporary"))) { }

            JObject root = FileAtVersion(1);
            Assert.Throws<SaveFormatException>(() => SaveMigrator.MigrateTo(root, 2));
        }

        /// <summary>
        /// The shipped list must be able to carry a version-1 file to the current version. A bump to
        /// CurrentVersion without the matching step lands here rather than in a player's save
        /// folder.
        /// </summary>
        [Test]
        public void ShippedMigrations_CoverEveryVersionUpToCurrent()
        {
            Assert.DoesNotThrow(() => SaveMigrator.Migrate(FileAtVersion(1)));
        }

        [Test]
        public void NullDocument_IsRefused()
        {
            Assert.Throws<SaveFormatException>(() => SaveMigrator.Migrate(null));
        }
    }
}
