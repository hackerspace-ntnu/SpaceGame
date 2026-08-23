using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the private quarry held by the three movement modules that resolve their own target:
    /// <see cref="HuntModule"/>, <see cref="KeepDistanceModule"/> and <see cref="ApproachModule"/>.
    ///
    /// <b>Why one saver for three modules.</b> They hold the same two fields for the same reason — a
    /// target found by <c>TargetResolution</c> and the countdown to the next scan — and an agent
    /// almost never has more than one of them. Three keys holding one idea would be three places to
    /// forget.
    ///
    /// <b>Low stakes, cheap to fix.</b> All three mostly re-derive from <c>AgentTargeting</c> within
    /// a frame. What they do not re-derive is the interval: <c>HuntModule.OnEnable</c> nulls the
    /// target and zeroes the timer, so an arena bot with nothing acquired reloads standing on its
    /// spawn point for up to a full retarget interval before it starts walking again. That is the
    /// bug, and it is one struct.
    ///
    /// <b>Deferred, because a target is a reference.</b> Read in <see cref="RestoreState"/>, resolved
    /// in <see cref="OnLoadComplete"/> — the quarry may be a player who has not rejoined yet, so each
    /// ref is consumed only on success and kept on failure.
    /// </summary>
    public class PursuitSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "pursuit";     // written into save files — NEVER rename

        private HuntModule hunt;
        private KeepDistanceModule keepDistance;
        private ApproachModule approach;

        // Lazy, not cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe. Re-queried every access because the
        // module may genuinely be absent — GetComponent returning null is the normal case here.
        private HuntModule Hunt => hunt != null ? hunt : hunt = GetComponent<HuntModule>();

        private KeepDistanceModule KeepDistance =>
            keepDistance != null ? keepDistance : keepDistance = GetComponent<KeepDistanceModule>();

        private ApproachModule Approach =>
            approach != null ? approach : approach = GetComponent<ApproachModule>();

        public string SaveKey => Key;

        public struct State
        {
            public SaveRef huntTarget;
            public float huntTimer;

            public SaveRef kiteTarget;
            public float kiteTimer;

            public SaveRef approachTarget;
            public float approachTimer;
        }

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            bool any = Hunt != null || KeepDistance != null || Approach != null;
            if (!any) return null;

            return new State
            {
                huntTarget = Hunt != null ? SaveRef.From(Hunt.HuntTarget) : SaveRef.None,
                huntTimer = Hunt != null ? Hunt.RetargetTimer : 0f,

                kiteTarget = KeepDistance != null ? SaveRef.From(KeepDistance.KiteTarget) : SaveRef.None,
                kiteTimer = KeepDistance != null ? KeepDistance.RetargetTimer : 0f,

                approachTarget = Approach != null ? SaveRef.From(Approach.ApproachTarget) : SaveRef.None,
                approachTimer = Approach != null ? Approach.RetargetTimer : 0f,
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
                Hunt?.RestoreHunt(null, 0f);
                KeepDistance?.RestoreKeepDistance(null, 0f);
                Approach?.RestoreApproach(null, 0f);
                return;
            }

            // Through the shared serializer — a SaveRef, like a Vector3, is only readable with the
            // converters registered on it.
            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = true;

            // The timers need no reference and no world, so they are applied now rather than waiting
            // on a pass they do not depend on. The targets follow in OnLoadComplete.
            Hunt?.RestoreHunt(Hunt.HuntTarget, pending.huntTimer);
            KeepDistance?.RestoreKeepDistance(KeepDistance.KiteTarget, pending.kiteTimer);
            Approach?.RestoreApproach(Approach.ApproachTarget, pending.approachTimer);
        }

        // Runs many times: once world-wide, again on every PlayerBound, again per late chunk hydrate.
        // Idempotent, and each ref is dropped from the pending set only once it has resolved.
        public void OnLoadComplete()
        {
            if (!hasPending) return;

            bool outstanding = false;

            if (Hunt != null && TryTake(ref pending.huntTarget, out Transform huntTarget, ref outstanding))
                Hunt.RestoreHunt(huntTarget, pending.huntTimer);

            if (KeepDistance != null && TryTake(ref pending.kiteTarget, out Transform kiteTarget, ref outstanding))
                KeepDistance.RestoreKeepDistance(kiteTarget, pending.kiteTimer);

            if (Approach != null && TryTake(ref pending.approachTarget, out Transform approachTarget, ref outstanding))
                Approach.RestoreApproach(approachTarget, pending.approachTimer);

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
