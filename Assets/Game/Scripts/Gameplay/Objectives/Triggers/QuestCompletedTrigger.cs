using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>Begins the quest once another quest is finished: a follow-up, a next chapter.</summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Triggers/Quest Completed")]
    public class QuestCompletedTrigger : QuestTrigger
    {
        [Tooltip("The quest that must be finished first. The story quest or one from the director's catalog.")]
        [SerializeField] private ObjectiveChain after;

        public override bool ShouldStart(ObjectiveDirector director) =>
            director.Find(after) is { IsComplete: true };
    }
}
