using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists which cover point an agent had claimed, and whether it had got there yet.
    ///
    /// Two visible failures without it, both from <c>CoverModule.OnEnable</c> vacating: every agent
    /// pinned behind a rock steps out into the open the moment the world reloads, and — because the
    /// reservation nobody re-takes is free — two agents can then claim one single-occupant point and
    /// stand inside each other.
    ///
    /// <b>The occupancy count is not stored.</b> It is rebuilt from the agents that re-claim their
    /// reservation, through <c>CoverPoint.TryOccupy</c>, exactly as it is built during play. A stored
    /// count would be a second record of the same fact and could disagree with the agents it is
    /// supposed to describe — and a save taken while a chunk was unloaded would restore a count with
    /// no one behind it, permanently blocking the cover.
    ///
    /// <b>Deferred, and referenced by a derived id rather than a <see cref="SaveRef"/>.</b> A cover
    /// point is a bare marker with no <c>SaveableEntity</c>, so <c>SaveRef.From</c> cannot describe
    /// one — see <c>CoverPoint.StableId</c>. The point may also live in a chunk that is still
    /// streaming in, which is what the deferral is for.
    /// </summary>
    [RequireComponent(typeof(CoverModule))]
    public class CoverSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "cover";           // written into save files — NEVER rename

        private CoverModule cover;

        private CoverModule Cover => cover != null ? cover : cover = GetComponent<CoverModule>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>The claimed point's <c>CoverPoint.StableId</c>. Empty means no reservation.</summary>
            public string coverId;

            /// <summary>
            /// Whether the agent had reached the point. Restored because it is what separates
            /// "walking to cover" from "in cover, holding and facing the threat", and re-deriving it
            /// from distance would put an agent that stopped just short back on the move.
            /// </summary>
            public bool arrivedAtCover;

            public float retargetTimer;
        }

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            if (Cover == null) return null;

            CoverPoint point = Cover.OccupiedCover;
            if (point == null) return null;

            return new State
            {
                coverId = point.StableId,
                arrivedAtCover = Cover.ArrivedAtCover,
                retargetTimer = Cover.RetargetTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pending = default;

            if (Cover == null) return;

            // No entry means the agent held no cover, and OnEnable already vacates. Nothing to undo —
            // and calling RestoreCover here would arm the module's restore latch for a restore that
            // is not happening.
            if (state == null) return;

            pending = state.ToObject<State>(SaveSerializer.Serializer);
            if (string.IsNullOrEmpty(pending.coverId)) return;

            hasPending = true;
        }

        public void OnLoadComplete()
        {
            if (!hasPending || Cover == null) return;

            // Kept on failure: null here means the point's scene has not streamed in yet, and this
            // pass runs again for every scene that hydrates afterwards. A point that never comes back
            // — because the scene was edited and the derived id moved with it — simply leaves the
            // agent without cover, which it will re-seek on its next tick.
            CoverPoint point = CoverPointRegistry.FindById(pending.coverId);
            if (point == null) return;

            // Consumed whether or not the claim succeeds. A refusal means somebody else got there
            // first, which is a settled answer rather than something to retry — and RestoreCover is
            // idempotent for the agent that did win it.
            hasPending = false;
            Cover.RestoreCover(point, pending.arrivedAtCover, pending.retargetTimer);
        }
    }
}
