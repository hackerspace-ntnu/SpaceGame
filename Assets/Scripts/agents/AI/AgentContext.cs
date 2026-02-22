// Snapshot of runtime data passed from AgentController to agent brains.
// Keeps AI logic decoupled from concrete movement components and scene lookups.
// Includes pose/state values that brains use to choose the next MoveIntent.
using UnityEngine;

public struct AgentContext
{
    public Transform Self;
    public Vector3 Position;
    public Vector3 Velocity;
    public bool HasReachedDestination;
    public bool IsImmobile;
}
