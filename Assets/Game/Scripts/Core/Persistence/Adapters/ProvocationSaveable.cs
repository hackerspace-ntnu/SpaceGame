using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a peaceful creature's grudge: who hurt it, and how far through calming down it was.
    ///
    /// <b>The gap this closes is the largest one in the game's AI.</b> "Peaceful until provoked" is
    /// implemented by having nothing acquire the creature at all — Fauna has no rows in the faction
    /// table, so <c>AgentTargeting.Reevaluate</c> is structurally incapable of finding the player —
    /// and <see cref="ProvocationModule"/> re-asserting the attacker every frame is the only thing
    /// holding the fight together. Nothing recorded that. Shoot a Golem, quit, reload, and it was
    /// peaceful again permanently, because the one component that could ever have made it hostile had
    /// forgotten and no other component was able to.
    ///
    /// <b>Deferred, because the aggressor is usually the player.</b> Netcode spawns players at a time
    /// this system does not control, so the reference is read in <see cref="RestoreState"/> and
    /// resolved in <see cref="OnLoadComplete"/> — kept on failure, since a player who has not rejoined
    /// yet is an ordinary answer rather than a dead reference.
    ///
    /// <b>The restore goes through <c>Provoke</c>.</b> Not around it: the grudge only has teeth
    /// because the module hands the target to <c>AgentTargeting</c> and, at
    /// <c>[DefaultExecutionOrder(-40)]</c>, keeps handing it over after each acquisition pass has run.
    /// A grudge restored by writing the field would leave a creature that is angry at nobody.
    /// </summary>
    [RequireComponent(typeof(ProvocationModule))]
    public class ProvocationSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "provocation";     // written into save files — NEVER rename

        private ProvocationModule provocation;

        private ProvocationModule Provocation =>
            provocation != null ? provocation : provocation = GetComponent<ProvocationModule>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Who the creature is angry at. Unset means it is at peace.</summary>
            public SaveRef aggressor;

            /// <summary>
            /// Seconds the aggressor has already spent outside the leash. Restored so a creature
            /// saved 50 seconds into a 60 second calm-down does not come back with a full clock —
            /// walking away and waiting it out has to keep working across a reload.
            /// </summary>
            public float calmingFor;

            /// <summary>
            /// The aggression meter, 0-100. **Appended** — an older save has no such field and
            /// deserializes it as 0, which reads as "calm", which is what every save written before
            /// the meter existed meant.
            ///
            /// Worth saving separately from the grudge because the interesting states are the ones
            /// BELOW a grudge: a nomad you have spent twenty seconds making wary is a different
            /// animal from one you have never met, and losing that on a reload hands the player back
            /// a camp they had already half-provoked.
            /// </summary>
            public float aggression;
        }

        private SaveRef pendingAggressor;
        private float pendingCalmingFor;
        private float pendingAggression;
        private bool hasPending;

        public object CaptureState()
        {
            if (Provocation == null) return null;

            // A creature that is neither fighting nor wound up is in the state a missing entry
            // already means. Writing a row per calm creature would put one in the save file for
            // every animal in the world.
            if (!Provocation.IsProvoked && Provocation.Aggression <= 0f) return null;

            // Whoever the anger is pointed at: the aggressor in a fight, the provoker on the way up.
            SaveRef aggressor = SaveRef.From(
                Provocation.IsProvoked ? Provocation.Aggressor : Provocation.Provoker);

            // An aggressor nothing can describe — an unsaved scene object, or something already
            // destroyed — leaves no grudge worth recording. The meter is still worth keeping: a
            // wound-up creature that has forgotten who wound it up is wound up all the same.
            if (!aggressor.IsSet && Provocation.IsProvoked) return null;

            return new State
            {
                aggressor = aggressor,
                calmingFor = Provocation.CalmingFor,
                aggression = Provocation.Aggression,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pendingAggressor = SaveRef.None;
            pendingCalmingFor = 0f;
            pendingAggression = 0f;

            // No entry means the creature was at peace when the world was saved, and
            // ProvocationModule.OnEnable already resets to exactly that. Nothing to undo.
            if (state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            pendingAggression = restored.aggression;
            pendingAggressor = restored.aggressor;
            pendingCalmingFor = restored.calmingFor;

            // A meter with nobody attached still restores — the creature comes back wound up and
            // cools down from where it was, which is what a player who spent a minute menacing a
            // camp and then quit should find when they come back.
            if (!restored.aggressor.IsSet)
            {
                if (pendingAggression > 0f) Provocation.RestoreAggression(pendingAggression, null);
                return;
            }

            hasPending = true;
        }

        public void OnLoadComplete()
        {
            if (!hasPending || Provocation == null) return;

            // Kept on failure, consumed on success. The aggressor is nearly always a player, and in
            // multiplayer they arrive one at a time — dropping the reference on the first pass would
            // permanently pardon whoever had not rejoined yet.
            if (!pendingAggressor.TryResolve(out GameObject aggressor)) return;

            hasPending = false;

            // A full meter is a grudge, and the grudge path is the one with teeth — it hands the
            // target to AgentTargeting and re-asserts it every frame. Anything below the top is
            // just a reading, and must NOT re-announce itself on the loading screen.
            if (pendingAggression >= AggressionMath.Max)
                Provocation.RestoreGrudge(aggressor.transform, pendingCalmingFor);
            else
                Provocation.RestoreAggression(pendingAggression, aggressor.transform);
        }
    }
}
