using System;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The clips a humanoid stands, walks, crouches, falls and sits with — the Base Layer of the
    /// generated controller, as data.
    ///
    /// <para>
    /// The profile's <see cref="HumanoidAnimationProfile.Locomotion"/> set is the controller's own.
    /// Every other set is a VARIANT: the builder turns it into an AnimatorOverrideController that
    /// swaps the default set's clips for its own, slot for slot, so an archetype (a formal walk, a
    /// jog) is one asset and one controller reference on the prefab. In a variant an empty slot
    /// keeps the default clip, and cell positions are ignored — the blend tree is the default's.
    /// </para>
    /// <para>
    /// The move cells sit in the animator's SpeedX/SpeedY space, where walk is 4 and run is 7.2.
    /// Every humanoid prefab's AgentAnimatorDriver speed multiplier is tuned against those two
    /// numbers, so moving them makes every NPC skate.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Locomotion Set", fileName = "Locomotion")]
    public sealed class LocomotionSet : ScriptableObject
    {
        [Serializable]
        public struct Cell
        {
            public AnimationClip clip;

            [Tooltip("Where the clip sits in (SpeedX, SpeedY).")]
            public Vector2 position;
        }

        [Tooltip("Standing idles, blended by IdleIndex 0..n-1. The idle blend sits at the centre " +
                 "of the move tree.")]
        [SerializeField] private AnimationClip[] idles = Array.Empty<AnimationClip>();

        [Tooltip("Walk and run clips around the idle, in (SpeedX, SpeedY).")]
        [SerializeField] private Cell[] move = Array.Empty<Cell>();

        [Tooltip("Crouched idle and crouched movement, in (SpeedX, SpeedY) of the crouch tree.")]
        [SerializeField] private Cell[] crouch = Array.Empty<Cell>();

        [Tooltip("Leaving the ground, blended with Fall by FallSpeed (-1 rising, 1 falling).")]
        [SerializeField] private AnimationClip jumpUp;

        [SerializeField] private AnimationClip fall;

        [Tooltip("The hard landing, played when a fall leaves the body immobilised.")]
        [SerializeField] private AnimationClip land;

        [Tooltip("Seated on a chair, a bench or a vessel seat (the Seated bool).")]
        [SerializeField] private AnimationClip sit;

        public AnimationClip[] Idles => idles;
        public Cell[] Move => move;
        public Cell[] Crouch => crouch;
        public AnimationClip JumpUp => jumpUp;
        public AnimationClip Fall => fall;
        public AnimationClip Land => land;
        public AnimationClip Sit => sit;
    }
}
