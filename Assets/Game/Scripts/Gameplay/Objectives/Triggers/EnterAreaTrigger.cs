using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Begins the quest the first time any member of the crew comes within <see cref="radius"/> of a fixed point in
    /// the world — a ruin, a crater, a stretch of desert that is no settlement. For somewhere that already has a
    /// GameObject in a chunk, a <see cref="QuestGiver"/> with a radius there is the same thing, placed by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Triggers/Enter Area")]
    public class EnterAreaTrigger : QuestTrigger
    {
        [Tooltip("The middle of the area, world space. Only its X and Z count.")]
        [SerializeField] private Vector3 centre;

        [Tooltip("How far from the centre counts as in the area, metres, over the ground.")]
        [SerializeField, Min(1f)] private float radius = 50f;

        public override bool ShouldStart(ObjectiveDirector director) =>
            director.Lookups.NearestCrewDistance(centre) <= radius;
    }
}
