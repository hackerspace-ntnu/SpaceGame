using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where a back item sits on the spine. The hand has a derived grip frame that means the same
    /// thing on every rig; the back has no such anatomy to read, so the pose is authored per
    /// prefab, in the spine bone's own frame.
    ///
    /// <para>
    /// Optional. A back item without one is seated at the bone with its authored scale, which is
    /// wrong for anything but a test cube — so the wing pack carries one, written into the prefab
    /// through <c>PrefabUtility.LoadPrefabContents</c> rather than its lossy builder.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WornFit : MonoBehaviour
    {
        [Tooltip("Offset from the trunk bone, in the bone's frame, metres. For CHEST gear this is " +
                 "the pose. For BACK gear it is only the fallback for a back with no pack on it: " +
                 "with the rig shouldered the position comes off its lash rail instead.")]
        [SerializeField] private Vector3 localPosition;

        [Tooltip("Rotation relative to the spine bone, degrees.")]
        [SerializeField] private Vector3 localEuler;

        [Tooltip("Longest-axis size to draw the worn item at, in metres. 0 keeps the prefab's own scale.")]
        [SerializeField, Min(0f)] private float size;

        [Tooltip("Ignore the pack's lash rail and sit at localPosition on the bone even when the " +
                 "rig IS shouldered. For gear fitted to the BODY rather than clipped to the pack.")]
        [SerializeField] private bool anchorToBone;

        [Tooltip("Hold the wearer's arms out on the gear screen while this is worn or being " +
                 "placed. Only for gear whose worn shape is authored along the arms — the " +
                 "wingsuit's membranes are. Everything else is looked at in a plain idle.")]
        [SerializeField] private bool holdsArmsOut;

        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => Quaternion.Euler(localEuler);
        public float Size => size;

        /// <summary>
        /// Whether this item's position is the bone's, not the pack rail's.
        ///
        /// <para>
        /// Off for anything that clips to the rig — the wing pack's wings hang off the rail's two
        /// protruding bar tips, so centring them on the rail is what puts them there. On for
        /// anything shaped around the WEARER: the worn wingsuit's wings run from the shoulders
        /// down toward the hips, and the rail sits half a metre behind the spine, so seating it
        /// there would hang the wings off the back of the pack instead of off the arms.
        /// </para>
        /// <para>
        /// It also decides whether deploying the pack moves the gear. Rail-anchored gear falls
        /// back to <see cref="LocalPosition"/> when the pack is on the sand and therefore shifts;
        /// bone-anchored gear does not move, which is right — a suit is on the wearer whether or
        /// not they are carrying a pack.
        /// </para>
        /// </summary>
        public bool AnchorToBone => anchorToBone;

        /// <summary>
        /// Whether looking at this item means holding the wearer's arms out.
        ///
        /// <para>
        /// The gear screen's stance is the character's own idle — arms down, the way they stand
        /// everywhere else in the game — and only gear that is <i>shaped along the arms</i> asks
        /// for anything else. The worn wingsuit is the one such item: its membranes are lofted
        /// along <see cref="InspectStance.DefaultDroop"/>, so with the arms at the sides the cloth
        /// folds into the ribs and there is nothing to look at. Wing pack, mount frame and every
        /// gauntlet sit clear of the arms and read fine in the idle.
        /// </para>
        /// <para>
        /// On the item rather than on the screen because the screen cannot know: it is the item's
        /// own authoring that ties it to the pose, and a list of item names kept next to the
        /// camera would go stale the first time somebody added a second one.
        /// </para>
        /// </summary>
        public bool HoldsArmsOut => holdsArmsOut;
    }
}
