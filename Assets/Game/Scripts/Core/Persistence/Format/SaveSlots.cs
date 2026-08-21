using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpaceGame.Persistence
{
    /// <summary>What a load menu needs to draw one row, without loading the world behind it.</summary>
    public readonly struct SaveSlotInfo
    {
        public readonly string SlotId;
        public readonly string Path;
        public readonly SaveHeader Header;

        /// <summary>True when the file exists but its header could not be read.</summary>
        public readonly bool Unreadable;

        public SaveSlotInfo(string slotId, string path, SaveHeader header, bool unreadable)
        {
            SlotId = slotId;
            Path = path;
            Header = header;
            Unreadable = unreadable;
        }

        public DateTime SavedAtUtc => Header?.SavedAtUtc ?? DateTime.MinValue;

        /// <summary>
        /// True when this save was written by a build newer than this one.
        ///
        /// Its header parses perfectly — <c>TryReadHeader</c> deliberately does not migrate — so it
        /// listed as a healthy, loadable world and, being the most recent, was exactly what
        /// "Continue" picked. The load then failed inside the migrator, fell through to the backup
        /// (also from the future), and reported "save unreadable, backup also unreadable" for a file
        /// that is neither. Naming the real reason is the whole fix.
        /// </summary>
        public bool FromNewerBuild => Header != null && Header.Version > SaveDocument.CurrentVersion;

        /// <summary>Whether this slot can actually be entered.</summary>
        public bool Loadable => !Unreadable && !FromNewerBuild;

        public string Label =>
            !string.IsNullOrEmpty(Header?.SlotLabel) ? Header.SlotLabel : SlotId;
    }

    /// <summary>
    /// Maps slot ids to files under a root directory, and lists what is there.
    ///
    /// The root is injected rather than read from <c>Application.persistentDataPath</c> so the whole
    /// class runs in an EditMode test against a temp directory — a save system whose file handling
    /// can only be exercised by launching the game is a save system nobody exercises.
    /// </summary>
    public class SaveSlots
    {
        public const string Extension = ".json";

        /// <summary>
        /// Slot ids the game no longer writes, and must never show.
        ///
        /// Before world selection there was one save per KIND rather than one per world: anything
        /// saved with no world chosen went to "autosave", and F5 went to a global "quicksave".
        /// Both are now the active world's own file, and there is no fallback slot at all — but the
        /// files those builds wrote are still on players' disks, and the world list is literally
        /// "every .json in this folder". Without this, a machine that ever ran an older build shows
        /// a world called "autosave" that nobody made.
        ///
        /// Case-insensitive because the platforms this ships on have case-insensitive filesystems:
        /// a world named "Autosave" would be the same FILE as the legacy one, not a second world.
        /// <see cref="WorldIdentity.ValidateNewName"/> refuses these names for that reason.
        /// </summary>
        private static readonly HashSet<string> LegacySlotIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "autosave",
            "quicksave",
        };

        /// <summary>Whether a slot id belongs to a build that predates world selection.</summary>
        public static bool IsLegacySlotId(string slotId) =>
            !string.IsNullOrWhiteSpace(slotId) && LegacySlotIds.Contains(Sanitize(slotId));

        public string Root { get; }

        public SaveSlots(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Save root is empty.", nameof(root));
            Root = root;
        }

        public string PathFor(string slotId) => Path.Combine(Root, Sanitize(slotId) + Extension);

        public bool Exists(string slotId) => SaveFileStore.Exists(PathFor(slotId));

        public void Delete(string slotId) => SaveFileStore.Delete(PathFor(slotId));

        /// <summary>
        /// Every slot on disk, newest first, so a "Continue" button is just <c>List()[0]</c>.
        /// Unreadable files are listed rather than hidden: a save the player can see and delete
        /// beats one that silently is not there. <see cref="LegacySlotIds"/> are the one exception
        /// — those are not worlds and never were.
        /// </summary>
        public List<SaveSlotInfo> List()
        {
            var slots = new List<SaveSlotInfo>();
            if (!Directory.Exists(Root)) return slots;

            string[] files;
            try
            {
                files = Directory.GetFiles(Root, "*" + Extension, SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return slots;
            }

            foreach (string file in files)
            {
                string slotId = Path.GetFileNameWithoutExtension(file);
                if (IsLegacySlotId(slotId)) continue;

                bool readable = SaveFileStore.TryReadHeader(file, out SaveHeader header);
                slots.Add(new SaveSlotInfo(slotId, file, header, !readable));
            }

            // A save with no readable header sorts to the bottom rather than to the top, where
            // DateTime.MinValue would otherwise put it if the comparison ran the other way.
            slots.Sort((a, b) => b.SavedAtUtc.CompareTo(a.SavedAtUtc));
            return slots;
        }

        /// <summary>The most recently written slot, for Continue. False when nothing loadable exists.</summary>
        public bool TryGetMostRecent(out SaveSlotInfo slot)
        {
            foreach (SaveSlotInfo candidate in List())
            {
                // Not merely "readable". A save from a newer build parses its header and fails at
                // load, so Continue used to pick it every time and then report a misleading
                // corruption error.
                if (!candidate.Loadable) continue;
                slot = candidate;
                return true;
            }

            slot = default;
            return false;
        }

        /// <summary>
        /// Turns a slot id into something safe to put in a path.
        ///
        /// Slot ids reach this from player-typed save names, so they can contain separators and
        /// <c>..</c>. Stripping every character that is not plainly a name means a slot id can
        /// never address a file outside <see cref="Root"/>.
        /// </summary>
        public static string Sanitize(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId)) return "save";

            var builder = new StringBuilder(slotId.Length);
            foreach (char c in slotId)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ')
                    builder.Append(c);
            }

            string result = builder.ToString().Trim();
            return result.Length == 0 ? "save" : result;
        }
    }
}
