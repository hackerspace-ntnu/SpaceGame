using UnityEngine;

/// <summary>
/// Manages rover movement states and behavior switching.
/// Supports both autonomous exploration and player control via mounting.
/// </summary>
public class RoverMovementSystem : MonoBehaviour
{
    public enum MovementState
    {
        Autonomous, // Autonomous exploration mode (roomba-like)
        PlayerControlled, // Player driving the rover
        Idle // Not moving
    }

    [SerializeField] private RoverMovementController movementController;
    [SerializeField] private MovementState currentState = MovementState.Autonomous;
    [SerializeField] private AutonomousExplorer autonomousExplorer;
    [SerializeField] private MountedRoverController mountedRoverController;

    private Vector3 currentTargetDirection = Vector3.zero;
    private MovementState previousState;

    private void Awake()
    {
        if (movementController == null)
        {
            movementController = GetComponent<RoverMovementController>();
        }

        if (movementController == null)
        {
            movementController = gameObject.AddComponent<RoverMovementController>();
        }

        if (autonomousExplorer == null)
        {
            autonomousExplorer = GetComponent<AutonomousExplorer>();
        }

        if (autonomousExplorer == null)
        {
            autonomousExplorer = gameObject.AddComponent<AutonomousExplorer>();
        }

        if (mountedRoverController == null)
        {
            mountedRoverController = GetComponent<MountedRoverController>();
        }

        if (mountedRoverController == null)
        {
            mountedRoverController = gameObject.AddComponent<MountedRoverController>();
        }

        previousState = currentState;
    }

    private void Update()
    {
        // Handle state transitions
        if (currentState != previousState)
        {
            OnStateExit(previousState);
            OnStateEnter(currentState);
            previousState = currentState;
        }

        // Update current state
        switch (currentState)
        {
            case MovementState.Autonomous:
                UpdateAutonomousMovement();
                break;
            case MovementState.PlayerControlled:
                UpdatePlayerControlledMovement();
                break;
            case MovementState.Idle:
                UpdateIdleMovement();
                break;
        }

        // Apply movement
        if (movementController != null)
        {
            movementController.SetTargetDirection(currentTargetDirection);
            movementController.UpdateMovement(transform);
        }
    }

    private void OnStateEnter(MovementState state)
    {
        switch (state)
        {
            case MovementState.Autonomous:
                if (autonomousExplorer != null)
                    autonomousExplorer.Initialize();
                break;
            case MovementState.PlayerControlled:
                Debug.Log("Rover: Entering player-controlled mode");
                break;
            case MovementState.Idle:
                currentTargetDirection = Vector3.zero;
                break;
        }
    }

    private void OnStateExit(MovementState state)
    {
        // Cleanup if needed
        if (state == MovementState.PlayerControlled)
        {
            Debug.Log("Rover: Exiting player-controlled mode");
            currentTargetDirection = Vector3.zero;
        }
    }

    private void UpdateAutonomousMovement()
    {
        if (autonomousExplorer != null)
        {
            currentTargetDirection = autonomousExplorer.GetMovementDirection();
        }
    }

    private void UpdatePlayerControlledMovement()
    {
        // Use the dedicated mounted rover controller (mirrors autonomous approach)
        if (mountedRoverController != null && mountedRoverController.IsActive())
        {
            currentTargetDirection = mountedRoverController.GetMovementDirection();
        }
        else
        {
            currentTargetDirection = Vector3.zero;
        }
    }

    private void UpdateIdleMovement()
    {
        currentTargetDirection = Vector3.zero;
    }

    /// <summary>
    /// Switch to a different movement state.
    /// </summary>
    public void SetMovementState(MovementState newState)
    {
        currentState = newState;
    }

    /// <summary>
    /// Get the current movement state.
    /// </summary>
    public MovementState GetMovementState()
    {
        return currentState;
    }

    /// <summary>
    /// Get the current target movement velocity.
    /// </summary>
    public Vector3 GetCurrentMovementVelocity()
    {
        return currentTargetDirection;
    }
}
