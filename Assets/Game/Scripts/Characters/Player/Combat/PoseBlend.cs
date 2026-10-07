using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// How an upper-body pose weight gets where it is going.
    ///
    /// <para>
    /// Separated from <see cref="PlayerAimRig"/> because it is the part with no state and no
    /// frame in it — every value here is a function of its arguments, so it can be reasoned
    /// about without an Animator, a network session or a play mode.
    /// </para>
    /// </summary>
    public static class PoseBlend
    {
        /// <summary>
        /// Frame-rate independent ease towards a target, 0 to 1.
        ///
        /// <para>
        /// A <paramref name="blendTime"/> of 0 snaps, which is what a caller that wants no blend
        /// should be able to ask for without a special case. Uses MoveTowards rather than an
        /// exponential so the blend actually ARRIVES — an exponential approaches 1 and never
        /// reaches it, and a layer weight that sits at 0.997 leaves the pose permanently a hair
        /// short of where it was asked to be.
        /// </para>
        /// </summary>
        public static float Ease(float current, float target, float blendTime, float deltaTime)
            => blendTime <= 0f
                ? target
                : Mathf.MoveTowards(current, target, deltaTime / blendTime);
    }
}
