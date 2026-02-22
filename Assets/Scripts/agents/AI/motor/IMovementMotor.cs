using UnityEngine;

public interface IMovementMotor
{
    Vector3 Velocity { get; }
    bool IsImmobile { get; }
    bool HasReachedDestination { get; }

    void Tick(in MoveIntent intent, float deltaTime);
    void ForceStop();
}
