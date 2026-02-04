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

    [SerializeField] private Transform lookTransform;

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
        
        Ray ray = new Ray(transform.position , lookTransform.forward);
        int layerMask = ~LayerMask.GetMask("Player");
        if (Physics.Raycast(ray, out RaycastHit hitInfo, _castDistance, layerMask))
        {
            interactable = hitInfo.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                return true;
            }
            return false;
        }
        return false;
    }
}
