using UnityEngine;

public class InteractableProxy : MonoBehaviour, IInteractable
{
    [SerializeField] private MonoBehaviour target;
    private IInteractable TargetInteractable => target as IInteractable;

    public bool CanInteract() => TargetInteractable != null && TargetInteractable.CanInteract();

    public void Interact(Interactor interactor)
    {
        TargetInteractable?.Interact(interactor);
    }
}