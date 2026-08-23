using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists whether a creature was running away, and from whom.
    ///
    /// <b>The flag is the whole point.</b> Fleeing starts inside <c>triggerRadius</c> and only stops
    /// outside the larger <c>safeRadius</c>, so for any threat standing between the two the flag is
    /// the only thing that says which side of the hysteresis the creature is on — it cannot be
    /// recomputed from the world. A DuneRat that reloads calm with the player 8 m away, past the 6 m
    /// trigger but inside the 10 m safe radius, will not start fleeing again at all. It stands there.
    ///
    /// <b>Deferred, because the threat is a reference</b> — normally the player, who Netcode spawns on
    /// its own schedule. The flag does not wait on it though: it is applied in
    /// <see cref="RestoreState"/>, because a creature that keeps running from whoever
    /// <c>TargetResolution.Refresh</c> finds next is far closer to right than one that stops.
    /// </summary>
    [RequireComponent(typeof(FleeModule))]
    public class FleeSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "flee";            // written into save files — NEVER rename

        private FleeModule flee;

        private FleeModule Flee => flee != null ? flee : flee = GetComponent<FleeModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool fleeing;

            /// <summary>Who is being run from, if the save system can name them.</summary>
            public SaveRef threat;

            /// <summary>
            /// Seconds until the threat is re-resolved. Restored so a creature does not re-scan the
            /// registry on the first frame of every load, which would drop a threat it already had in
            /// favour of whoever happens to be nearest.
            /// </summary>
            public float retargetTimer;
        }

        private SaveRef pendingThreat;
        private bool hasPending;

        public object CaptureState()
        {
            if (Flee == null || !Flee.IsFleeing) return null;

            return new State
            {
                fleeing = true,
                threat = SaveRef.From(Flee.Threat),
                retargetTimer = Flee.RetargetTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pendingThreat = SaveRef.None;

            if (Flee == null) return;

            if (state == null)
            {
                Flee.RestoreFlee(false, 0f);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            // The flag now, not in the deferred pass: it is self-contained, and holding it back would
            // leave the creature calm for however long the threat takes to resolve — during which the
            // hysteresis has already decided it is not fleeing.
            Flee.RestoreFlee(restored.fleeing, restored.retargetTimer);

            if (!restored.threat.IsSet) return;

            pendingThreat = restored.threat;
            hasPending = true;
        }

        public void OnLoadComplete()
        {
            if (!hasPending || Flee == null) return;

            // Kept on failure, consumed on success — the threat is usually a player, and in
            // multiplayer they arrive one at a time.
            if (!pendingThreat.TryResolve(out GameObject threat)) return;

            hasPending = false;
            Flee.RestoreThreat(threat.transform);
        }
    }
}
