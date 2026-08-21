using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How this item is held. Drop it on the root of any prefab that can end up in a hand —
    /// artifact, weapon, tool — and it is the whole answer to where the thing sits, which way it
    /// points, and how big it is.
    ///
    /// <para>
    /// Everything here is optional. An item with no <see cref="ItemGrip"/> at all is still seated
    /// and sized by <see cref="EquipItemSocket"/> from its renderer bounds, which is good enough to
    /// look deliberate. This component is how an item stops being a guess.
    /// </para>
    /// <para>
    /// The offsets are expressed in the <em>hand's</em> frame, not the bone's — see
    /// <see cref="HandGripFrame"/>. That frame means the same thing on every rig, so a grip tuned
    /// against the player also holds on an NPC with a different skeleton. Tuning against raw bone
    /// axes would not survive the first character with a different export.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemGrip : MonoBehaviour
    {
        public enum Hand
        {
            Right,
            Left
        }

        [Header("Where the hand closes")]
        [Tooltip("Child transform the hand grips. Leave empty to grip the prefab root. Its position " +
                 "is what lands in the palm; its rotation is ignored — use rotationOffset for angle.")]
        [SerializeField] private Transform gripPoint;

        [Tooltip("Which hand this is held in. Left mirrors the grip frame.")]
        [SerializeField] private Hand hand = Hand.Right;

        [Header("Pose")]
        [Tooltip("Rotation applied in hand space. Start from zero: the item's +Z points where the " +
                 "back of the hand faces and its +Y points out the thumb side, as a torch's flame " +
                 "would. Tune from there.")]
        [SerializeField] private Vector3 rotationOffset;

        [Tooltip("Offset in hand space, in metres. +Y is out the thumb side, +Z is the way the item " +
                 "points, +X is across the palm. Use it to slide the item along the grip.")]
        [SerializeField] private Vector3 positionOffset;

        [Header("Size")]
        [Tooltip("Longest-axis size of the item in metres, once held. 0 keeps the prefab's own " +
                 "scale untouched. A hand tool is roughly 0.2-0.4; a rifle 0.9-1.2.")]
        [SerializeField] private float holdSize;

        [Tooltip("Measure holdSize against this subtree only, instead of the whole prefab. For an " +
                 "item that carries geometry it is not: the Lasso's rope is a 4.4 m mesh sitting in " +
                 "the same prefab as its handle, and sizing the pair to fit a hand shrinks the " +
                 "handle to nothing. Point this at the handle and the rope comes along at whatever " +
                 "scale the handle needed.")]
        [SerializeField] private Transform sizeReference;

        [Header("Escape hatches")]
        [Tooltip("Leave this item's colliders enabled while held. Off by default, and it should stay " +
                 "off: a live collider on the end of an arm shoves the holder around and catches on " +
                 "the world as they walk.")]
        [SerializeField] private bool keepColliders;

        /// <summary>The transform the hand closes around. Never null — falls back to the root.</summary>
        public Transform GripPoint => gripPoint != null ? gripPoint : transform;

        public Hand HeldIn => hand;
        public Vector3 RotationOffset => rotationOffset;
        public Vector3 PositionOffset => positionOffset;

        /// <summary>Longest-axis size in metres, or 0 to keep the prefab's own scale.</summary>
        public float HoldSize => Mathf.Max(0f, holdSize);

        /// <summary>What holdSize is measured against. Null means the whole prefab.</summary>
        public Transform SizeReference => sizeReference;

        public bool KeepColliders => keepColliders;

        private void OnValidate()
        {
            holdSize = Mathf.Max(0f, holdSize);

            // A grip point outside this prefab is a reference that survives in the inspector and
            // dies the moment the prefab is instantiated, leaving the item to grip its own root
            // with no hint as to why the pose it was tuned to is gone.
            if (gripPoint != null && !gripPoint.IsChildOf(transform))
            {
                Debug.LogWarning($"ItemGrip on '{name}': gripPoint '{gripPoint.name}' is not part of " +
                                 "this prefab and will not survive instantiation. Clearing it.", this);
                gripPoint = null;
            }

            if (sizeReference != null && !sizeReference.IsChildOf(transform))
            {
                Debug.LogWarning($"ItemGrip on '{name}': sizeReference '{sizeReference.name}' is not " +
                                 "part of this prefab and will not survive instantiation. Clearing it.", this);
                sizeReference = null;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform g = GripPoint;
            float s = HoldSize > 0f ? HoldSize * 0.25f : 0.05f;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(g.position, s * 0.3f);

            // The axes the hand will align this item to, drawn where the palm will be.
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(g.position, transform.forward * s);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(g.position, transform.up * s);
            Gizmos.color = Color.red;
            Gizmos.DrawRay(g.position, transform.right * s);
        }
#endif
    }
}
