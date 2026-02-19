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