using UnityEngine;

/// <summary>
/// High-level rover controller that delegates to the movement system.
/// Automatically switches between autonomous and player control based on mount state.
/// </summary>
public class RoverController : MonoBehaviour
{
    [SerializeField] private RoverMovementSystem movementSystem;
    [SerializeField] private MountController mountController;
    [SerializeField] private MountSteeringController mountSteeringController;
    
    private RoverMovementSystem.MovementState previousState;

    private void Awake()
    {
        if (movementSystem == null)
        {
            movementSystem = GetComponent<RoverMovementSystem>();
        }

        if (movementSystem == null)
        {
            movementSystem = gameObject.AddComponent<RoverMovementSystem>();
        }

        if (mountController == null)
        {
            mountController = GetComponent<MountController>();
        }

        if (mountSteeringController == null)
        {
            mountSteeringController = GetComponent<MountSteeringController>();
        }

        previousState = movementSystem.GetMovementState();
    }

    private void OnEnable()
    {
        if (mountController != null)
        {
            mountController.Mounted += OnPlayerMounted;
            mountController.Dismounted += OnPlayerDismounted;
        }
    }

    private void OnDisable()
    {
        if (mountController != null)
        {
            mountController.Mounted -= OnPlayerMounted;
            mountController.Dismounted -= OnPlayerDismounted;
        }
    }

    private void Update()
    {
        // If mounted and steering controller exists, use player control instead of autonomous
        if (mountController != null && mountController.IsMounted && mountSteeringController != null)
        {
            // Player input should take precedence - do nothing, let MountSteeringController handle it
        }
    }

    private void OnPlayerMounted(PlayerMovement playerMovement)
    {
        // Switch to player control when mounted
        if (movementSystem != null)
        {
            previousState = movementSystem.GetMovementState();
            movementSystem.SetMovementState(RoverMovementSystem.MovementState.PlayerControlled);
        }
        
        Debug.Log("Rover mounted - switching to player control", this);
    }

    private void OnPlayerDismounted(PlayerMovement playerMovement)
    {
        // Switch back to autonomous exploration when dismounted
        if (movementSystem != null)
        {
            movementSystem.SetMovementState(RoverMovementSystem.MovementState.Autonomous);
        }
        
        Debug.Log("Rover dismounted - resuming autonomous exploration", this);
    }

    /// <summary>
    /// Get the current movement state.
    /// </summary>
    public RoverMovementSystem.MovementState GetMovementState()
    {
        return movementSystem.GetMovementState();
    }

    /// <summary>
    /// Switch to a different movement state.
    /// </summary>
    public void SetMovementState(RoverMovementSystem.MovementState newState)
    {
        movementSystem.SetMovementState(newState);
    }

    /// <summary>
    /// Get the current movement velocity being applied.
    /// </summary>
    public Vector3 GetCurrentMovementVelocity()
    {
        return movementSystem.GetCurrentMovementVelocity();
    }

    /// <summary>
    /// Check if the rover is currently mounted.
    /// </summary>
    public bool IsMounted()
    {
        return mountController != null && mountController.IsMounted;
    }
}
