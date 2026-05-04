using UnityEngine;

/// <summary>
/// Drop on the same GameObject as a SceneTransition to fire it when the player
/// interacts (raycast → press E). Requires no configuration — it just forwards
/// the Interactor's GameObject to the transition.
/// </summary>
[RequireComponent(typeof(SceneTransition))]
[AddComponentMenu("Scene Management/Triggers/Interactable Transition Trigger")]
public class InteractableTransitionTrigger : MonoBehaviour, IInteractable
{
    private SceneTransition transition;

    private void Awake()
    {
        transition = GetComponent<SceneTransition>();
    }

    public bool CanInteract()
    {
        return transition != null && !transition.IsBusy
            && transition.Destination != null && transition.Destination.IsValid();
    }

    public void Interact(Interactor interactor)
    {
        if (interactor == null || transition == null) return;
        transition.Trigger(interactor.gameObject);
    }
}
