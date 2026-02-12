using UnityEngine;

/// <summary>
/// Handles door interaction - rotates left and right door children in opposite directions
/// </summary>
public class DoorInteraction : MonoBehaviour, IInteractable
{
    [SerializeField]
    private float _rotationSpeed = 2f;

    [SerializeField]
    private Transform _leftDoor;

    [SerializeField]
    private Transform _rightDoor;

    private bool _isOpen = false;
    private Quaternion _leftStartRotation;
    private Quaternion _leftTargetRotation;
    private Quaternion _rightStartRotation;
    private Quaternion _rightTargetRotation;
    private float _rotationTime = 0f;
    private bool _isRotating = false;

    private void Start()
    {
        // Find the door children if not assigned
        if (_leftDoor == null)
            _leftDoor = transform.Find("LeftDoors");
        if (_rightDoor == null)
            _rightDoor = transform.Find("RightDoors");

        if (_leftDoor != null)
        {
            _leftStartRotation = _leftDoor.rotation;
            _leftTargetRotation = _leftStartRotation;
        }

        if (_rightDoor != null)
        {
            _rightStartRotation = _rightDoor.rotation;
            _rightTargetRotation = _rightStartRotation;
        }
    }

    private void Update()
    {
        if (_isRotating)
        {
            _rotationTime += Time.deltaTime * _rotationSpeed;

            if (_leftDoor != null)
                _leftDoor.rotation = Quaternion.Slerp(_leftStartRotation, _leftTargetRotation, _rotationTime);

            if (_rightDoor != null)
                _rightDoor.rotation = Quaternion.Slerp(_rightStartRotation, _rightTargetRotation, _rotationTime);

            if (_rotationTime >= 1f)
            {
                if (_leftDoor != null)
                    _leftDoor.rotation = _leftTargetRotation;
                if (_rightDoor != null)
                    _rightDoor.rotation = _rightTargetRotation;
                _isRotating = false;
            }
        }
    }

    /// <summary>
    /// Checks if the door can be interacted with (always true for doors)
    /// </summary>
    public bool CanInteract()
    {
        return true;
    }

    /// <summary>
    /// Interacts with the door - rotates left and right doors in opposite directions
    /// </summary>
    public void Interact(Interactor interactor)
    {
        if (!CanInteract() || _isRotating)
            return;
            
        Debug.Log("Door interacted with");
        _isOpen = !_isOpen;

        // Store current rotations as start
        if (_leftDoor != null)
            _leftStartRotation = _leftDoor.rotation;
        if (_rightDoor != null)
            _rightStartRotation = _rightDoor.rotation;
        
        // Rotate left door -90 degrees, right door +90 degrees around Y axis
        if (_isOpen)
        {
            if (_leftDoor != null)
                _leftTargetRotation = _leftStartRotation * Quaternion.Euler(0, -90f, 0);
            if (_rightDoor != null)
                _rightTargetRotation = _rightStartRotation * Quaternion.Euler(0, 90f, 0);
        }
        else
        {
            if (_leftDoor != null)
                _leftTargetRotation = _leftStartRotation * Quaternion.Euler(0, 90f, 0);
            if (_rightDoor != null)
                _rightTargetRotation = _rightStartRotation * Quaternion.Euler(0, -90f, 0);
        }

        _rotationTime = 0f;
        _isRotating = true;
    }
}