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

    public static MoveIntent MoveTo(Vector3 targetPosition, float stopDistance = 0.2f, float speedMultiplier = 1f)
    {
        return new MoveIntent
        {
            Type = AgentIntentType.MoveToPosition,
            TargetPosition = targetPosition,
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
