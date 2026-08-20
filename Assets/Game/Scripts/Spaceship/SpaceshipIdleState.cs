using UnityEngine;

/// <summary>
/// Idle state - spaceship is turned off, no flight active
/// </summary>
public class SpaceshipIdleState : ISpaceshipState
{
    public void Enter(SpaceshipManager manager)
    {
        // Turn off booster visual
        manager.SetBoosterActive(false);
        
        // Turn off all booster lights
        manager.SetBoosterLightsActive(false);
    }

    public void Exit(SpaceshipManager manager)
    {
        // Prepare to leave idle state
    }

    public void Update(SpaceshipManager manager)
    {
        // Idle state doesn't do anything each frame
    }
}
