using UnityEngine;
using SpaceGame.Persistence;

/// <summary>
/// Main spaceship manager using state pattern
/// Handles state transitions, booster control, and booster lights
///
/// <para>
/// <see cref="IPersistentEntity"/> because which state the ship is in is world state a player put
/// it in, and the hull may carry nothing else <c>SaveablePolicy.NeedsSaving</c> looks for — its
/// Rigidbody is only a qualifier while it is non-kinematic, which is exactly the state a ship
/// sitting on the pad is not in.
/// </para>
/// </summary>
public class SpaceshipManager : MonoBehaviour, IPersistentEntity
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

    /// <summary>Which of the three states the ship is in. The save system's whole view of it.</summary>
    public enum StateKind { Idle, Flight, Crash }

    // Set by a restore, cleared by the OnEnable that honours it.
    //
    // Initialize runs from OnEnable and used to END unconditionally in "start in idle state", which
    // meant a ship that had crashed came back sitting quietly on the pad with its boosters off — the
    // state machine reset by the one method that has to run before anything can read it. A latch is
    // the smallest thing that lets a restore outrank that default without moving the default.
    private bool restored;

    private void OnEnable()
    {
        Initialize();
    }

    private void Initialize()
    {
        EnsureStates();

        // A restore has already put us somewhere. Consumed rather than sticky: the next enable with
        // nothing restored in between is an ordinary one and belongs in idle.
        if (restored) { restored = false; return; }

        // Start in idle state
        TransitionToState(idleState);
    }

    /// <summary>
    /// Build the parts the state machine needs, without deciding which state to be in.
    ///
    /// Split out of <see cref="Initialize"/> so a restore that arrives before the first OnEnable can
    /// put the ship into a state without the idle transition that Initialize ends with immediately
    /// undoing it. Idempotent.
    /// </summary>
    private void EnsureStates()
    {
        // Initialize Rigidbody reference
        if (rb == null) rb = GetComponent<Rigidbody>();

        // Collect all booster lights from children
        if (boosterLights == null)
        {
            if (boostersParent != null)
            {
                boosterLights = boostersParent.GetComponentsInChildren<Light>();
            }
            else
            {
                Debug.LogWarning("SpaceshipManager: boostersParent not assigned!");
                boosterLights = new Light[0];
            }
        }

        // Initialize states
        if (idleState == null) idleState = new SpaceshipIdleState();

        // Flight controller needs to be set via inspector or property
        if (flightController == null)
        {
            flightController = GetComponent<IFlightController>();
        }

        if (flightState == null) flightState = new SpaceshipFlightState(flightController);
        if (crashState == null) crashState = new SpaceshipCrashState();
    }

    /// <summary>The state the ship is in right now, as something a record can hold.</summary>
    public StateKind CurrentKind =>
        currentState is SpaceshipFlightState ? StateKind.Flight :
        currentState is SpaceshipCrashState ? StateKind.Crash :
        StateKind.Idle;

    /// <summary>How fast the ship was going when it hit. Zero unless it has crashed.</summary>
    public Vector3 CrashVelocity => crashState != null ? crashState.VelocityAtCrash : Vector3.zero;

    /// <summary>
    /// Restore-only. Called by the save system; do not call from gameplay.
    ///
    /// Goes through <see cref="TransitionToState"/> rather than assigning the field, so each state's
    /// Enter runs and the boosters and their lights end up matching the state they belong to —
    /// those flags are an OUTPUT of the state machine, and storing them separately could only ever
    /// produce a ship that is flying with its engines off.
    ///
    /// The crash velocity is set before the transition because <see cref="SpaceshipCrashState"/>
    /// reads it in Enter, which is the same order <see cref="Crash"/> uses.
    /// </summary>
    public void RestoreState(StateKind kind, Vector3 crashVelocity)
    {
        EnsureStates();

        crashState.VelocityAtCrash = crashVelocity;
        restored = true;

        ISpaceshipState target = idleState;
        if (kind == StateKind.Flight) target = flightState;
        else if (kind == StateKind.Crash) target = crashState;

        TransitionToState(target);
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
