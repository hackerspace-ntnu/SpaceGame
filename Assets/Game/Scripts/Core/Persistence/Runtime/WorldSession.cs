using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Which world this session is playing, and the document it starts from.
    ///
    /// Replaces SaveManager's PendingLoad/StageLoad/ClearStagedLoad. Those expressed "new game" as
    /// the ABSENCE of a staged document, so every entry point had to independently know that
    /// convention — and one that forgot silently turned New Game into Continue. Here the choice is
    /// a value: <see cref="IsNew"/> is true or a document is staged, never ambiguous.
    ///
    /// Static for the same reason PendingLoad was: nothing survives the scene load between the menu
    /// and the world. The menu's SaveManager dies with the menu scene and the world's does not
    /// exist yet, so the choice cannot live on either.
    /// </summary>
    public static class WorldSession
    {
        /// <summary>The sanitized slot id — the save's file name.</summary>
        public static string WorldId { get; private set; }

        /// <summary>What the player typed, shown in menus.</summary>
        public static string DisplayName { get; private set; }

        /// <summary>The WorldStreamingConfig GUID this world belongs to.</summary>
        public static string WorldConfigId { get; private set; }

        /// <summary>True when this world has no save behind it yet.</summary>
        public static bool IsNew { get; private set; }

        /// <summary>
        /// False before any world has been chosen — a world scene opened directly in the editor,
        /// with no menu run behind it.
        /// </summary>
        public static bool IsActive => !string.IsNullOrEmpty(WorldId);

        private static SaveDocument staged;

        /// <summary>Begins a world with no save behind it.</summary>
        public static void StageNew(string displayName, WorldStreamingConfig config)
        {
            WorldId = WorldIdentity.IdFor(displayName);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? WorldId : displayName.Trim();
            WorldConfigId = config != null ? config.ConfigId : string.Empty;
            IsNew = true;
            staged = null;
        }

        /// <summary>
        /// Reads a world's save and stages it for the next world load.
        ///
        /// Stages nothing on failure, so a refused load leaves the previous choice untouched rather
        /// than half-applied. <paramref name="config"/> is the world being loaded INTO — the caller
        /// supplies it because WorldSession must not depend on how a config is found (the menu has
        /// a serialized reference, SaveHotkeys asks the live streamer).
        /// </summary>
        public static bool StageExisting(string worldId, WorldStreamingConfig config, out string error)
        {
            var slots = new SaveSlots(SaveManager.DefaultRoot);
            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor(worldId));

            switch (result.Outcome)
            {
                case SaveFileStore.ReadOutcome.RecoveredFromBackup:
                    Debug.LogWarning($"[Save] {result.Error}");
                    break;

                case SaveFileStore.ReadOutcome.Ok:
                    break;

                case SaveFileStore.ReadOutcome.Missing:
                    error = $"There is no world called '{worldId}'.";
                    return false;

                default:
                    error = result.Error ?? $"World '{worldId}' could not be read.";
                    return false;
            }

            SaveDocument document = result.Document.Normalized();
            string configId = config != null ? config.ConfigId : string.Empty;

            if (!WorldIdentity.AcceptsConfig(document.Header, configId, out error))
                return false;

            WorldId = WorldIdentity.IdFor(worldId);
            DisplayName = WorldIdentity.DisplayNameFor(document.Header, worldId);
            WorldConfigId = document.Header.WorldConfigId;
            IsNew = false;
            staged = document;

            error = null;
            return true;
        }

        /// <summary>
        /// Takes the staged document, leaving the world active but the document consumed.
        ///
        /// Consumed rather than merely read so a second world load in the same process cannot
        /// silently re-apply a document the player has already moved on from. Null for a new world.
        /// </summary>
        public static SaveDocument Consume()
        {
            SaveDocument document = staged;
            staged = null;
            return document;
        }

        /// <summary>Forgets the world entirely, for a return to the main menu.</summary>
        public static void Clear()
        {
            WorldId = null;
            DisplayName = null;
            WorldConfigId = null;
            IsNew = false;
            staged = null;
        }
    }
}
