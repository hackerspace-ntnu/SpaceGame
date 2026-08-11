using System.Linq;
using UnityEngine;

/// <summary>
/// Delegates interaction to another IInteractable component on a different GameObject.
/// Useful for when you want to have an interactable that is not on the same GameObject as the collider that detects the interaction.
/// </summary>
public class InteractableProxy : MonoBehaviour, IInteractable
{
    [SerializeField] Transform target;
    private IInteractable targetInteractable;

    private void Awake()
    {
        if (target == null)
        {
            Debug.LogWarning($"[InteractableProxy] target not assigned on {name}, searching children.", this);
            foreach (var c in GetComponentsInChildren<IInteractable>(true))
            {
                if (c is not InteractableProxy)
                {
                    targetInteractable = c;
                    return;
                }
            }
            return;
        }
        targetInteractable = target.GetComponent<IInteractable>();
    }

    public bool CanInteract()
    {
        return targetInteractable != null && targetInteractable.CanInteract();
    }

    public void Interact(Interactor interactor)
    {
        targetInteractable?.Interact(interactor);
    }
}