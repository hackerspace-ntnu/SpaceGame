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
    
    public bool IsHoveringInteractable { get; private set; }
    
    [Header("Debug")]
    [SerializeField] private bool _debugRay = true;
    private Color hitNormalColor = Color.blue;
     private Color hitInteractableColor = Color.green;
     private Color missColor = Color.red;

     private RaycastHit hitInfo;
    private bool rayCastHit;

    private void Awake()
    {
        _interactAction = InputSystem.actions.FindAction("Interact");
    }

    private void Update()
    {
        if (!DoInteractionTest(out IInteractable interactable))
        {
            IsHoveringInteractable = false;
            return;
        }
        
        IsHoveringInteractable = true;

        if (!_interactAction.WasPressedThisFrame()) return;
        
        if (interactable.CanInteract())
        {
            interactable.Interact(this);
        }
    }

    private bool DoInteractionTest(out IInteractable interactable)
    {
        interactable = null;
        
        Vector3 origin = lookTransform.position;
        Vector3 direction = lookTransform.forward;
        
        int layerMask = ~LayerMask.GetMask("Player");

        Ray ray = new Ray(origin, direction);
        rayCastHit = Physics.Raycast(ray, out hitInfo, _castDistance, layerMask);
        if (rayCastHit)
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
    
    private void OnDrawGizmos()
    {
        if (!_debugRay)
            return;
        
        Vector3 origin = lookTransform.position;
        Vector3 direction = lookTransform.forward;
        Ray ray = new Ray(origin, direction);
        
        Vector3 end = ray.origin + ray.direction * _castDistance;

        if (rayCastHit)
        {
            IInteractable interactable = hitInfo.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                Gizmos.color = hitInteractableColor;
            }
            else
            {
                Gizmos.color = hitNormalColor;
            }

            Gizmos.DrawSphere(hitInfo.point, 0.03f);
            Gizmos.DrawLine(origin, hitInfo.point);
        }
        else
        {
            Gizmos.color = missColor;
            Gizmos.DrawLine(origin, end);
        }
    }

}
