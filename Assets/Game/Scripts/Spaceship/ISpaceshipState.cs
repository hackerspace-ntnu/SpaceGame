using UnityEngine;

/// <summary>
/// Base interface for spaceship states
/// </summary>
public interface ISpaceshipState
{
    void Enter(SpaceshipManager manager);
    void Exit(SpaceshipManager manager);
    void Update(SpaceshipManager manager);
}
