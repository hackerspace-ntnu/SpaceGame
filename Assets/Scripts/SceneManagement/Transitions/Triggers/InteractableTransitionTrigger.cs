using UnityEngine;

/// <summary>
/// Compatibility shim: legacy trigger that only knows how to fire a SceneTransition.
/// Prefer the generic <see cref="InteractableTrigger"/> on the same GameObject — it works
/// with any <see cref="ITriggerable"/> (SceneTransition, CutsceneAction, future actions).
/// Kept so existing prefabs/scenes that serialize this component keep working.
/// </summary>
[System.Obsolete("Use InteractableTrigger (which auto-discovers any ITriggerable on the same GameObject).")]
[RequireComponent(typeof(SceneTransition))]
[AddComponentMenu("Scene Management/Triggers/Interactable Transition Trigger (legacy)")]
public class InteractableTransitionTrigger : MonoBehaviour, IInteractable
{
    private SceneTransition transition;

    private void Awake()
    {
        transition = GetComponent<SceneTransition>();
    }

    public bool CanInteract() => transition != null && transition.CanTrigger(gameObject);

    public void Interact(Interactor interactor)
    {
        if (interactor == null || transition == null) return;
        transition.Trigger(interactor.gameObject);
    }
}
