using UnityEngine;

/// <summary>
/// Simple flight controller that moves spaceship upward with acceleration
/// Triggers booster automatically during flight
/// </summary>
public class SimpleUpwardFlightController : MonoBehaviour, IFlightController
{
    [SerializeField] private float initialSpeed = 5f;
    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float maxSpeed = 50f;
    [SerializeField] private float maxAltitude = 100f;
    
    private SpaceshipManager spaceshipManager;
    private float startAltitude;
    private float currentSpeed;

    private void OnEnable()
    {
        spaceshipManager = GetComponent<SpaceshipManager>();
        if (spaceshipManager == null)
        {
            Debug.LogError("SimpleUpwardFlightController requires SpaceshipManager on same GameObject!");
        }
    }

    public void OnFlightStart(SpaceshipManager manager)
    {
        // Record starting altitude and reset speed
        startAltitude = transform.position.y;
        currentSpeed = initialSpeed;
    }

    public void OnFlightEnd(SpaceshipManager manager)
    {
        // Flight has ended - nothing specific needed here
    }

    public void UpdateFlight(SpaceshipManager manager)
    {
        // Accelerate up to max speed
        currentSpeed = Mathf.Min(currentSpeed + acceleration * Time.deltaTime, maxSpeed);
        
        // Move upward
        Vector3 currentPosition = transform.position;
        float currentAltitude = currentPosition.y - startAltitude;
        
        // Stop if reached max altitude
        if (currentAltitude < maxAltitude)
        {
            transform.position += Vector3.up * currentSpeed * Time.deltaTime;
        }
        else
        {
            // Reached max altitude, end flight
            manager.EndFlight();
        }
    }
}
