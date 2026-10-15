using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Something fire clears away is burned away during this step — "burn the growth out of the
    /// well". The thing is a <c>BurnAway</c> naming the same <see cref="markId"/>, and it should only
    /// take flame while this step runs, or the mark lands in no step.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Burn Step")]
    public class BurnStep : ObjectiveStep
    {
        [Tooltip("The questMarkId on the BurnAway to wait for.")]
        [SerializeField] private string markId;

        [Tooltip("Where the waypoint points. Optional.")]
        [SerializeField] private string siteId;

        public override bool IsMet(ObjectiveWorld world) => world.HasMark(QuestMark.Burned, markId);

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            QuestSite.TryGet(siteId, out position, out _);
    }
}
