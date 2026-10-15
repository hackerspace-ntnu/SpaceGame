using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Shift a named thing off a place — "get the fallen log off the cottage door". Met while the
    /// thing (a <see cref="QuestActor"/>, here a loose physics body) stands outside the place, read
    /// live, so a log moved before anybody asked counts the moment the favour begins.
    ///
    /// <para>
    /// The thing must be loaded to be judged, which it is whenever somebody is close enough to move
    /// it. How it is moved — pushed, lassoed, leashed to a mount — is the player's business.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Move Away Step")]
    public class MoveAwayStep : ObjectiveStep
    {
        [Tooltip("The QuestActor id of the thing to move.")]
        [SerializeField] private string actorId;

        [Tooltip("The QuestSite id of the place it must leave. Its radius is how far counts as off it.")]
        [SerializeField] private string siteId;

        public override bool IsMet(ObjectiveWorld world) =>
            QuestActor.TryFind(actorId, out QuestActor thing)
            && QuestSite.TryGet(siteId, out Vector3 site, out float radius)
            && !QuestPlaces.IsInside(thing.transform.position, site, radius);

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            QuestPlaces.TryLocate(actorId, siteId, out position);
    }
}
