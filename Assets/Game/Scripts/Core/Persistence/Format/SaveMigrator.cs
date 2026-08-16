using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// One step up the version ladder, applied to the raw JSON before it is ever typed.
    ///
    /// Migrations work on <see cref="JObject"/> rather than on the DTOs deliberately: the DTOs only
    /// describe the CURRENT shape, so a migration written against them would silently see the new
    /// field names on old data and do nothing. The raw tree is the only place the old shape still
    /// exists.
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>The version this step reads. It produces <see cref="FromVersion"/> + 1.</summary>
        int FromVersion { get; }

        /// <summary>Rewrites <paramref name="root"/> in place. Must not touch the version field.</summary>
        void Apply(JObject root);
    }

    /// <summary>
    /// Walks a save file up to <see cref="SaveDocument.CurrentVersion"/>, one registered step at a
    /// time.
    ///
    /// A file from the future is refused rather than loaded. Reading it would mean guessing at
    /// fields this build has never seen, and the failure mode of guessing — a partially populated
    /// world that then gets SAVED BACK over the good file — destroys the player's progress. A
    /// clear refusal costs them one launch of the older build.
    /// </summary>
    public static class SaveMigrator
    {
        private static readonly List<ISaveMigration> Migrations = new()
        {
            // v1 filed world state under the scene an object happened to be in. v2 keys it by the
            // object's own identity, because the scene moves and the identity does not.
            new V1GlobalEntities(),
        };

        /// <summary>
        /// Brings <paramref name="root"/> to the current version in place.
        /// Throws <see cref="SaveFormatException"/> when the file is newer than this build, or when
        /// no registered step can move it forward.
        /// </summary>
        public static void Migrate(JObject root) => MigrateTo(root, SaveDocument.CurrentVersion);

        /// <summary>
        /// Runs the ladder up to an explicit target instead of <see cref="SaveDocument.CurrentVersion"/>.
        ///
        /// The target is a parameter so the ladder can be exercised at all. While the format is at
        /// version 1 there is no step to register and no gap to climb, and a mechanism that is only
        /// tested the day the first real migration is written is a mechanism that is untested on the
        /// day it first matters.
        /// </summary>
        public static void MigrateTo(JObject root, int targetVersion)
        {
            if (root == null) throw new SaveFormatException("Save file has no content to migrate.");

            int version = ReadVersion(root);

            if (version > targetVersion)
            {
                throw new SaveFormatException(
                    $"Save file version {version} was written by a newer build of the game " +
                    $"(this build reads up to version {targetVersion}). " +
                    "Loading it would discard state this build cannot see.");
            }

            while (version < targetVersion)
            {
                ISaveMigration step = Migrations.FirstOrDefault(m => m.FromVersion == version);

                if (step == null)
                {
                    throw new SaveFormatException(
                        $"Save file version {version} cannot be upgraded: no migration is " +
                        $"registered for it (target version {targetVersion}).");
                }

                step.Apply(root);
                version++;
                WriteVersion(root, version);
            }
        }

        /// <summary>
        /// The declared version, or 1 for a file that predates the field. Zero and negative values
        /// are treated the same way — a save that never wrote a version is by definition the first
        /// format, and starting the ladder below 1 would look for a migration that cannot exist.
        /// </summary>
        public static int ReadVersion(JObject root)
        {
            JToken header = root?["header"];
            JToken version = header?["version"];

            if (version == null || version.Type != JTokenType.Integer) return 1;

            int value = version.Value<int>();
            return value < 1 ? 1 : value;
        }

        private static void WriteVersion(JObject root, int version)
        {
            if (root["header"] is not JObject header)
            {
                header = new JObject();
                root["header"] = header;
            }

            header["version"] = version;
        }

        /// <summary>
        /// Registers a step for the duration of the returned handle's life. This exists so the
        /// ladder itself can be tested without shipping a fake migration in
        /// <see cref="Migrations"/>; real migrations belong in that list, not here.
        /// </summary>
        public static System.IDisposable RegisterScoped(ISaveMigration migration)
        {
            Migrations.Add(migration);
            return new Registration(migration);
        }

        private class Registration : System.IDisposable
        {
            private readonly ISaveMigration migration;
            public Registration(ISaveMigration migration) => this.migration = migration;
            public void Dispose() => Migrations.Remove(migration);
        }
    }
}
