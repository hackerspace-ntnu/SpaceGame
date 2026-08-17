using UnityEngine;

/// <summary>
/// Flight controller interface for different flight behaviors
/// </summary>
public interface IFlightController
{
    void UpdateFlight(SpaceshipManager manager);
    void OnFlightStart(SpaceshipManager manager);
    void OnFlightEnd(SpaceshipManager manager);
}
