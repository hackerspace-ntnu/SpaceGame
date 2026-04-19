using UnityEngine;

/// <summary>
/// Applies mounted player movement to the mount point object.
/// Mirrors the autonomous exploration movement pattern for consistent physics behavior.
/// Converts input direction to movement along the mount's forward axis.
/// </summary>
public class MountedMovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 8f;
    [SerializeField] private MountedInputHandler inputHandler;

    [Header("Turning")]
    [SerializeField] private float turnSpeed = 120f; // degrees per second

    private Rigidbody targetRigidbody;
    private Vector3 currentVelocity = Vector3.zero;
    private float currentForwardSpeed = 0f;

    private void Awake()
    {
        if (inputHandler == null)
        {
            inputHandler = GetComponent<MountedInputHandler>();
        }

        targetRigidbody = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (inputHandler == null || targetRigidbody == null)
        {
            return;
        }

        // Get player input
        Vector2 moveInput = inputHandler.GetMoveInput();

        // Update forward speed with acceleration/deceleration
        float targetSpeed = moveInput.y * moveSpeed;
        if (Mathf.Abs(targetSpeed) > Mathf.Abs(currentForwardSpeed))
        {
            currentForwardSpeed = Mathf.Lerp(currentForwardSpeed, targetSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            currentForwardSpeed = Mathf.Lerp(currentForwardSpeed, targetSpeed, deceleration * Time.deltaTime);
        }

        // Handle turning (rotate around Y axis)
        if (Mathf.Abs(moveInput.x) > 0.01f)
        {
            float turnAmount = moveInput.x * turnSpeed * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0, Space.Self);
        }

        // Calculate movement direction: forward along the mount's current forward
        currentVelocity = transform.forward * currentForwardSpeed;
    }

    private void FixedUpdate()
    {
        if (targetRigidbody == null)
        {
            return;
        }

        // Apply movement via rigidbody (same approach as AutonomousExplorer)
        // This works well with ConfigurableJoint physics constraints
        targetRigidbody.linearVelocity = new Vector3(currentVelocity.x, targetRigidbody.linearVelocity.y, currentVelocity.z);
    }

    /// <summary>
    /// Get current movement velocity for debugging/camera purposes.
    /// </summary>
    public Vector3 GetCurrentVelocity()
    {
        return currentVelocity;
    }

    /// <summary>
    /// Get current forward speed (useful for camera transitions).
    /// </summary>
    public float GetCurrentForwardSpeed()
    {
        return currentForwardSpeed;
    }
}
