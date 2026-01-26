using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles raycasting for object interactions
/// Sends out a raycast when a button is pressed to detect interactable objects
/// </summary>
public class Interactor : MonoBehaviour
{
    [SerializeField]
    private float _castDistance = 5f;
    [SerializeField]
    private Vector3 _rayOffset = new Vector3(0, 1.5f, 0);

    private InputAction _interactAction;

    private void Awake()
    {
        // Get the actions from the current Input System
        _interactAction = InputSystem.actions.FindAction("Interact");
    }

    private void Update()
    {
        if (_interactAction.WasPressedThisFrame())
        {
            if (DoInteractionTest(out IInteractable interactable))
            {
                if (interactable.CanInteract())
                {
                    interactable.Interact(this);
                }
            }
        }
    }

    private bool DoInteractionTest(out IInteractable interactable)
    {
        interactable = null;

        Ray ray = new Ray(transform.position + _rayOffset, transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, _castDistance))
        {
            interactable = hitInfo.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                return true;
            }
            return false;
        }
        return false;
    }
}
