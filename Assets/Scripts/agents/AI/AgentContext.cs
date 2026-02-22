using UnityEngine;

public struct AgentContext
{
    public Transform Self;
    public Vector3 Position;
    public Vector3 Velocity;
    public bool HasReachedDestination;
    public bool IsImmobile;
}
