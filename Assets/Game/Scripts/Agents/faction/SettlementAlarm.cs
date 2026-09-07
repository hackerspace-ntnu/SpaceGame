// A settlement that notices when the wrong people walk in.
//
// Put on the root of a settlement with the faction that owns it. Every scanInterval it asks the
// targeting registry for entities the owner is HOSTILE toward inside `radius`; the moment one is
// there the alarm is raised, and while it is raised every owner-faction agent inside the radius
// that has nothing better to do is handed the nearest intruder. The alarm holds for holdSeconds
// after the last intruder leaves, so stepping out and back in does not re-trigger it every time.
//
// Two halves, on purpose (INVARIANTS: "server decides, every machine presents"):
//
//   DECIDING — which defenders get which target — runs only where Network.Simulates(this) is
//   true, which for a scene component with no NetworkObject means the server and offline. Agents'
//   targets are theirs to replicate through the normal body/presentation path, so nothing here
//   goes on the wire.
//
//   PRESENTING — the siren — runs on every machine from what every machine already knows: the
//   registry holds every EntityFaction on every machine and player positions replicate, so each
//   client reaches the same "there is an intruder" answer by itself. No message, nothing for a
//   late joiner to miss: they hear it as soon as they see the intruder.
//
// It is the first piece of the faction design's territory rule (design doc §3.10). Goodwill is
// not built yet, so "unwelcome" is the authored stance alone: for a Clanker town that is everyone.
// When goodwill lands, the intruder query is what consults it.
//
// Holds no state worth persisting: a raised alarm is a consequence of an intruder standing in
// the radius, and the defenders' targets are already saved by AgentStateSaveable.
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public class SettlementAlarm : MonoBehaviour
    {
        [Header("Territory")]
        [Tooltip("Whose settlement this is. Entities this faction is Hostile toward are intruders; " +
                 "entities Allied to it are its defenders.")]
        [SerializeField] private FactionDefinition owner;
        [SerializeField] private FactionRelationshipTable relationshipTable;
        [Tooltip("Metres from this object that count as inside the settlement.")]
        [SerializeField] private float radius = 170f;

        [Header("Timing")]
        [Tooltip("Seconds between registry scans. Every machine pays this, so not per frame.")]
        [SerializeField] private float scanInterval = 0.5f;
        [Tooltip("Seconds the alarm stays raised after the last intruder has left.")]
        [SerializeField] private float holdSeconds = 20f;
        [Tooltip("Seconds between siren repeats while raised. 0 plays it once per raise.")]
        [SerializeField] private float sirenRepeatSeconds = 8f;

        [Header("Siren")]
        [SerializeField] private SfxId sirenId = SfxId.ShipAlarm;
        [SerializeField] private EventReference sirenSound;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        public bool IsRaised => state.Raised;
        public int IntruderCount { get; private set; }

        private SettlementAlarmLogic.State state;
        private float scanTimer;
        private readonly List<EntityFaction> intruders = new List<EntityFaction>(16);
        private readonly List<EntityFaction> defenders = new List<EntityFaction>(32);

        private void OnEnable()
        {
            state = default;
            scanTimer = 0f;
            IntruderCount = 0;
        }

        private void Update()
        {
            scanTimer -= Time.deltaTime;
            if (scanTimer > 0f)
                return;
            scanTimer = scanInterval;

            EntityTargetRegistry.Query(owner, relationshipTable, FactionRelationship.Hostile,
                                       transform.position, radius, intruders);
            IntruderCount = intruders.Count;

            SettlementAlarmLogic.Signal signal = SettlementAlarmLogic.Step(
                ref state, intruders.Count > 0, Time.time, holdSeconds, sirenRepeatSeconds);

            if (signal.PlaySiren)
                Sfx.Play(sirenId, transform.position, sirenSound, GetInstanceID());

            if (state.Raised && intruders.Count > 0 && Network.Simulates(this))
                RallyDefenders();
        }

        // Every owner-faction agent inside the radius that is not already fighting gets the
        // intruder nearest to it. Through the alert receiver where there is one — that is the
        // path that makes a target stick on a Neutral-faction defender — and straight to targeting
        // otherwise.
        private void RallyDefenders()
        {
            EntityTargetRegistry.Query(owner, relationshipTable, FactionRelationship.Allied,
                                       transform.position, radius, defenders);

            for (int i = 0; i < defenders.Count; i++)
            {
                EntityFaction defender = defenders[i];
                if (defender == null || !defender.TryGetComponent(out AgentTargeting targeting))
                    continue;
                if (targeting.HasTarget)
                    continue;

                Transform nearest = NearestIntruder(defender.transform.position);
                if (nearest == null)
                    continue;

                if (defender.TryGetComponent(out AlertReceiverModule receiver))
                    receiver.ReceiveAlert(nearest, nearest.position);
                else
                    targeting.ForceTarget(nearest);
            }
        }

        private Transform NearestIntruder(Vector3 from)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < intruders.Count; i++)
            {
                Transform candidate = intruders[i].transform;
                if (!TargetResolution.IsViable(candidate))
                    continue;
                float sqr = (candidate.position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }
            return best;
        }

        public void Configure(FactionDefinition ownerFaction, FactionRelationshipTable table, float territoryRadius)
        {
            owner = ownerFaction;
            relationshipTable = table;
            radius = Mathf.Max(1f, territoryRadius);
        }

        private void OnValidate()
        {
            radius = Mathf.Max(1f, radius);
            scanInterval = Mathf.Max(0.1f, scanInterval);
            holdSeconds = Mathf.Max(0f, holdSeconds);
            sirenRepeatSeconds = Mathf.Max(0f, sirenRepeatSeconds);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = IsRaised ? new Color(1f, 0.2f, 0.1f, 0.35f) : new Color(1f, 0.6f, 0.1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }

    /// <summary>
    /// The alarm's clock, with no scene in it so the edge cases can be asserted: raise on the first
    /// intruder, hold after the last leaves, repeat the siren on an interval while raised, and do
    /// not re-raise for someone who steps out and straight back in.
    /// </summary>
    public static class SettlementAlarmLogic
    {
        public struct State
        {
            public bool Raised;
            public float LastIntruderSeen;
            public float LastSiren;
        }

        public struct Signal
        {
            /// <summary>True on the tick the alarm goes from quiet to raised.</summary>
            public bool Raised;
            /// <summary>True on the tick the alarm goes from raised to quiet.</summary>
            public bool Lowered;
            /// <summary>True when the siren should sound this tick.</summary>
            public bool PlaySiren;
        }

        public static Signal Step(ref State state, bool intruderPresent, float now, float holdSeconds, float sirenRepeatSeconds)
        {
            var signal = new Signal();

            if (intruderPresent)
                state.LastIntruderSeen = now;

            if (!state.Raised)
            {
                if (!intruderPresent)
                    return signal;

                state.Raised = true;
                state.LastSiren = now;
                signal.Raised = true;
                signal.PlaySiren = true;
                return signal;
            }

            if (!intruderPresent && now - state.LastIntruderSeen >= holdSeconds)
            {
                state.Raised = false;
                signal.Lowered = true;
                return signal;
            }

            if (sirenRepeatSeconds > 0f && now - state.LastSiren >= sirenRepeatSeconds)
            {
                state.LastSiren = now;
                signal.PlaySiren = true;
            }

            return signal;
        }
    }
}
