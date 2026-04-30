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

    // The current NavMesh destination, or null if the agent is stopped / has no path.
    Vector3? CurrentDestination { get; }

    // Bias the current destination by a world-space offset without replacing it.
    // No-op if the agent is stopped or has no path.
    void NudgeDestination(Vector3 offset);

    // Drive the agent toward a position set externally (e.g. by HerdModule destination sharing).
    // The agent's own movement modules will override this on their next Tick.
    void SuggestDestination(Vector3 position);
}
