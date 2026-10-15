using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A stretch of one chain — "from the step where the crew return to the hamlet, until the oath is
    /// reforged" — that a character's lines or a piece of the world follow.
    ///
    /// <para>
    /// Positions run <c>-1</c> (nobody has taken the chain on), <c>0 … n-1</c> (its steps), <c>n</c>
    /// (finished). <see cref="from"/> is the first position inside, <see cref="until"/> the first after.
    /// Both are step ids so a window survives steps being inserted; empty means open-ended, and
    /// <see cref="CompleteToken"/> names the finished chain.
    /// </para>
    /// </summary>
    [Serializable]
    public struct QuestStepWindow
    {
        /// <summary>Stands for "the chain is finished" in <see cref="from"/> or <see cref="until"/>.</summary>
        public const string CompleteToken = "#complete";

        public ObjectiveChain chain;

        [Tooltip("First step inside the window. Empty: from before anybody accepted the chain. " +
                 "'#complete': only once the chain is finished.")]
        public string from;

        [Tooltip("First step after the window. Empty: through the end of the chain and beyond. " +
                 "'#complete': up to, not including, the chain being finished.")]
        public string until;

        /// <summary>
        /// Whether the crew stand inside the window now. False outside a story world, where there is no
        /// quest to stand in.
        /// </summary>
        public bool IsInside => TryGetPosition(out int position) && Contains(position);

        /// <summary>
        /// Where the crew stand in this window's chain: -1 before anybody has taken it on, its step
        /// index, or the step count once finished. False without a director to ask.
        /// </summary>
        public bool TryGetPosition(out int position)
        {
            position = -1;
            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (chain == null || director == null) return false;

            Quest quest = director.Find(chain);
            if (quest != null) position = quest.Progress.Step;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="position"/> (see <see cref="TryGetPosition"/>) is inside the window. A
        /// misspelt step id is reported and treated as outside, so a typo shows up as a character who
        /// never says the line rather than one who says it forever.
        /// </summary>
        public bool Contains(int position)
        {
            if (chain == null) return false;

            int count = chain.Steps.Count;
            if (!TryResolve(from, -1, count, out int first) || !TryResolve(until, int.MaxValue, count, out int after))
                return false;

            return position >= first && position < after;
        }

        private bool TryResolve(string stepId, int open, int count, out int position)
        {
            if (string.IsNullOrEmpty(stepId)) { position = open; return true; }
            if (stepId == CompleteToken) { position = count; return true; }

            position = chain.IndexOf(stepId);
            if (position >= 0) return true;

            Debug.LogError($"[Quest] A window names step '{stepId}', which chain '{chain.name}' does not have.", chain);
            return false;
        }
    }
}
