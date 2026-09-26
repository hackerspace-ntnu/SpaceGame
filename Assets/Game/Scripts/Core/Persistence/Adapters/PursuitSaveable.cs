using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the private quarry held by <see cref="KeepDistanceModule"/>, the movement module
    /// that resolves its own target rather than reading <c>AgentTargeting</c>.
    ///
    /// <b>Low stakes, cheap to fix.</b> The target mostly re-derives from <c>AgentTargeting</c>
    /// within a frame. What does not re-derive is the interval: <c>OnEnable</c> nulls the target and
    /// zeroes the timer, so a kiting agent with nothing acquired reloads standing on its spawn point
    /// for up to a full retarget interval before it moves again. That is the bug, and it is one
    /// struct.
    ///
    /// <b>Deferred, because a target is a reference.</b> Read in <see cref="RestoreState"/>, resolved
    /// in <see cref="OnLoadComplete"/> — the quarry may be a player who has not rejoined yet, so each
    /// ref is consumed only on success and kept on failure.
    /// </summary>
    public class PursuitSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "pursuit";     // written into save files — NEVER rename

        private KeepDistanceModule keepDistance;

        // Lazy, not cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe. Re-queried every access because the
        // module may genuinely be absent — GetComponent returning null is the normal case here.
        private KeepDistanceModule KeepDistance =>
            keepDistance != null ? keepDistance : keepDistance = GetComponent<KeepDistanceModule>();

        public string SaveKey => Key;

        public struct State
        {
            public SaveRef kiteTarget;
            public float kiteTimer;
        }

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            if (KeepDistance == null) return null;

            return new State
            {
                kiteTarget = SaveRef.From(KeepDistance.KiteTarget),
                kiteTimer = KeepDistance.RetargetTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            // Staged state cleared on the null path too, or a saver that was restored once keeps a
            // ref that the record no longer claims.
            hasPending = false;
            pending = default;

            if (state == null)
            {
                KeepDistance?.RestoreKeepDistance(null, 0f);
                return;
            }

            // Through the shared serializer — a SaveRef, like a Vector3, is only readable with the
            // converters registered on it.
            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = true;

            // The timers need no reference and no world, so they are applied now rather than waiting
            // on a pass they do not depend on. The targets follow in OnLoadComplete.
            KeepDistance?.RestoreKeepDistance(KeepDistance.KiteTarget, pending.kiteTimer);
        }

        // Runs many times: once world-wide, again on every PlayerBound, again per late chunk hydrate.
        // Idempotent, and each ref is dropped from the pending set only once it has resolved.
        public void OnLoadComplete()
        {
            if (!hasPending) return;

            bool outstanding = false;

            if (KeepDistance != null && TryTake(ref pending.kiteTarget, out Transform kiteTarget, ref outstanding))
                KeepDistance.RestoreKeepDistance(kiteTarget, pending.kiteTimer);

            // Kept only while something might still arrive. A ref that named a player who has not
            // rejoined resolves on a later pass; one that named a dead entity never will, and the
            // module simply re-scans, which is the correct world.
            hasPending = outstanding;
        }

        /// <summary>
        /// Resolves one ref, consuming it on success. Returns false — and flags
        /// <paramref name="outstanding"/> — when the referent is not here yet, so the next pass tries
        /// again. An unset ref is not outstanding: there was simply nothing to restore.
        /// </summary>
        private static bool TryTake(ref SaveRef reference, out Transform resolved, ref bool outstanding)
        {
            resolved = null;
            if (!reference.IsSet) return false;

            if (!reference.TryResolve(out GameObject target))
            {
                outstanding = true;
                return false;
            }

            reference = SaveRef.None;       // consumed only on success
            resolved = target != null ? target.transform : null;
            return true;
        }
    }
}
