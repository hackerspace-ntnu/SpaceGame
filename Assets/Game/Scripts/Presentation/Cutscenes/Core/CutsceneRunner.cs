using System.Collections;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    // Static helpers for the common "play cutscene and await its end" pattern. Used by
    // CutsceneAction, WalkThroughCutsceneEffect, and any ad-hoc story code.
    //
    // Caller owns the StartCoroutine. The helper just yields until the Director's
    // OnCutsceneEnded fires for the specific cutscene we asked it to play.
    public static class CutsceneRunner
    {
        /// <summary>
        /// Play <paramref name="cutscene"/> through CutsceneDirector and yield until it
        /// finishes. <paramref name="initiator"/> is the entity the cutscene is about
        /// (player or AI agent) — pass null to use the local player.
        /// <paramref name="started"/> is set true if the Director accepted the play (it can
        /// reject if another cutscene is already running).
        /// </summary>
        public static IEnumerator PlayAndAwait(Cutscene cutscene, GameObject initiator = null,
                                               System.Action<bool> started = null)
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

            bool accepted = director.Play(cutscene, initiator);
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
}
