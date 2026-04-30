using System;
using Unity.Netcode;
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
    
    
    public bool IsHoveringInteractable { get; private set; }
    
    [Header("Debug")]
    [SerializeField] private bool _debugRay = true;
    private Color hitNormalColor = Color.blue;
     private Color hitInteractableColor = Color.green;
     private Color missColor = Color.red;

     private RaycastHit hitInfo;
    private bool rayCastHit;

    private void Start()
    {
        PlayerInputManager input = GetComponent<PlayerController>().Input;
        input.OnInteractPressed += Interact;
    }

    private void Update()
    {
        if (!DoInteractionTest(out IInteractable interactable))
        {
            IsHoveringInteractable = false;
            return;
        }

        if (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled)
        {
            IsHoveringInteractable = false;
            return;
        }

        IsHoveringInteractable = true;
    }

    private void Interact()
    {
        if (!DoInteractionTest(out IInteractable interactable)) return;

        if (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled) return;
        if (!interactable.CanInteract()) return;
        interactable.Interact(this);

    }

    private bool DoInteractionTest(out IInteractable interactable)
    {
        interactable = null;
        
        Vector3 origin = lookTransform.position;
        Vector3 direction = lookTransform.forward;
        
        int layerMask = ~LayerMask.GetMask("Player");

        Ray ray = new Ray(origin, direction);
        rayCastHit = Physics.Raycast(ray, out var hit, _castDistance, layerMask);
        hitInfo = hit;
        
        if (rayCastHit)
        {
            interactable = hitInfo.collider.GetComponent<IInteractable>();
            if (interactable == null)
            {
                interactable = hitInfo.collider.GetComponentInParent<IInteractable>();
            }
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

        if (rayCastHit && hitInfo.collider != null)
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
