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
        }

        private SaveRef pendingAggressor;
        private float pendingCalmingFor;
        private bool hasPending;

        public object CaptureState()
        {
            if (Provocation == null || !Provocation.IsProvoked) return null;

            SaveRef aggressor = SaveRef.From(Provocation.Aggressor);

            // An aggressor nothing can describe — an unsaved scene object, or something already
            // destroyed — leaves no grudge worth recording. Storing the timer alone would restore a
            // calm-down clock with nothing to calm down from.
            if (!aggressor.IsSet) return null;

            return new State
            {
                aggressor = aggressor,
                calmingFor = Provocation.CalmingFor,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pendingAggressor = SaveRef.None;
            pendingCalmingFor = 0f;

            // No entry means the creature was at peace when the world was saved, and
            // ProvocationModule.OnEnable already resets to exactly that. Nothing to undo.
            if (state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            if (!restored.aggressor.IsSet) return;

            pendingAggressor = restored.aggressor;
            pendingCalmingFor = restored.calmingFor;
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
            Provocation.RestoreGrudge(aggressor.transform, pendingCalmingFor);
        }
    }
}
