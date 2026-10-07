using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Who is carrying the horizontal half of a look — the body, or the neck.
    ///
    /// <para>
    /// One explicit mode rather than a handful of booleans scattered over the components that
    /// happen to know. Every caller that takes a player's ability to turn away from them
    /// (<see cref="SpaceGame.Presentation.ArrivalCameraRig"/> today) sets the mode and puts it
    /// back, and everything downstream — the clamp, the bone, the wire — reads that one answer.
    /// </para>
    /// </summary>
    public enum HeadAimMode
    {
        /// <summary>
        /// On foot. Mouse yaw is spent turning the RIGIDBODY (see <see cref="PlayerLook"/>), so the
        /// body is already facing wherever the player is looking and a neck that also turned would
        /// double the answer — the character would look permanently over their own shoulder.
        /// Pitch only.
        /// </summary>
        Free,

        /// <summary>
        /// Seated. The body is held at a seat pose by something else and cannot turn at all, so the
        /// neck is the only thing left to carry the horizontal look with.
        /// </summary>
        Seated,
    }

    /// <summary>
    /// How far a neck may turn, and what a look of that size does to it.
    ///
    /// <para>
    /// Separated from <see cref="PlayerHeadLook"/> for the same reason <see cref="PoseBlend"/> is
    /// separated from <see cref="PlayerAimRig"/>: nothing here has state, a frame, an Animator or a
    /// network session in it, so the rules can be reasoned about — and tested — on their own.
    /// </para>
    /// </summary>
    public static class HeadAim
    {
        /// <summary>
        /// How much of a look the neck is allowed to carry, in degrees, split up and down because a
        /// neck is not symmetrical.
        ///
        /// <para>
        /// These are NECK limits, not camera limits, and the difference matters: a camera can be
        /// swung 110° off the seat's forward and still read as a look, whereas a head twisted that
        /// far shears the skin at the collar. The two used to be independent because the camera was
        /// the only thing that moved.
        /// </para>
        /// </summary>
        public readonly struct Limits
        {
            /// <summary>Degrees either side of the body's forward.</summary>
            public readonly float Yaw;

            /// <summary>Degrees of chin-to-chest. Positive pitch is DOWN, as in <see cref="PlayerLook"/>.</summary>
            public readonly float Down;

            /// <summary>Degrees of chin-up.</summary>
            public readonly float Up;

            public Limits(float yaw, float down, float up)
            {
                // Negative limits are a mis-set Inspector field, not a request to invert the neck.
                Yaw = Mathf.Max(0f, yaw);
                Down = Mathf.Max(0f, down);
                Up = Mathf.Max(0f, up);
            }
        }

        /// <summary>
        /// The head yaw a look of <paramref name="yaw"/> degrees actually produces in
        /// <paramref name="mode"/>.
        ///
        /// <para>
        /// This is the whole seated/moving split, in one place: <see cref="HeadAimMode.Free"/>
        /// always answers zero, because on foot the yaw has already been spent turning the body and
        /// asking the neck for it again would apply it twice.
        /// </para>
        /// </summary>
        public static float Yaw(float yaw, HeadAimMode mode, in Limits limits) =>
            mode == HeadAimMode.Seated ? Mathf.Clamp(yaw, -limits.Yaw, limits.Yaw) : 0f;

        /// <summary>
        /// The head pitch a look of <paramref name="pitch"/> degrees produces. Positive is down.
        ///
        /// <para>
        /// Clamped in both modes, and deliberately tighter than the camera's own vertical clamp: on
        /// foot the eyes may travel further than the neck does, and the head simply stops following
        /// at the limit rather than the view stopping with it.
        /// </para>
        /// </summary>
        public static float Pitch(float pitch, in Limits limits) =>
            Mathf.Clamp(pitch, -limits.Up, limits.Down);

        /// <summary>
        /// The look as a rotation in the BODY's frame: yaw about its up, pitch about its right.
        ///
        /// <para>
        /// Yaw outermost, so pitch is applied in the un-yawed frame — the same composition
        /// <c>Quaternion.Euler(pitch, yaw, 0)</c> performs, and the same one the camera uses. Both
        /// have to agree exactly or the view slides off the head it is supposed to be riding.
        /// </para>
        /// </summary>
        public static Quaternion Local(float yaw, float pitch) => Quaternion.Euler(pitch, yaw, 0f);

        /// <summary>
        /// The same rotation expressed about a body's world axes, ready to be pre-multiplied onto a
        /// bone's world rotation.
        ///
        /// <para>
        /// Built from the axes rather than from <see cref="Local"/> composed with the body's
        /// rotation because a bone carries an arbitrary bind orientation: pre-multiplying a
        /// world-axis delta turns the head by exactly this much whatever the rig's idea of "head
        /// forward" happens to be, which is the one thing a hand-authored humanoid cannot be
        /// trusted about.
        /// </para>
        /// </summary>
        public static Quaternion Delta(float yaw, float pitch, Vector3 up, Vector3 right) =>
            Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right);

        /// <summary>
        /// The share of <paramref name="delta"/> one bone in a chain takes.
        ///
        /// <para>
        /// A quaternion power, not a lerp of angles: the neck takes <paramref name="share"/> of the
        /// turn and the head — read back AFTER the neck has moved, so it already carries that share
        /// — takes the remainder, and the two compose to exactly <paramref name="delta"/> because
        /// they are powers of the same rotation. Splitting it is what stops the head snapping off a
        /// rigid neck at the extremes.
        /// </para>
        /// </summary>
        public static Quaternion Share(Quaternion delta, float share) =>
            Quaternion.SlerpUnclamped(Quaternion.identity, delta, Mathf.Clamp01(share));
    }
}
