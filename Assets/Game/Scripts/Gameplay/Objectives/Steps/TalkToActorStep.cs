using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Somebody in the crew finishes a conversation with one character during this step — "tell the
    /// Elder what you found". The character's <see cref="QuestDialog"/> decides what is said; this
    /// only waits for the server to hear that it was.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Talk To Actor Step")]
    public class TalkToActorStep : ObjectiveStep
    {
        [Tooltip("The QuestActor id of the character to talk to.")]
        [SerializeField] private string actorId;

        [Tooltip("Where the waypoint points while the character's chunk is not loaded. Optional.")]
        [SerializeField] private string fallbackSiteId;

        public override bool IsMet(ObjectiveWorld world) => world.HasMark(QuestMark.Talked, actorId);

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            QuestPlaces.TryLocate(actorId, fallbackSiteId, out position);
    }
}
