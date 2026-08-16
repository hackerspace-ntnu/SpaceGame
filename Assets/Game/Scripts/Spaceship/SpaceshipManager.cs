using UnityEngine;

/// <summary>
/// Main spaceship manager using state pattern
/// Handles state transitions, booster control, and booster lights
/// </summary>
public class SpaceshipManager : MonoBehaviour
{
    [SerializeField] private RocketBoosterController boosterController;
    [SerializeField] private Transform boostersParent;
    [SerializeField] private IFlightController flightController;
    
    private ISpaceshipState currentState;
    private SpaceshipIdleState idleState;
    private SpaceshipFlightState flightState;
    private SpaceshipCrashState crashState;
    
    private Light[] boosterLights;
    private Rigidbody rb;

    private void OnEnable()
    {
        Initialize();
    }

    private void Initialize()
    {
        // Initialize Rigidbody reference
        rb = GetComponent<Rigidbody>();
        
        // Collect all booster lights from children
        if (boostersParent != null)
        {
            boosterLights = boostersParent.GetComponentsInChildren<Light>();
        }
        else
        {
            Debug.LogWarning("SpaceshipManager: boostersParent not assigned!");
            boosterLights = new Light[0];
        }
        
        // Initialize states
        idleState = new SpaceshipIdleState();
        
        // Flight controller needs to be set via inspector or property
        if (flightController == null)
        {
            flightController = GetComponent<IFlightController>();
        }
        
        flightState = new SpaceshipFlightState(flightController);
        crashState = new SpaceshipCrashState();
        
        // Start in idle state
        TransitionToState(idleState);
    }

    private void Update()
    {
        currentState?.Update(this);
    }

    /// <summary>
    /// Transition to a new state
    /// </summary>
    public void TransitionToState(ISpaceshipState newState)
    {
        if (currentState != null)
        {
            currentState.Exit(this);
        }

        currentState = newState;
        currentState.Enter(this);
    }

    /// <summary>
    /// Begin flight with the assigned flight controller
    /// </summary>
    public void BeginFlight()
    {
        if (!(currentState is SpaceshipFlightState))
        {
            TransitionToState(flightState);
        }
    }

    /// <summary>
    /// End flight and return to idle
    /// </summary>
    public void EndFlight()
    {
        TransitionToState(idleState);
    }

    /// <summary>
    /// Crash the spaceship with current velocity
    /// </summary>
    public void Crash()
    {
        if (rb != null)
        {
            crashState.VelocityAtCrash = rb.linearVelocity;
        }
        TransitionToState(crashState);
    }

    /// <summary>
    /// Set booster visual active/inactive
    /// </summary>
    public void SetBoosterActive(bool active)
    {
        if (boosterController != null)
        {
            boosterController.IsBoosterActive = active;
        }
    }

    /// <summary>
    /// Set all booster lights active/inactive
    /// </summary>
    public void SetBoosterLightsActive(bool active)
    {
        if (boosterLights == null || boosterLights.Length == 0) return;

        foreach (Light light in boosterLights)
        {
            if (light != null)
            {
                light.enabled = active;
            }
        }
    }

    /// <summary>
    /// Set the flight controller (can be changed dynamically)
    /// </summary>
    public void SetFlightController(IFlightController newFlightController)
    {
        flightController = newFlightController;
        flightState = new SpaceshipFlightState(flightController);
    }

    /// <summary>
    /// Get current state for debugging
    /// </summary>
    public ISpaceshipState GetCurrentState()
    {
        return currentState;
    }

    /// <summary>
    /// Get Rigidbody reference for flight controllers
    /// </summary>
    public Rigidbody GetRigidbody()
    {
        return rb;
    }
}
