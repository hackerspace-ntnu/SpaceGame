// Optional motor extensions for mounts that can jump or leap.
// Mounted modules (SteerModule) call these instead of assuming a concrete motor type.
using UnityEngine;

namespace SpaceGame.Agents
{
    public interface IMountJumpMotor
    {
        void RequestJump();

        /// <summary>
        /// Whether the mount's feet are off the ground right now, for any reason this motor
        /// arranges -- a hop or a leap.
        ///
        /// <para>
        /// It exists because the animator has no other way to find out. AgentAnimatorDriver drives
        /// gaits from horizontal velocity, and a jump on a NavMeshAgent is a change to baseOffset
        /// that never appears in one, so without this the animal keeps walking placidly while it
        /// rises into the air.
        /// </para>
        /// </summary>
        bool IsAirborne { get; }
    }

    // Leap = a long horizontal dash with a vertical arc. Used by SteerModule's hold-jump-to-leap mechanic.
    public interface IMountLeapMotor
    {
        bool IsLeapAvailable { get; }
        bool IsLeaping { get; }
        void RequestLeap(Vector3 direction, float horizontalDistance, float verticalHeight, float duration);
    }
}
