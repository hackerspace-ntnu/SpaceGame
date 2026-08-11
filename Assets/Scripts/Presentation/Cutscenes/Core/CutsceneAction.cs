using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// ITriggerable action that plays a Cutscene for the initiator, then optionally invokes a
    /// UnityEvent. Pair with any trigger component (InteractableTrigger, VolumeTrigger, or a
    /// scripted Trigger() call). The cutscene is the "what plays"; the trigger is the "how it
    /// fires"; the UnityEvent is the "what happens after" — none of them know about each other.
    /// </summary>
    [AddComponentMenu("Cutscenes/Cutscene Action")]
    public class CutsceneAction : MonoBehaviour, ITriggerable
    {
        [SerializeField] private Cutscene cutscene;

        [Tooltip("If true, can only be triggered once per scene load.")]
        [SerializeField] private bool playOnce;

        [Tooltip("Invoked after the cutscene finishes. Wire any post-cutscene actions here.")]
        [SerializeField] private UnityEvent<GameObject> onCutsceneEnded;

        private bool busy;
        private bool fired;

        public bool CanTrigger(GameObject initiator)
        {
            if (cutscene == null) return false;
            if (busy) return false;
            if (playOnce && fired) return false;
            if (CutsceneDirector.Instance == null) return false;
            if (CutsceneDirector.Instance.IsPlaying) return false;
            return true;
        }

        public Coroutine Trigger(GameObject initiator)
        {
            if (!CanTrigger(initiator)) return null;
            return StartCoroutine(Run(initiator));
        }

        private IEnumerator Run(GameObject initiator)
        {
            busy = true;
            try
            {
                bool accepted = false;
                yield return CutsceneRunner.PlayAndAwait(cutscene, initiator, ok => accepted = ok);

                // Only mark "fired" if the cutscene actually played. If the Director rejected
                // it (another cutscene running), we still invoke the post-action so the player
                // isn't stranded, but a playOnce action isn't bricked.
                if (accepted) fired = true;

                try { onCutsceneEnded?.Invoke(initiator); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            finally
            {
                busy = false;
            }
        }
    }
}
