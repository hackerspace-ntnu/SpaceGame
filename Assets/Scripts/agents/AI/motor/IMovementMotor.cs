// Motor interface for executing MoveIntent commands on an agent.
// Exposes runtime velocity/state so controllers and animators can react.
using UnityEngine;

public interface IMovementMotor
{
    Vector3 Velocity { get; }
    bool IsImmobile { get; }
    bool HasReachedDestination { get; }

    void Tick(in MoveIntent intent, float deltaTime);
    void ForceStop();
}
