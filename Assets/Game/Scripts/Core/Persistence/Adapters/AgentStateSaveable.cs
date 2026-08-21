using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what an agent was doing: who it was fighting, what it remembers, and which tuning
    /// profile was in force.
    ///
    /// <b>Why this is worth saving at all.</b> Pose and health alone make a reload look like a reset.
    /// A Golem mid-fight comes back standing in the same spot at the same health with no idea anyone is
    /// there, and re-notices the player a second later from scratch — which reads as the world having
    /// forgotten what was happening, because it had.
    ///
    /// <b>One memory, two components.</b> <see cref="PerceptionModule"/> keeps its own copy of the
    /// last-known position, written whenever <c>AgentTargeting</c> asks it to look. Only
    /// <c>AgentTargeting</c>'s copy is stored, and this saver pushes it into both on restore. A second
    /// saver for the perception copy would be a second answer to one question, and the winner would be
    /// whichever ran last.
    ///
    /// <b>Deferred, because targets are references.</b> A target is another entity or a player, and
    /// neither reliably exists when this agent's scene hydrates. The refs are read in
    /// <see cref="RestoreState"/> and resolved in <see cref="OnLoadComplete"/>.
    ///
    /// <b>Patrol progress is not here any more.</b> See <see cref="State.patrolIndex"/>.
    /// </summary>
    [RequireComponent(typeof(AgentTargeting))]
    public class AgentStateSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "agent";

        private AgentTargeting targeting;
        private PerceptionModule perception;

        private AgentTargeting Targeting => targeting != null ? targeting : targeting = GetComponent<AgentTargeting>();
        private PerceptionModule Perception => perception != null ? perception : perception = GetComponent<PerceptionModule>();

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

            /// <summary>
            /// Seconds since the target was last visible, so memory keeps expiring on schedule.
            ///
            /// <para>
            /// <b>Restored clamped to the agent's own memory duration, not verbatim.</b> This is a
            /// simulation timer, and the house reading of a save/load gap is that the world was
            /// paused, not that it aged — <c>DayNightSaveable</c> restores the hour rather than
            /// advancing it by the wall-clock gap, and an agent's attention should not be the one
            /// thing in the world that ran while the game was closed. So the elapsed real time
            /// between quitting and loading is deliberately ignored.
            /// </para>
            /// <para>
            /// The clamp is what the verbatim restore was missing. Nothing guarantees this value
            /// still fits the live rules: the profile may have been retuned, swapped by
            /// <c>ApplyProfile</c>, or the record may come from a build whose memoryDuration was
            /// longer. Restoring a 30-second-old memory into a 6-second memory leaves the agent
            /// holding something it should already have dropped, and it would then survive until the
            /// next frame's expiry check instead of never having existed. Clamped, a memory that is
            /// past its expiry comes back exactly at it and expires on the first tick.
            /// </para>
            /// </summary>
            public float timeSinceSeen;

            /// <summary>
            /// The GUID of the <c>TargetingProfile</c> that was live, or empty for an agent running
            /// its inline fields.
            ///
            /// <c>AgentTargeting.ApplyProfile</c> is a runtime swap — MatchManager gives arena bots a
            /// more aggressive profile than the prefab ships with — and nothing recorded it, so Awake
            /// silently re-read the serialized one and a restored arena bot went back to open-world
            /// tuning.
            /// </summary>
            public string profileId;

            /// <summary>
            /// Vestigial. Patrol progress moved to its own saver, because <c>PatrolRobot</c> and
            /// <c>DeathmatchBot</c> have a <c>PatrolModule</c> and no <c>AgentTargeting</c> — so this
            /// saver was never added to them and their patrol was never saved at all.
            ///
            /// The fields stay in the struct, unwritten and unread, so a save file from before the
            /// split still deserializes without a migration. Do not reuse the names.
            /// </summary>
            public int patrolIndex;

            /// <inheritdoc cref="patrolIndex"/>
            public int patrolDirection;
        }

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            if (Targeting == null) return null;

            TargetingProfile profile = Targeting.ActiveProfile;

            return new State
            {
                target = SaveRef.From(Targeting.Target),
                lastAttacker = SaveRef.From(Targeting.LastAttacker),
                lastKnownPosition = Targeting.LastKnownPosition,
                hasLastKnownPosition = Targeting.HasLastKnownPosition,
                timeSinceSeen = Targeting.TimeSinceSeen,
                profileId = profile != null ? profile.ID : string.Empty,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pending = default;
            if (state == null) return;

            // Through the shared serializer, not by probing tokens. It is the only thing that knows how
            // to read a Vector3 back — the Unity converters are registered there, and a Vector3 read
            // without them recurses through its own properties into a stack overflow. Missing fields
            // become defaults, which is exactly the right reading of a save from before they existed.
            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = true;

            // Tuning needs no reference and no world, so it is applied straight away. Retried in
            // OnLoadComplete because the profile asset is only in the registry once something that
            // references it has been loaded, and a profile applied at runtime may belong to a scene
            // that is still streaming in.
            TryApplyProfile(pending.profileId);
        }

        public void OnLoadComplete()
        {
            if (!hasPending || Targeting == null) return;

            // Consumed: this pass can run twice for one object (the load-wide pass, then again if its
            // chunk hydrates later) and re-applying a stale memory would drag the agent back to a
            // position it has since investigated and left.
            State state = pending;
            hasPending = false;

            TryApplyProfile(state.profileId);

            state.target.TryResolve(out GameObject target);
            state.lastAttacker.TryResolve(out GameObject attacker);

            // Clamped rather than restored as written — see State.timeSinceSeen for why.
            float timeSinceSeen = Mathf.Clamp(state.timeSinceSeen, 0f, Targeting.MemoryDuration);

            // A target that no longer resolves still leaves the memory intact, which is deliberate: the
            // agent knows something was over there, which is exactly what it knew before the save.
            Targeting.RestoreMemory(
                target != null ? target.transform : null,
                state.lastKnownPosition,
                state.hasLastKnownPosition,
                timeSinceSeen,
                attacker != null ? attacker.transform : null);

            // The perception copy of the same memory, from the same record. It clamps again against
            // its own (possibly shorter) memoryDuration.
            //
            // Compared with Unity's == rather than ?., which does a plain reference check and would
            // happily call into a destroyed component.
            PerceptionModule eyes = Perception;
            if (eyes != null)
                eyes.RestoreMemory(state.lastKnownPosition, state.hasLastKnownPosition, timeSinceSeen);
        }

        /// <summary>
        /// Puts the recorded profile back, if it can be found.
        ///
        /// Silent on failure, and deliberately so: not finding it leaves the prefab's own profile in
        /// force, which is the behaviour this project had before profiles were persisted at all. A
        /// profile built at runtime with <c>CreateInstance</c> has no id and is never recorded, so it
        /// takes this path too — an object that exists only for one session cannot be named in a file.
        /// </summary>
        private void TryApplyProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || Targeting == null) return;

            TargetingProfile profile = Registry<TargetingProfile>.Get(profileId);
            if (profile == null || profile == Targeting.ActiveProfile) return;

            Targeting.ApplyProfile(profile);
        }
    }
}
