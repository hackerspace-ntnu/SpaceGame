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
        Vector3 facingDirection = default)
    {
        return new MoveIntent
        {
            Type = AgentIntentType.MoveToPosition,
            TargetPosition = targetPosition,
            FacingDirection = facingDirection,
            OverrideFacingDirection = overrideFacingDirection,
            StopDistance = Mathf.Max(0.01f, stopDistance),
            SpeedMultiplier = Mathf.Max(0.01f, speedMultiplier)
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
