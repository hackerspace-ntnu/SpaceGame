using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Pick up an artifact and use it once. The server leaves it on the sand by the wreck, as if it
    /// were thrown clear in the crash; met the first time anybody fires that item.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Try Artifact Step")]
    public class TryArtifactStep : PlacedItemStep
    {
        [Tooltip("Where it lands, in the hull's frame (+Z is the nose). In the open beside the " +
                 "wreck, where the crew will walk past it.")]
        [SerializeField] private Vector3 dropOffset = new(0f, 0f, 22f);

        protected override Vector3 ChooseOffset() => dropOffset;

        public override bool IsMet(ObjectiveWorld world) => world.WasUsed(item);
    }
}
