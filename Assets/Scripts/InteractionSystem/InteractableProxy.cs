using System.Linq;
using UnityEngine;

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