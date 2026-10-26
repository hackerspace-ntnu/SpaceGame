using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What a quest step remembers beyond <see cref="ObjectiveWorld"/>'s own per-step memory: the
    /// <see cref="QuestMark"/>s that landed during it, and when this machine first saw it.
    ///
    /// <para>
    /// Kept per quest, keyed by the quest's <see cref="ObjectiveWorld"/> — the one handle a stateless step
    /// is given — and wiped whenever that quest's step index moves on, so a mark counts only for the step
    /// that was current when it landed. A quest restored from a save or adopted on joining is a new
    /// <see cref="Quest"/> with a new world, so it starts with nothing remembered, like the world's own memory.
    /// </para>
    /// </summary>
    public static class QuestStepMemory
    {
        private sealed class Memory
        {
            public int Step = -1;
            public float Since;
            public readonly HashSet<(QuestMark, string)> Marks = new();
        }

        private static readonly ConditionalWeakTable<ObjectiveWorld, Memory> Memories = new();

        /// <summary>SERVER: <paramref name="mark"/> happened to <paramref name="id"/>; every unfinished quest's current step hears it.</summary>
        internal static void Record(QuestMark mark, string id)
        {
            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (director == null) return;

            foreach (Quest quest in director.Quests)
                if (!quest.IsComplete) Of(quest.World, quest).Marks.Add((mark, id));
        }

        /// <summary>Whether <paramref name="mark"/> happened to <paramref name="id"/> during the quest's current step.</summary>
        public static bool HasMark(this ObjectiveWorld world, QuestMark mark, string id)
        {
            Memory memory = Of(world);
            return memory != null && memory.Marks.Contains((mark, id));
        }

        /// <summary>
        /// EVERY machine: seconds since this machine first asked about the quest's current step. A step timed
        /// from here starts its clock again after a reload or a join.
        /// </summary>
        public static float StepSeconds(this ObjectiveWorld world)
        {
            Memory memory = Of(world);
            return memory != null ? Time.time - memory.Since : 0f;
        }

        private static Memory Of(ObjectiveWorld world)
        {
            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (world == null || director == null) return null;

            foreach (Quest quest in director.Quests)
                if (quest.World == world) return Of(world, quest);

            return null;
        }

        private static Memory Of(ObjectiveWorld world, Quest quest)
        {
            Memory memory = Memories.GetOrCreateValue(world);
            if (memory.Step == quest.Progress.Step) return memory;

            memory.Step = quest.Progress.Step;
            memory.Since = Time.time;
            memory.Marks.Clear();
            return memory;
        }
    }
}
