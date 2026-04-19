using UnityEngine;

/// <summary>
/// Player-controlled movement for the rover when mounted.
/// Mirrors the autonomous explorer approach to work properly with rover physics.
/// </summary>
public class MountedRoverController : MonoBehaviour
{
    [SerializeField] private MountController mountController;
    [SerializeField] private MountSteeringController mountSteeringController;
    [SerializeField] private float moveSpeed = 3f;
    
    private Vector3 currentMovementDirection = Vector3.zero;
    private bool isActive;

    private void Awake()
    {
        if (mountController == null)
        {
            mountController = GetComponent<MountController>();
        }

        if (mountSteeringController == null)
        {
            mountSteeringController = GetComponent<MountSteeringController>();
        }
    }

    private void OnEnable()
    {
        // Mount events are handled by RoverController, not here
        // This controller just checks IsMounted status
    }

    private void OnDisable()
    {
        isActive = false;
    }

    private void Update()
    {
        // Only active when rover is mounted and player control state is active
        if (!mountController.IsMounted)
        {
            isActive = false;
            return;
        }

        // Sync active state with mount status
        isActive = true;

        if (mountSteeringController == null)
        {
            return;
        }

        // Read player input
        Vector2 moveInput = mountSteeringController.CurrentMoveInput;

        // Get the rover's current forward direction (already rotated by steering)
        Vector3 roverForward = transform.forward;
        
        // Calculate movement direction in world space
        // W/S controls forward/backward along rover's current forward
        currentMovementDirection = roverForward * moveInput.y * moveSpeed;
    }

    private void OnPlayerMounted(PlayerMovement playerMovement)
    {
        // Removed - mount events are handled by RoverController
    }

    private void OnPlayerDismounted(PlayerMovement playerMovement)
    {
        // Removed - mount events are handled by RoverController
    }

    /// <summary>
    /// Get the current movement direction for the rover.
    /// </summary>
    public Vector3 GetMovementDirection()
    {
        return currentMovementDirection;
    }

    /// <summary>
    /// Check if mounted player control is active.
    /// </summary>
    public bool IsActive()
    {
        return isActive;
    }
}
