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
    public class CutsceneAction : MonoBehaviour, ITriggerable, SpaceGame.Persistence.IPersistentEntity
    {
        [SerializeField] private Cutscene cutscene;

        [Tooltip("If true, can only be triggered once per scene load.")]
        [SerializeField] private bool playOnce;

        [Tooltip("Invoked after the cutscene finishes. Wire any post-cutscene actions here.")]
        [SerializeField] private UnityEvent<GameObject> onCutsceneEnded;

        private bool busy;
        private bool fired;

        /// <summary>Whether a <c>playOnce</c> action has already had its turn.</summary>
        public bool HasPlayed => fired;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// The tooltip on <c>playOnce</c> says "once per scene load", and that was literally true —
        /// so a one-time cutscene played again every time the world was loaded, and any UnityEvent
        /// hung off <c>onCutsceneEnded</c> fired again with it. Saving the flag is what makes the
        /// setting mean "once", which is what a designer ticking it intends.
        /// </summary>
        public void RestorePlayed(bool played) => fired = played;

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
