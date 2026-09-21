// An ordered errand somebody sends you on. Steps in order; the index IS the progress.
//
// The id is the asset GUID, stamped the way InventoryItem stamps its own and for the identical
// reason: a save records which questline a giver is running, and a record that named the asset by
// name or by list position would be orphaned the first time anybody renamed or reordered anything.
// [field: SerializeField] is load-bearing — without it OnValidate's value is editor-only and every
// built player ships a null id. See InventoryItem.ID for the full account of that failure.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.Gameplay.Quests
{
    [CreateAssetMenu(fileName = "Questline", menuName = "SpaceGame/Quests/Questline")]
    public class Questline : ScriptableObject, IRegistryEntry
    {
        /// <summary>The asset's GUID. Written into save files; never rename, never hand-edit.</summary>
        [field: SerializeField]
        public string ID { get; set; }

        [Tooltip("Shown in logs and in the inspector. Not currently shown to the player — the NPC's " +
                 "own lines carry the story.")]
        public string title = "Untitled";

        [Tooltip("Worked through in order, first to last.")]
        public List<QuestStep> steps = new();

        [TextArea(1, 3)]
        [Tooltip("Optional. Said once when the last step lands. After this the giver falls back to " +
                 "whatever dialog its prefab already had.")]
        public string completionLine = string.Empty;

        public int StepCount => steps?.Count ?? 0;

        /// <summary>The step at <paramref name="index"/>, or null when the questline is finished.</summary>
        public QuestStep StepAt(int index) =>
            steps != null && index >= 0 && index < steps.Count ? steps[index] : null;

        /// <summary>
        /// Everything wrong with this questline, for <c>TownGenerator.Verify</c>. Empty means it is
        /// fit to hand to somebody.
        /// </summary>
        public IEnumerable<string> Problems()
        {
            if (StepCount == 0)
            {
                yield return $"questline '{name}' has no steps";
                yield break;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                QuestStep step = steps[i];
                if (step == null)
                {
                    yield return $"questline '{name}' step {i} is null";
                    continue;
                }

                if (step.required == null)
                    yield return $"questline '{name}' step {i} asks for no item, so it can never complete";
                else if (string.IsNullOrEmpty(step.required.ID))
                    yield return $"questline '{name}' step {i}: item '{step.required.name}' has a blank ID — " +
                                 "re-import that asset, or it will resolve to nothing in a built player";

                if (step.reward != null && string.IsNullOrEmpty(step.reward.ID))
                    yield return $"questline '{name}' step {i}: reward '{step.reward.name}' has a blank ID — " +
                                 "re-import that asset";

                if (string.IsNullOrWhiteSpace(step.text))
                    yield return $"questline '{name}' step {i} has no line for the NPC to say";
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            string path = AssetDatabase.GetAssetPath(this);
            string guid = AssetDatabase.AssetPathToGUID(path);

            if (!string.IsNullOrEmpty(guid) && ID != guid)
            {
                ID = guid;
                EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}
