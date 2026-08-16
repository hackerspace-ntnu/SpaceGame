using UnityEngine;

/// <summary>
/// Crash state - spaceship has crashed, stores velocity from flight state
/// Implementation for crash physics and behavior will be added later
/// </summary>
public class SpaceshipCrashState : ISpaceshipState
{
    private Vector3 velocityAtCrash;

    public Vector3 VelocityAtCrash
    {
        get => velocityAtCrash;
        set => velocityAtCrash = value;
    }

    public void Enter(SpaceshipManager manager)
    {
        // Turn off booster visual
        manager.SetBoosterActive(false);
        
        // Turn off all booster lights
        manager.SetBoosterLightsActive(false);
        
        // Crash state entered - velocity is already set before transition
        Debug.Log($"Spaceship crashed with velocity: {velocityAtCrash}");
    }

    public void Exit(SpaceshipManager manager)
    {
        // Prepare to leave crash state (if recovering)
    }

    public void Update(SpaceshipManager manager)
    {
        // Crash state update - implementation will be added later
        // Placeholder for crash physics, damage effects, etc.
    }
}
