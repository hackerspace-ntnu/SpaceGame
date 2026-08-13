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
        public const string AutoSaveSlotId = "autosave";
        public const string QuickSaveSlotId = "quicksave";

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
        /// beats one that silently is not there.
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
                if (candidate.Unreadable) continue;
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
