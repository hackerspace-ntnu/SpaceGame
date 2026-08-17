using UnityEngine;

/// <summary>
/// Flight state - spaceship is flying, using flight controller and boosters active
/// </summary>
public class SpaceshipFlightState : ISpaceshipState
{
    private IFlightController flightController;

    public SpaceshipFlightState(IFlightController flightController)
    {
        this.flightController = flightController;
    }

    public void Enter(SpaceshipManager manager)
    {
        // Turn on booster visual
        manager.SetBoosterActive(true);
        
        // Turn on all booster lights
        manager.SetBoosterLightsActive(true);
        
        // Notify flight controller of start
        flightController?.OnFlightStart(manager);
    }

    public void Exit(SpaceshipManager manager)
    {
        // Turn off booster visual
        manager.SetBoosterActive(false);
        
        // Turn off all booster lights
        manager.SetBoosterLightsActive(false);
        
        // Notify flight controller of end
        flightController?.OnFlightEnd(manager);
    }

    public void Update(SpaceshipManager manager)
    {
        // Update flight behavior
        flightController?.UpdateFlight(manager);
    }
}
