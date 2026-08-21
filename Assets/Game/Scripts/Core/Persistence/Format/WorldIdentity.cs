namespace SpaceGame.Persistence
{
    /// <summary>
    /// The rules that decide what a world is called and whether a save belongs to the world being
    /// entered.
    ///
    /// Deliberately pure and in the Format assembly: this is the logic a wrong answer corrupts a
    /// session with, so it has to be reachable by a test that does not open Unity. It knows nothing
    /// about ScriptableObjects — the caller resolves a config to its id and passes the string in.
    /// </summary>
    public static class WorldIdentity
    {
        /// <summary>
        /// The file name a world is stored under, derived from what the player typed.
        ///
        /// Shares <see cref="SaveSlots.Sanitize"/> rather than repeating it, so there is exactly one
        /// answer to "can a typed world name escape the save directory".
        /// </summary>
        public static string IdFor(string displayName) => SaveSlots.Sanitize(displayName);

        /// <summary>
        /// Whether a save may be loaded into the world identified by <paramref name="configId"/>.
        ///
        /// A mismatch is refused rather than repaired. The chunk deltas in a save are keyed by scene
        /// name and grid coordinate, so hydrating world 2's save onto world 1's grid does not fail
        /// loudly — it scatters objects into whatever scenes happen to share those keys.
        ///
        /// An empty id on the save is accepted: files written before world selection existed carry
        /// no id and belong to the only world there was.
        /// </summary>
        public static bool AcceptsConfig(SaveHeader header, string configId, out string error)
        {
            string saved = header?.WorldConfigId;

            if (string.IsNullOrEmpty(saved) || saved == configId)
            {
                error = null;
                return true;
            }

            error = "This save belongs to a different world and cannot be loaded here.";
            return false;
        }

        /// <summary>What to show in a world list, falling back to the file name for legacy saves.</summary>
        public static string DisplayNameFor(SaveHeader header, string slotId) =>
            string.IsNullOrEmpty(header?.WorldName) ? slotId : header.WorldName;

        /// <summary>
        /// Whether a player-typed name may be used for a new world, and why not when it may not.
        ///
        /// Lives here rather than in the screen that asks the question because the interesting case
        /// is not the empty box — it is that two different names can be the same FILE.
        /// <see cref="IdFor"/> strips everything that is not plainly a name, so "Dune Camp!" and
        /// "Dune Camp" collide, and creating the second would overwrite the first. The existence
        /// check therefore has to run against the id, never against what was typed.
        ///
        /// Rejecting rather than overwriting is deliberate: create and load are one keystroke apart
        /// on this screen and only one of them is recoverable.
        /// </summary>
        public static bool ValidateNewName(string typed, SaveSlots slots, out string error)
        {
            if (string.IsNullOrWhiteSpace(typed))
            {
                error = "Type a name for the new world.";
                return false;
            }

            if (SaveSlots.IsLegacySlotId(typed))
            {
                // Not merely reserved: on a case-insensitive filesystem this name IS the legacy
                // file, which the world list hides — so the world would be created and then be
                // invisible, which is worse than being refused.
                error = $"'{typed.Trim()}' is a reserved name. Pick another.";
                return false;
            }

            if (slots != null && slots.Exists(IdFor(typed)))
            {
                error = $"A world called '{typed.Trim()}' already exists.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
