using UnityEngine;

/// <summary>
/// Interactable entry-point for mounting any object with a MountController.
/// </summary>
public class MountInteractor : MonoBehaviour, IInteractable
{
    [SerializeField] private MountController mountController;
    [SerializeField] private Transform mountTransform;

    private void Awake()
    {
        if (!mountController)
        {
            mountController = GetComponentInParent<MountController>();
        }

        if (!mountTransform)
        {
            mountTransform = transform;
        }
    }

    public bool CanInteract()
    {
        return mountController != null && mountController.IsAvailableForMount;
    }

    public void Interact(Interactor interactor)
    {
        if (!mountController)
        {
            return;
        }

        mountController.TryMount(interactor, mountTransform);
    }
}
