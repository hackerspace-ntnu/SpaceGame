using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// One character is beaten during this step — killed ("defeat Morwen Crookhat") or brought down
    /// alive ("bring Merob down, don't destroy him"), as <see cref="defeat"/> says. The status line is
    /// the character's own (<see cref="IQuestStatus"/>), when it has one.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Defeat Actor Step")]
    public class DefeatActorStep : ObjectiveStep
    {
        public enum Defeat { Killed, Subdued }

        [Tooltip("The QuestActor id of the character to beat.")]
        [SerializeField] private string actorId;

        [SerializeField] private Defeat defeat = Defeat.Killed;

        [Tooltip("Where the waypoint points while the character's chunk is not loaded. Optional.")]
        [SerializeField] private string fallbackSiteId;

        public override bool IsMet(ObjectiveWorld world)
        {
            if (defeat == Defeat.Killed) return world.HasMark(QuestMark.Dead, actorId);

            // The live state as well as the mark: beaten down before this step began still counts.
            return world.HasMark(QuestMark.Subdued, actorId)
                   || (QuestActor.TryFind(actorId, out QuestActor actor)
                       && actor.TryGetComponent(out ISubduable subduable) && subduable.IsSubdued);
        }

        public override string Status(ObjectiveWorld world) =>
            QuestActor.TryFind(actorId, out QuestActor actor) && actor.TryGetComponent(out IQuestStatus status)
                ? status.QuestStatus
                : string.Empty;

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            QuestPlaces.TryLocate(actorId, fallbackSiteId, out position);
    }
}
