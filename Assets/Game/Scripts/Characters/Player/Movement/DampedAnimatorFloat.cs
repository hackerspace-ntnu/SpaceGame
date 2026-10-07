using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// One animator float, damped here rather than by <c>Animator.SetFloat</c>'s own damping, and
    /// written in steps of a fixed quantum.
    ///
    /// <para>
    /// Why not the Animator's damping: it leaves the parameter creeping toward its target by a
    /// fraction every frame, forever, and <c>NetworkAnimator</c> compares parameters with exact
    /// float equality every frame. A player standing still therefore sent a reliable parameter
    /// update every frame for as long as the damping had not converged to the bit — with six
    /// players, on the order of a thousand messages a second fanned out by the host. Quantising
    /// the damped value gives it a resting place: once within half a quantum of the target it
    /// stops changing, and a parameter that does not change is a parameter that is not sent.
    /// </para>
    /// </summary>
    public struct DampedAnimatorFloat
    {
        private float value;

        /// <summary>The damped value before quantising.</summary>
        public float Value => value;

        /// <summary>Snaps the damped value to <paramref name="to"/>, for an idle reset.</summary>
        public void Reset(float to) => value = to;

        /// <summary>
        /// Moves toward <paramref name="target"/> with the same exponential feel as the Animator's
        /// damping and returns the value to write, rounded to <paramref name="quantum"/>.
        /// </summary>
        public float Step(float target, float dampTime, float deltaTime, float quantum)
        {
            value = dampTime <= 0f || deltaTime <= 0f
                ? target
                : Mathf.Lerp(value, target, 1f - Mathf.Exp(-deltaTime / dampTime));

            return Quantise(value, quantum);
        }

        /// <summary>Rounds <paramref name="v"/> to the nearest multiple of <paramref name="quantum"/>.</summary>
        public static float Quantise(float v, float quantum) =>
            quantum <= 0f ? v : Mathf.Round(v / quantum) * quantum;
    }
}
