// Defines command types that brains send to movement motors each tick.
// Standardizes destination, facing, and speed-control data across AI and mounts.
// Acts as the shared contract between decision logic and locomotion execution.
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    public enum AgentIntentType
    {
        Idle,
        MoveToPosition,
        StopAndFacePosition
    }

    public struct MoveIntent
    {
        public AgentIntentType Type;
        public Vector3 TargetPosition;
        public Vector3 FacePosition;
        public float StopDistance;
        public float SpeedMultiplier;
        public bool IsRunning;

        // An absolute walking pace in m/s, or 0 to walk at the motor's own pace. SpeedMultiplier still
        // scales it and IsRunning still runs (at the motor's own run-to-walk ratio). Set by goal travel
        // so every agent on its way somewhere walks as fast as a player does (PlayerPace).
        public float WalkSpeed;

        // Facing is a separate channel from locomotion. When set, the motor turns the body toward
        // FacePosition instead of along the path, so an agent can walk one way and aim another —
        // that is what lets a ranged module keep its gun on target while ChaseModule owns the path,
        // instead of every combat behaviour having to come to a full stop to shoot.
        public bool OverrideFacing;

        // Returns this intent with the facing channel pointed at a world position. Locomotion is
        // untouched, so the caller keeps whatever destination it already decided on.
        public MoveIntent WithFacing(Vector3 facePosition)
        {
            MoveIntent copy = this;
            copy.FacePosition = facePosition;
            copy.OverrideFacing = true;
            return copy;
        }

        public static MoveIntent Idle()
        {
            return new MoveIntent
            {
                Type = AgentIntentType.Idle,
                StopDistance = 0.1f,
                SpeedMultiplier = 1f
            };
        }

        public static MoveIntent MoveTo(
            Vector3 targetPosition,
            float stopDistance = 0.2f,
            float speedMultiplier = 1f,
            bool isRunning = false,
            float walkSpeed = 0f)
        {
            return new MoveIntent
            {
                Type = AgentIntentType.MoveToPosition,
                TargetPosition = targetPosition,
                StopDistance = Mathf.Max(0.01f, stopDistance),
                SpeedMultiplier = Mathf.Max(0.01f, speedMultiplier),
                IsRunning = isRunning,
                WalkSpeed = Mathf.Max(0f, walkSpeed)
            };
        }

        /// <summary>
        /// The ground speed this intent asks of a motor whose own gaits are
        /// <paramref name="ownWalkSpeed"/> and <paramref name="ownRunSpeed"/>, in m/s. With no
        /// <see cref="WalkSpeed"/> that is the motor's own walk or run; with one, the walk is that
        /// pace and the run keeps the motor's own run-to-walk ratio. Pure, for the tests.
        /// </summary>
        public float GaitSpeed(float ownWalkSpeed, float ownRunSpeed)
        {
            float walk = WalkSpeed > 0f ? WalkSpeed : ownWalkSpeed;
            float run = ownWalkSpeed > 0f ? ownRunSpeed * (walk / ownWalkSpeed) : ownRunSpeed;
            return (IsRunning ? run : walk) * Mathf.Max(0.01f, SpeedMultiplier);
        }

        public static MoveIntent StopAndFace(Vector3 facePosition)
        {
            return new MoveIntent
            {
                Type = AgentIntentType.StopAndFacePosition,
                FacePosition = facePosition,
                StopDistance = 0.1f,
                SpeedMultiplier = 1f
            };
        }
    }
}
