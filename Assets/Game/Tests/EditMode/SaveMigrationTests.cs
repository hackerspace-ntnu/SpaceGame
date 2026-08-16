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
        /// Exercises the ladder with temporary steps against an explicit target.
        ///
        /// Every test below builds ABOVE <see cref="SaveDocument.CurrentVersion"/> rather than at
        /// version 1. The shipped list now has a real step out of 1, and
        /// <c>Migrations.FirstOrDefault</c> would hand these files to it instead of to the marker —
        /// so pinning them to 1 only worked while nothing had ever been migrated, and would break on
        /// each future bump. The rungs past the top of the ladder are always empty.
        /// </summary>
        private static int Top => SaveDocument.CurrentVersion;

        [Test]
        public void RegisteredSteps_RunInOrderAndAdvanceTheVersion()
        {
            using (SaveMigrator.RegisterScoped(new MarkerMigration(Top, "first")))
            using (SaveMigrator.RegisterScoped(new MarkerMigration(Top + 1, "second")))
            {
                JObject root = FileAtVersion(Top);

                SaveMigrator.MigrateTo(root, Top + 2);

                Assert.AreEqual(Top + 2, SaveMigrator.ReadVersion(root));
                Assert.AreEqual(new[] { "first", "second" }, root["trail"]?.ToObject<string[]>());
            }
        }

        [Test]
        public void RegisteredSteps_StopAtTheTargetVersion()
        {
            using (SaveMigrator.RegisterScoped(new MarkerMigration(Top, "first")))
            using (SaveMigrator.RegisterScoped(new MarkerMigration(Top + 1, "second")))
            {
                JObject root = FileAtVersion(Top);

                SaveMigrator.MigrateTo(root, Top + 1);

                Assert.AreEqual(Top + 1, SaveMigrator.ReadVersion(root));
                Assert.AreEqual(new[] { "first" }, root["trail"]?.ToObject<string[]>());
            }
        }

        [Test]
        public void MissingStep_ThrowsRatherThanLoadingAnUnmigratedFile()
        {
            // Only the step out of Top+1 exists, so a file at Top cannot reach Top+2.
            using (SaveMigrator.RegisterScoped(new MarkerMigration(Top + 1, "second")))
            {
                JObject root = FileAtVersion(Top);

                var error = Assert.Throws<SaveFormatException>(() => SaveMigrator.MigrateTo(root, Top + 2));
                StringAssert.Contains("no migration is", error.Message);
            }
        }

        [Test]
        public void RegisteredSteps_AreRemovedWhenTheirScopeEnds()
        {
            using (SaveMigrator.RegisterScoped(new MarkerMigration(Top, "temporary"))) { }

            JObject root = FileAtVersion(Top);
            Assert.Throws<SaveFormatException>(() => SaveMigrator.MigrateTo(root, Top + 1));
        }

        // ─────────────────────────────────────────────
        //  v1 → v2: per-scene records become one global, id-keyed registry
        // ─────────────────────────────────────────────

        /// <summary>
        /// The lift that lets an existing world keep its contents. Proven against the shape real v1
        /// files actually hold — checked against the save files on this machine before it was
        /// written, where four of five authored creatures were filed under a chunk they had wandered
        /// into while all of them are authored in persistentScene.
        /// </summary>
        [Test]
        public void V1Records_BecomeGlobalEntitiesKeyedByIdentity()
        {
            JObject root = JObject.Parse(@"{
              ""header"": { ""version"": 1 },
              ""world"": {
                ""scenes"": {
                  ""chunk:7,5"": {
                    ""entities"": [{ ""instanceId"": ""item-1"", ""prefabId"": ""p"", ""state"": {} }],
                    ""authored"": {
                      ""golem-1"": { ""entries"": { ""transform"": {
                          ""position"": { ""x"": 10.0, ""y"": 2.0, ""z"": 30.0 },
                          ""scale"": { ""x"": 1.0, ""y"": 1.0, ""z"": 1.0 } } } }
                    },
                    ""destroyedAuthored"": [""dead-1""]
                  }
                }
              }
            }");

            SaveMigrator.Migrate(root);

            Assert.IsNull(root["world"]["scenes"], "the per-scene partition should be gone");

            JToken item = root["world"]["entities"]["item-1"];
            Assert.AreEqual("chunk:7,5", (string)item["scene"], "routing information was lost");
            Assert.IsFalse((bool)item["authored"]);

            JToken golem = root["world"]["entities"]["golem-1"];
            Assert.IsTrue((bool)golem["authored"], "a migrated authored object would be respawned as a duplicate");
            Assert.AreEqual(10.0, (double)golem["position"]["x"], 0.001,
                            "the pose was not lifted out of the transform payload");

            Assert.AreEqual("dead-1", (string)root["world"]["destroyed"][0]);
        }

        /// <summary>
        /// The subtle half. A v1 authored record held no pose of its own, so one whose object had no
        /// transform saver has nowhere for a position to come from — and a defaulted zero is
        /// indistinguishable from an object that belongs at the origin. Left unmarked, the first load
        /// after the update would teleport every such object to (0,0,0).
        /// </summary>
        [Test]
        public void V1AuthoredRecordWithNoTransform_IsMarkedAsHavingNoPose()
        {
            JObject root = JObject.Parse(@"{
              ""header"": { ""version"": 1 },
              ""world"": { ""scenes"": { ""persistent"": {
                ""authored"": { ""crate-1"": { ""entries"": { ""health"": { ""current"": 5 } } } }
              } } }
            }");

            SaveMigrator.Migrate(root);

            Assert.IsFalse((bool)root["world"]["entities"]["crate-1"]["hasPose"]);
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
