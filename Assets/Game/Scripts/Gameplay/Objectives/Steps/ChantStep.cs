using UnityEngine;
using SpaceGame.Gameplay.SpriteHamlet;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A <see cref="ChantCircle"/> chants its oath to the end during this step — "guard the
    /// Oath-Chant". The circle runs itself from the quest's progress; this only waits for it to finish
    /// and shows how far it has got.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Chant Step")]
    public class ChantStep : ObjectiveStep
    {
        [Tooltip("The id of the ChantCircle that must finish.")]
        [SerializeField] private string circleId;

        [Tooltip("Status line. {0} is verses finished, {1} the verse count.")]
        [SerializeField] private string statusFormat = "Oath verses  <b>{0}/{1}</b>";

        public override bool IsMet(ObjectiveWorld world) => world.HasMark(QuestMark.Chanted, circleId);

        public override string Status(ObjectiveWorld world) =>
            ChantCircle.TryFind(circleId, out ChantCircle circle)
                ? string.Format(statusFormat, Mathf.FloorToInt(circle.Progress), circle.Verses)
                : string.Empty;

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            bool found = ChantCircle.TryFind(circleId, out ChantCircle circle);
            position = found ? circle.Centre : default;
            return found;
        }
    }
}
