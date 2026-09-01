using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Where the aiming hand goes, and how fast it gets there.
    ///
    /// <para>
    /// Separated from <see cref="PlayerAimRig"/> because it is the part with no state and no
    /// frame in it — every value here is a function of its arguments, so it can be reasoned
    /// about without an Animator, a network session or a play mode.
    /// </para>
    /// </summary>
    public static class AimPose
    {
        /// <summary>
        /// The world point the hand is pulled to, given the eye's pose and an offset expressed
        /// in the eye's own frame.
        ///
        /// <para>
        /// Anchored on the eye rather than on the body: "up to the eye" is a position the CAMERA
        /// defines, and the body does not pitch. Anchoring on the body is what would make the
        /// weapon stay level while the player looked up.
        /// </para>
        /// </summary>
        public static Vector3 HandGoal(Vector3 eyePosition, Quaternion eyeRotation, Vector3 localOffset)
            => eyePosition + eyeRotation * localOffset;

        /// <summary>
        /// Where the elbow should be pulled towards.
        ///
        /// <para>
        /// Without a hint the two-bone solver is free to put the elbow anywhere on the circle
        /// around the shoulder-to-hand axis, and it flips sides when the hand crosses the body —
        /// which is exactly what looking up and down does. Taking the midpoint and pushing it
        /// out and down picks the side a person actually uses.
        /// </para>
        /// </summary>
        public static Vector3 ElbowHint(Vector3 shoulder, Vector3 hand, Quaternion bodyRotation,
                                        Vector3 localPush)
            => Vector3.Lerp(shoulder, hand, 0.5f) + bodyRotation * localPush;

        /// <summary>
        /// Frame-rate independent ease towards a target, 0 to 1.
        ///
        /// <para>
        /// A <paramref name="blendTime"/> of 0 snaps, which is what a caller that wants no blend
        /// should be able to ask for without a special case. Uses MoveTowards rather than an
        /// exponential so the blend actually ARRIVES — an exponential approaches 1 and never
        /// reaches it, and an aim weight that sits at 0.997 leaves the hand permanently a few
        /// millimetres short of where it was asked to be.
        /// </para>
        /// </summary>
        public static float Ease(float current, float target, float blendTime, float deltaTime)
            => blendTime <= 0f
                ? target
                : Mathf.MoveTowards(current, target, deltaTime / blendTime);

        /// <summary>
        /// The rotation to give the HAND so that the item in it points down <paramref name="ray"/>.
        ///
        /// <para>
        /// The item is not the hand. It is seated by <see cref="SpaceGame.Items.EquipItemSocket"/>
        /// at a fixed rotation relative to the hand's grip frame, so aiming the hand at the target
        /// aims the item somewhere else entirely — off by whatever that frame is, which on this rig
        /// is most of a right angle. Undoing the frame here is what makes the barrel, rather than
        /// the wrist, end up on the crosshair.
        /// </para>
        /// </summary>
        public static Quaternion HandRotationForItem(Quaternion ray, Quaternion gripLocalRotation)
            => ray * Quaternion.Inverse(gripLocalRotation);
    }
}
