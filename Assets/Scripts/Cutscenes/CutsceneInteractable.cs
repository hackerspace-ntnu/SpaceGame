using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// Generic IInteractable: click → play the assigned cutscene → fire OnCutsceneEnded.
//
// Drag whatever cutscene you want into `cutscene` and wire any number of methods into
// `OnCutsceneEnded` in the inspector. No baked-in coupling to any particular action
// (interior load, scene change, sound, dialog, etc.) — that's whatever you wire up.
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
            yield return CutsceneRunner.PlayAndAwait(cutscene, ok => accepted = ok);

            // Only consider the trigger "fired" if the cutscene actually played.
            // (If the Director rejected because something else was running, we still
            // invoke the post-action so the player isn't stranded — but we leave
            // `fired` clear so a playOnce trigger isn't bricked.)
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
