using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists whether a play-once cutscene has already played.
    ///
    /// <c>CutsceneAction.playOnce</c> is documented as "can only be triggered once per scene load",
    /// and that was exactly what it did: the flag lived in a private bool with no record behind it,
    /// so a one-time cutscene replayed on every load — and so did whatever <c>onCutsceneEnded</c>
    /// was wired to, which in a scripted sequence is rarely something that wants running twice.
    ///
    /// Not deferred: the flag depends on nothing but itself.
    /// </summary>
    [RequireComponent(typeof(CutsceneAction))]
    public class CutsceneActionSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "cutscene";

        private CutsceneAction action;

        // Lazy rather than cached in Awake: EditMode tests never run Awake, and a saver that caches
        // there cannot be round-trip tested by PersistenceProbe.
        private CutsceneAction Action =>
            action != null ? action : action = GetComponent<CutsceneAction>();

        public string SaveKey => Key;

        public struct State
        {
            public bool played;
        }

        /// <summary>
        /// Nothing at all until it has played. A cutscene that has not fired is the overwhelming
        /// majority of them, and a key per unplayed cutscene per save is noise.
        /// </summary>
        public object CaptureState() => Action == null || !Action.HasPlayed
            ? null
            : new State { played = true };

        public void RestoreState(JObject state)
        {
            if (Action == null) return;

            // Null means "was at its default when saved", which for this saver means not yet played.
            // Saying so explicitly matters: without it, a scene object that had played in a previous
            // session and is being restored from a save taken before it played would keep the live
            // flag it happens to hold.
            if (state == null)
            {
                Action.RestorePlayed(false);
                return;
            }

            Action.RestorePlayed(state.ToObject<State>(SaveSerializer.Serializer).played);
        }
    }
}
