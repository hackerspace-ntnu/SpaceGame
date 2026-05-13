using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Compatibility shim. Same surface as before — click → play assigned cutscene → fire
/// UnityEvent — but routed through the unified <see cref="ITriggerable"/> path so it
/// shares behaviour with everything else. For new content, prefer
/// <see cref="CutsceneAction"/> + <see cref="InteractableTrigger"/>.
/// </summary>
[System.Obsolete("Use CutsceneAction + InteractableTrigger on the same GameObject.")]
public class CutsceneInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private Cutscene cutscene;

    [Tooltip("If true, can only be triggered once per scene load.")]
    [SerializeField] private bool playOnce;

    [Tooltip("Invoked after the cutscene finishes playing. Wire any post-cutscene actions here.")]
    [SerializeField] private UnityEvent<Interactor> onCutsceneEnded;

    private bool busy;
    private bool fired;

    public bool CanInteract()
        => cutscene != null
        && !busy
        && !(playOnce && fired)
        && CutsceneDirector.Instance != null
        && !CutsceneDirector.Instance.IsPlaying;

    public void Interact(Interactor interactor)
    {
        if (!CanInteract() || interactor == null) return;
        StartCoroutine(PlayThenInvoke(interactor));
    }

    private IEnumerator PlayThenInvoke(Interactor interactor)
    {
        busy = true;
        try
        {
            bool accepted = false;
            yield return CutsceneRunner.PlayAndAwait(cutscene, interactor.gameObject, ok => accepted = ok);
            if (accepted) fired = true;

            try { onCutsceneEnded?.Invoke(interactor); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        finally
        {
            busy = false;
        }
    }
}
