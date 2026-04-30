using System.Collections;

// Static helpers for the common "play cutscene and await its end" pattern. Used by
// any caller that wants to chain something after a cutscene (CutsceneInteractable,
// InteriorPortal, ad-hoc story code).
//
// Caller owns the StartCoroutine. The helper just yields until the Director's
// OnCutsceneEnded fires for the specific cutscene we asked it to play.
public static class CutsceneRunner
{
    /// <summary>
    /// Play <paramref name="cutscene"/> through CutsceneDirector and yield until it
    /// finishes. <paramref name="started"/> is set true if the Director accepted the
    /// play (it can reject if another cutscene is already running). If null Director
    /// or null cutscene, returns immediately with started=false.
    /// </summary>
    public static IEnumerator PlayAndAwait(Cutscene cutscene, System.Action<bool> started = null)
    {
        var director = CutsceneDirector.Instance;
        if (cutscene == null || director == null)
        {
            started?.Invoke(false);
            yield break;
        }

        bool ended = false;
        System.Action<Cutscene> onEnd = c => { if (c == cutscene) ended = true; };
        director.OnCutsceneEnded += onEnd;

        bool accepted = director.Play(cutscene);
        started?.Invoke(accepted);

        if (!accepted)
        {
            director.OnCutsceneEnded -= onEnd;
            yield break;
        }

        while (!ended) yield return null;
        director.OnCutsceneEnded -= onEnd;
    }
}
