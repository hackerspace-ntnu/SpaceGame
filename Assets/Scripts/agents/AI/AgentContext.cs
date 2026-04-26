// Snapshot of runtime data passed from AgentController to behaviour modules each frame.
// NearbyAgents is populated only when at least one FlockingModule is present (zero-alloc otherwise).
using UnityEngine;

public struct AgentContext
{
    public Transform Self;
    public Vector3 Position;
    public Vector3 Velocity;
    public bool HasReachedDestination;
    public bool IsImmobile;

    // Filled by AgentController when nearbyAgentScanRadius > 0.
    // Positions and velocities are parallel arrays indexed [0..NearbyAgentCount).
    public Vector3[] NearbyAgentPositions;
    public Vector3[] NearbyAgentVelocities;
    public int NearbyAgentCount;

    public bool IsMoving => Velocity.sqrMagnitude > 0.01f;
}
