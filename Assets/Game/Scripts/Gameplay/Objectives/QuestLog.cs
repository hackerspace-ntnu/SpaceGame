using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What the visor's objective panel can show: a title, status lines and somewhere to head. The current step of
    /// a <see cref="Quest"/> is one (<see cref="Quest.Objective"/>); a mission given out in the world is another.
    ///
    /// <para>EVERY machine, every frame: presentation only, derived from state the machine already holds.</para>
    /// </summary>
    public interface ITrackedObjective
    {
        string Title { get; }

        /// <summary>Lines under the title. May use TMP rich text. Empty for none.</summary>
        string Status { get; }

        bool TryGetWaypoint(out Vector3 position);
    }

    /// <summary>One line of the quest log: a quest the crew have taken on, and what it asks of them now.</summary>
    public interface IQuestEntry
    {
        /// <summary>The quest's name.</summary>
        string Name { get; }

        /// <summary>What the quest is about, for the quest log. Empty for none.</summary>
        string Summary { get; }

        bool IsComplete { get; }

        /// <summary>
        /// What the quest asks for now, or null once it is complete. A new object per step where the quest has
        /// steps, so whoever watches it can tell one step from the next.
        /// </summary>
        ITrackedObjective Objective { get; }
    }

    /// <summary>
    /// The quests open on this machine, in the order they were taken on, and which one the player is following.
    /// The visor shows the <see cref="Active"/> quest's objective; every other quest keeps its own progress all the
    /// same, because progress is the quest's and only the choice of what to show is the log's.
    ///
    /// <para>
    /// <b>The most recently acquired quest becomes active</b> (<see cref="Open"/> with <c>acquired</c>), and the
    /// player can make any other open, unfinished quest active from the quest log (<see cref="SetActive"/>). A
    /// chosen quest that completes or closes hands the visor back to the newest unfinished one.
    /// </para>
    /// <para>
    /// Per machine and not saved: each player follows the quest they choose, and a reload starts on the newest.
    /// </para>
    /// </summary>
    public static class QuestLog
    {
        private static readonly List<IQuestEntry> open = new();
        private static IQuestEntry chosen;

        /// <summary>A quest was opened, closed or made active on this machine.</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            open.Clear();
            chosen = null;
            Changed = null;
        }

        /// <summary>Every open quest, oldest first, finished ones included.</summary>
        public static IReadOnlyList<IQuestEntry> Entries => open;

        /// <summary>
        /// The quest the visor follows: the one last acquired or chosen while it is open and unfinished, else the
        /// newest open unfinished quest, else null.
        /// </summary>
        public static IQuestEntry Active
        {
            get
            {
                if (IsFollowable(chosen)) return chosen;

                for (int i = open.Count - 1; i >= 0; i--)
                    if (!open[i].IsComplete) return open[i];

                return null;
            }
        }

        /// <summary>
        /// Lists <paramref name="entry"/> on this machine. <paramref name="acquired"/> means the crew took it on
        /// just now, in play, which makes it the active quest; a quest restored from a save, learned on joining or
        /// streamed back in is listed without taking the visor. Idempotent.
        /// </summary>
        public static void Open(IQuestEntry entry, bool acquired)
        {
            if (entry == null) return;

            bool added = !open.Contains(entry);
            if (added) open.Add(entry);
            if (acquired) chosen = entry;
            if (added || acquired) Changed?.Invoke();
        }

        /// <summary>Takes <paramref name="entry"/> off this machine's log. Idempotent.</summary>
        public static void Close(IQuestEntry entry)
        {
            if (!open.Remove(entry)) return;

            if (chosen == entry) chosen = null;
            Changed?.Invoke();
        }

        public static bool IsOpen(IQuestEntry entry) => entry != null && open.Contains(entry);

        /// <summary>The player chose <paramref name="entry"/> to follow. False, and nothing changes, unless it is open and unfinished.</summary>
        public static bool SetActive(IQuestEntry entry)
        {
            if (!IsFollowable(entry)) return false;

            chosen = entry;
            Changed?.Invoke();
            return true;
        }

        private static bool IsFollowable(IQuestEntry entry) => entry != null && !entry.IsComplete && open.Contains(entry);
    }
}
