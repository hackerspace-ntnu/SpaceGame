using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists which health thresholds a creature has already crossed.
    ///
    /// <b>This one is not a loss, it is a misbehaviour.</b> <c>HealthThresholdReaction.triggered</c>
    /// is the latch that makes a threshold fire ONCE. It is [HideInInspector] on a serialized struct
    /// and is explicitly cleared in <c>OnEnable</c>, so a creature restored at 20% health comes back
    /// with every latch open — and the first hit it takes afterwards re-crosses thresholds it crossed
    /// long ago. The <c>onThresholdReached</c> UnityEvent fires again: the enrage replays, the scream
    /// replays, whatever a designer hung on it replays, on every single load. Saving the latches is
    /// what closes that.
    ///
    /// <b>And the modules those thresholds switched are durable state.</b> Each reaction enables and
    /// disables a list of components, and component <c>enabled</c> flags are exactly the sort of
    /// thing that looks like authoring and is not. Nothing put them back, so an agent that a
    /// threshold had switched OFF — including its <c>AgentController</c>, which is how a corpse is
    /// parked — reloaded switched ON and thinking again. Restoring re-applies the lists SILENTLY:
    /// the module states are consequences that must come back, the UnityEvent is an announcement of a
    /// moment that has already happened and must not.
    ///
    /// <b>Positional, and tolerant of both directions.</b> A reaction added to the prefab since the
    /// save reads as "not yet fired", which is the right answer for a threshold that did not exist to
    /// be crossed; one removed since is simply not asked about.
    ///
    /// Not deferred: the latches and the modules are all on this object.
    /// </summary>
    [RequireComponent(typeof(HealthReactionModule))]
    public class HealthReactionSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "healthReaction";

        private HealthReactionModule reactions;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private HealthReactionModule Reactions =>
            reactions != null ? reactions : reactions = GetComponent<HealthReactionModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool[] triggered;
        }

        public object CaptureState()
        {
            if (Reactions == null) return null;

            bool[] flags = Reactions.TriggeredThresholds();
            if (flags.Length == 0) return null;

            // A creature at full health has fired nothing, and an all-false array is the same
            // statement as no record at all. Dropping the key keeps a world full of untouched
            // wildlife from carrying one entry each.
            foreach (bool fired in flags)
                if (fired) return new State { triggered = flags };

            return null;
        }

        public void RestoreState(JObject state)
        {
            if (Reactions == null) return;

            // Null means no threshold had fired — a healthy creature — and that must be ASSERTED,
            // not skipped. The latch this call sets is also what stops OnEnable from having the last
            // word, so the reset path has to go through the same door as the restore path.
            if (state == null)
            {
                Reactions.RestoreThresholds(null);
                return;
            }

            Reactions.RestoreThresholds(state.ToObject<State>(SaveSerializer.Serializer).triggered);
        }
    }
}
