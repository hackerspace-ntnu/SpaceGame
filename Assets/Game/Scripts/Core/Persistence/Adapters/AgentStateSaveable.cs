using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what an agent was doing: who it was fighting, what it remembers, and where it had got
    /// to on its patrol route.
    ///
    /// <b>Why this is worth saving at all.</b> Pose and health alone make a reload look like a reset.
    /// A Golem mid-fight comes back standing in the same spot at the same health with no idea anyone is
    /// there, and re-notices the player a second later from scratch — which reads as the world having
    /// forgotten what was happening, because it had.
    ///
    /// <b>Deferred, because targets are references.</b> A target is another entity or a player, and
    /// neither reliably exists when this agent's scene hydrates. The refs are read in
    /// <see cref="RestoreState"/> and resolved in <see cref="OnLoadComplete"/>.
    /// </summary>
    [RequireComponent(typeof(AgentTargeting))]
    public class AgentStateSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "agent";

        private AgentTargeting targeting;
        private PatrolModule patrol;

        private AgentTargeting Targeting => targeting != null ? targeting : targeting = GetComponent<AgentTargeting>();

        public string SaveKey => Key;

        public struct State
        {
            public SaveRef target;
            public SaveRef lastAttacker;

            /// <summary>
            /// Where the agent last saw its target. Kept even when the target is gone: it is what
            /// <c>SearchModule</c> walks to, so an agent that lost sight of somebody resumes the search
            /// instead of forgetting the chase happened.
            /// </summary>
            public Vector3 lastKnownPosition;

            public bool hasLastKnownPosition;

            /// <summary>Seconds since the target was last visible, so memory keeps expiring on schedule.</summary>
            public float timeSinceSeen;

            public int patrolIndex;
            public int patrolDirection;
        }

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            if (Targeting == null) return null;

            patrol ??= GetComponent<PatrolModule>();

            return new State
            {
                target = SaveRef.From(Targeting.Target),
                lastAttacker = SaveRef.From(Targeting.LastAttacker),
                lastKnownPosition = Targeting.LastKnownPosition,
                hasLastKnownPosition = Targeting.HasLastKnownPosition,
                timeSinceSeen = Targeting.TimeSinceSeen,
                patrolIndex = patrol != null ? patrol.WaypointIndex : 0,
                patrolDirection = patrol != null ? patrol.WaypointDirection : 1,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            if (state == null) return;

            // Through the shared serializer, not by probing tokens. It is the only thing that knows how
            // to read a Vector3 back — the Unity converters are registered there, and a Vector3 read
            // without them recurses through its own properties into a stack overflow. Missing fields
            // become defaults, which is exactly the right reading of a save from before they existed.
            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = true;

            // Patrol progress needs no reference and no world, so it is applied straight away rather
            // than waiting for a pass it does not depend on.
            patrol ??= GetComponent<PatrolModule>();
            patrol?.RestorePatrolProgress(pending.patrolIndex, pending.patrolDirection);
        }

        public void OnLoadComplete()
        {
            if (!hasPending || Targeting == null) return;

            // Consumed: this pass can run twice for one object (the load-wide pass, then again if its
            // chunk hydrates later) and re-applying a stale memory would drag the agent back to a
            // position it has since investigated and left.
            State state = pending;
            hasPending = false;

            state.target.TryResolve(out GameObject target);
            state.lastAttacker.TryResolve(out GameObject attacker);

            // A target that no longer resolves still leaves the memory intact, which is deliberate: the
            // agent knows something was over there, which is exactly what it knew before the save.
            Targeting.RestoreMemory(
                target != null ? target.transform : null,
                state.lastKnownPosition,
                state.hasLastKnownPosition,
                state.timeSinceSeen,
                attacker != null ? attacker.transform : null);
        }
    }
}
