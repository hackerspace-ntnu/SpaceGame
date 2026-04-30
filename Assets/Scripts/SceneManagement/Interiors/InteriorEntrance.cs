using UnityEngine;

/// <summary>
/// Place on a door (or any interactable) in the exterior. On interact, sends the player to the
/// configured interior scene via InteriorManager.
/// </summary>
public class InteriorEntrance : MonoBehaviour, IInteractable
{
    [SerializeField] private InteriorScene targetInterior;

    public void Initialize(InteriorScene def) => targetInterior = def;

    public bool CanInteract() => targetInterior != null && InteriorManager.Instance != null;

    public void Interact(Interactor interactor)
    {
        if (interactor == null) return;
        InteriorManager.Instance.EnterInterior(interactor.gameObject, targetInterior);
    }
}
