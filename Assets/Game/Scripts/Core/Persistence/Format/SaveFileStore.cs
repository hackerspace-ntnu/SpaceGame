using System;
using System.IO;
using System.Text;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// Reads and writes save files without ever leaving the player without one.
    ///
    /// A save file is the single artefact a player cannot afford to lose, and the naive
    /// <c>File.WriteAllText(path, json)</c> truncates the existing file before it writes a byte —
    /// so a crash, a power cut, or a killed process anywhere in that window leaves a zero-length
    /// file where hours of play used to be. This class never writes to the live path:
    ///
    ///   1. serialize to <c>&lt;name&gt;.tmp</c> and flush it all the way to the disk;
    ///   2. <see cref="File.Replace(string,string,string)"/> the live file with it, which moves the
    ///      previous contents to <c>&lt;name&gt;.bak</c> in the same operation;
    ///   3. on read, fall through to the <c>.bak</c> when the live file is missing or unreadable.
    ///
    /// The worst case is therefore losing the newest save, never the one before it.
    /// </summary>
    public static class SaveFileStore
    {
        public const string TempSuffix = ".tmp";
        public const string BackupSuffix = ".bak";

        /// <summary>Result of a read attempt. A missing save is a normal answer, not an error.</summary>
        public enum ReadOutcome
        {
            Ok,
            Missing,
            /// <summary>The live file was unusable and the backup was loaded instead.</summary>
            RecoveredFromBackup,
            /// <summary>Both the live file and the backup failed. <see cref="ReadResult.Error"/> says how.</summary>
            Corrupt,
        }

        public readonly struct ReadResult
        {
            public readonly ReadOutcome Outcome;
            public readonly SaveDocument Document;
            public readonly string Error;

            public ReadResult(ReadOutcome outcome, SaveDocument document, string error)
            {
                Outcome = outcome;
                Document = document;
                Error = error;
            }

            public bool HasDocument => Document != null;
        }

        /// <summary>
        /// Writes <paramref name="document"/> to <paramref name="path"/> atomically.
        /// Safe to call from a background thread — it touches no Unity API.
        /// </summary>
        public static void Write(string path, SaveDocument document, bool pretty = true)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Save path is empty.", nameof(path));
            if (document == null) throw new ArgumentNullException(nameof(document));

            Write(path, SaveSerializer.ToJson(document, pretty));
        }

        /// <summary>Writes already-serialized text atomically. Split out so serialization can happen on the main thread.</summary>
        public static void Write(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = path + TempSuffix;
            string backup = path + BackupSuffix;

            WriteAllTextDurable(temp, json);

            if (File.Exists(path))
            {
                // File.Replace performs the swap and the backup rotation as one operation, and —
                // unlike Delete-then-Move — leaves no instant where neither file exists.
                File.Replace(temp, path, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        /// <summary>
        /// Loads the document at <paramref name="path"/>, falling back to its backup.
        ///
        /// Never throws for a bad file. A corrupt save is something the load menu has to render and
        /// explain, and an exception at this level would take the menu down with it.
        /// </summary>
        public static ReadResult Read(string path)
        {
            string backup = path + BackupSuffix;

            bool liveExists = File.Exists(path);
            bool backupExists = File.Exists(backup);

            if (!liveExists && !backupExists)
                return new ReadResult(ReadOutcome.Missing, null, null);

            string liveError = null;

            // Declared up here rather than inline in the condition below: `&&` short-circuits, so
            // when backupExists is false TryReadFile never runs and an inline `out string` would be
            // unassigned in the else-if branch that reads it (CS0165).
            string backupError = null;

            if (liveExists && TryReadFile(path, out SaveDocument document, out liveError))
                return new ReadResult(ReadOutcome.Ok, document, null);

            if (backupExists && TryReadFile(backup, out SaveDocument recovered, out backupError))
            {
                return new ReadResult(ReadOutcome.RecoveredFromBackup, recovered,
                    $"Primary save was unreadable ({liveError}); loaded the backup instead.");
            }
            else if (backupExists)
            {
                return new ReadResult(ReadOutcome.Corrupt, null,
                    $"Save unreadable ({liveError}); backup also unreadable ({backupError}).");
            }

            return new ReadResult(ReadOutcome.Corrupt, null, liveError);
        }

        /// <summary>The header alone, for listing slots without paying to parse whole worlds.</summary>
        public static bool TryReadHeader(string path, out SaveHeader header)
        {
            header = null;

            foreach (string candidate in new[] { path, path + BackupSuffix })
            {
                if (!File.Exists(candidate)) continue;

                try
                {
                    if (SaveSerializer.TryReadHeader(File.ReadAllText(candidate, Encoding.UTF8), out header))
                        return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }
            }

            return false;
        }

        /// <summary>
        /// True when writing <paramref name="candidate"/> over the save at <paramref name="path"/>
        /// would replace a NEWER format with an older one.
        ///
        /// <c>SaveMigrator</c> already refuses to LOAD a file from the future, and its comment says
        /// why: a partially understood world that then gets saved back over the good file. But the
        /// save path never checked, so the refusal only ever protected the first half. Launch an
        /// older build against a world last played on a newer one, wait out one autosave interval,
        /// and the newer save is gone — replaced by a document in a format that cannot express it.
        ///
        /// The header alone is enough to answer this and costs nothing, which matters because it
        /// runs on every autosave.
        /// </summary>
        public static bool WouldDowngradeFormat(string path, SaveDocument candidate)
        {
            if (candidate?.Header == null) return false;
            if (!TryReadHeader(path, out SaveHeader existing) || existing == null) return false;

            return existing.Version > candidate.Header.Version;
        }

        /// <summary>Removes a save and everything that shadows it, so a deleted slot cannot come back from its backup.</summary>
        public static void Delete(string path)
        {
            foreach (string candidate in new[] { path, path + BackupSuffix, path + TempSuffix })
            {
                try
                {
                    if (File.Exists(candidate)) File.Delete(candidate);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static bool Exists(string path) => File.Exists(path) || File.Exists(path + BackupSuffix);

        /// <summary>
        /// True when writing <paramref name="candidate"/> over the save at <paramref name="path"/>
        /// would trade a file that remembers players for one that does not.
        ///
        /// This is a last line of defence against a whole family of faults rather than one bug: any
        /// path that produces a technically-valid but player-less document — a half-initialised
        /// manager, a capture that ran after the player was torn down, a session that quit before
        /// anyone spawned — writes a file that loads into an empty world and silently ends the
        /// player's progress. There is no legitimate reason to replace a save containing players
        /// with one containing none, so the write is refused instead.
        ///
        /// Deliberately narrow: a first save, or one that keeps at least one player, is never
        /// blocked. Only the strict downgrade is.
        /// </summary>
        public static bool WouldDiscardAllPlayers(string path, SaveDocument candidate)
        {
            if (candidate == null) return true;

            // The overwhelmingly common case, and the one that must stay free: this save has
            // players, so it cannot be discarding any. Answering it first is what keeps the
            // expensive branch below off the autosave path entirely.
            if (candidate.Players is { Count: > 0 }) return false;

            if (!Exists(path)) return false;

            // What we last wrote here ourselves, if we know. A header-only read would not do — the
            // player list lives in the body — so the alternative is parsing the whole previous world
            // on the main thread, inside the frame that is already serializing the current one. That
            // happened on every single save, autosaves included, and it is the one part of "the
            // write is off-thread" that was never true.
            if (LastWrittenPlayerCount.TryGetValue(path, out int known)) return known > 0;

            ReadResult existing = Read(path);
            bool hadPlayers = existing.HasDocument && existing.Document.Players is { Count: > 0 };

            LastWrittenPlayerCount[path] = hadPlayers ? 1 : 0;
            return hadPlayers;
        }

        /// <summary>
        /// How many player records the file at each path last held, as far as this process knows.
        ///
        /// Only ever populated from a write we performed or a read we already paid for, so it cannot
        /// disagree with the disk unless another process edits the save behind us — and in that case
        /// the guard it feeds is a safety net, not a correctness requirement.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, int> LastWrittenPlayerCount = new();

        /// <summary>Records what a completed write put on disk, so the next guard need not re-read it.</summary>
        public static void NotifyWritten(string path, SaveDocument document)
        {
            if (string.IsNullOrEmpty(path)) return;
            LastWrittenPlayerCount[path] = document?.Players?.Count ?? 0;
        }

        private static bool TryReadFile(string path, out SaveDocument document, out string error)
        {
            document = null;
            error = null;

            try
            {
                document = SaveSerializer.FromJson(File.ReadAllText(path, Encoding.UTF8));
                return true;
            }
            catch (SaveFormatException e)
            {
                error = e.Message;
            }
            catch (IOException e)
            {
                error = $"could not be read from disk: {e.Message}";
            }
            catch (UnauthorizedAccessException e)
            {
                error = $"access denied: {e.Message}";
            }
            catch (Exception e)
            {
                // The catch-all is the point, and its absence broke this class's own contract.
                //
                // Read is documented as never throwing for a bad file, because the load menu has to
                // be able to render a "corrupt save" row rather than take an unhandled exception.
                // But only three exception types were caught, and the migration ladder runs inside
                // SaveSerializer.FromJson OUTSIDE any guard of its own: V1GlobalEntities does
                // unchecked casts like (string)entity["instanceId"], so a hand-edited or truncated
                // v1 file where that token is an object throws InvalidCastException — which sailed
                // straight through here, through Read, and out of SaveSlots into the menu.
                //
                // A file we cannot parse is a corrupt file, whatever the reason.
                error = $"could not be understood: {e.GetBaseException().Message}";
            }

            return false;
        }

        /// <summary>
        /// Writes and forces the bytes out of the OS write-back cache before returning.
        ///
        /// Without the <c>Flush(true)</c> the rename in <see cref="Write(string,string)"/> can reach
        /// the disk before the temp file's contents do, and a crash in between produces the exact
        /// failure this class exists to prevent: a live save file full of zeroes.
        /// </summary>
        private static void WriteAllTextDurable(string path, string contents)
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                                              bufferSize: 64 * 1024, useAsync: false);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
    }
}
