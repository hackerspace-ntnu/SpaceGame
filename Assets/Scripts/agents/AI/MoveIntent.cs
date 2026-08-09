// Defines command types that brains send to movement motors each tick.
// Standardizes destination, facing, and speed-control data across AI and mounts.
// Acts as the shared contract between decision logic and locomotion execution.
using UnityEngine;

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
    public Vector3 FacingDirection;
    public bool OverrideFacingDirection;
    public float StopDistance;
    public float SpeedMultiplier;
    public bool IsRunning;

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
        bool overrideFacingDirection = false,
        Vector3 facingDirection = default,
        bool isRunning = false)
    {
        return new MoveIntent
        {
            Type = AgentIntentType.MoveToPosition,
            TargetPosition = targetPosition,
            FacingDirection = facingDirection,
            OverrideFacingDirection = overrideFacingDirection,
            StopDistance = Mathf.Max(0.01f, stopDistance),
            SpeedMultiplier = Mathf.Max(0.01f, speedMultiplier),
            IsRunning = isRunning
        };
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
