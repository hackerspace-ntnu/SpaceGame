using UnityEngine;

/// <summary>
/// Makes a wheel rotate based on the rover's movement speed.
/// Should be attached to each wheel GameObject.
/// </summary>
public class WheelRotator : MonoBehaviour
{
    [SerializeField] private RoverController roverController;
    [SerializeField] private float wheelRadius = 0.5f; // Radius of the wheel in units
    [SerializeField] private bool rotateAroundX = true; // Rotation axis (typically X for wheels rotating forward)

    private float rotationSpeed; // Degrees per second

    private void Awake()
    {
        if (roverController == null)
        {
            // Try to find it in parent hierarchy
            roverController = GetComponentInParent<RoverController>();
        }

        if (roverController == null)
        {
            // Search up the hierarchy
            Transform rover = transform.parent;
            while (rover != null)
            {
                roverController = rover.GetComponent<RoverController>();
                if (roverController != null) break;
                rover = rover.parent;
            }
        }

        if (roverController == null)
        {
            Debug.LogWarning($"{name}: Could not find RoverController! Wheel rotation disabled.", this);
        }
    }

    private void Update()
    {
        if (roverController == null)
        {
            return;
        }

        // Get the current movement velocity from the rover controller
        Vector3 movementVelocity = roverController.GetCurrentMovementVelocity();
        float currentSpeed = movementVelocity.magnitude;
        
        // Calculate rotation speed: speed / circumference = rotations per unit time
        // Circumference = 2 * pi * radius
        float circumference = 2f * Mathf.PI * wheelRadius;
        rotationSpeed = (currentSpeed / circumference) * 360f; // Convert to degrees per second

        // Apply rotation
        if (rotateAroundX)
        {
            transform.Rotate(rotationSpeed * Time.deltaTime, 0, 0, Space.Self);
        }
        else
        {
            transform.Rotate(0, 0, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}
