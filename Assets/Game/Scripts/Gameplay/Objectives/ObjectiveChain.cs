using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A quest: the ordered steps a crew works through, and what the quest log calls it. The crew share their place
    /// in it. The story quest (<c>CrashSiteChain</c>) begins on landing; every other quest is listed in
    /// <see cref="ObjectiveDirector"/>'s catalog and begins when its <see cref="Trigger"/> fires or a
    /// <see cref="QuestGiver"/> gives it.
    ///
    /// <para>
    /// Steps are saved by <see cref="ObjectiveStep.Id"/>, never by position, so steps may be
    /// inserted, removed and reordered here without an existing save landing on the wrong one. The
    /// quest itself is saved and replicated by <see cref="Id"/>.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Quest")]
    public class ObjectiveChain : ScriptableObject
    {
        /// <summary>The longest id the wire carries (<c>FixedString64Bytes</c>), in UTF-8 bytes.</summary>
        public const int MaxIdBytes = 61;

        [Tooltip("Written into save files and sent over the wire as this quest. NEVER rename it once a save exists. " +
                 "Lower-case-with-dashes, at most 61 characters.")]
        [SerializeField] private string id;

        [Tooltip("The quest's name in the quest log and over the visor's objective panel.")]
        [SerializeField] private string title;

        [Tooltip("One or two sentences in the quest log: what the quest is about.")]
        [SerializeField, TextArea(2, 4)] private string summary;

        [Tooltip("Who reads this quest's briefings and remarks, shown in bold before each line. Empty: the " +
                 "briefing's default speaker, the lander.")]
        [SerializeField] private string speaker;

        [Tooltip("What begins this quest on the server. Empty: only a QuestGiver or code begins it. Ignored on " +
                 "the story quest, which begins when the crew land.")]
        [SerializeField] private QuestTrigger trigger;

        [SerializeField] private ObjectiveStep[] steps = Array.Empty<ObjectiveStep>();

        public string Id => id;
        public string Title => title;
        public string Summary => summary;
        public string Speaker => speaker;
        public QuestTrigger Trigger => trigger;

        public IReadOnlyList<ObjectiveStep> Steps => steps;

        /// <summary>Position of the step with this id, or -1.</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;

            for (int i = 0; i < steps.Length; i++)
                if (steps[i] != null && steps[i].Id == id) return i;

            return -1;
        }
    }
}
