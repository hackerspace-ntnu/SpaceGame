// Receives alerts from AlertBroadcaster and forces AgentTargeting onto a target the agent has
// not independently detected yet, so chase and both attack modules all act on it at once.
// Drag onto any entity that should respond to ally alerts (guards, pack hunters, etc.).
using UnityEngine;

namespace SpaceGame.Agents
{
    public class AlertReceiverModule : BehaviourModuleBase
    {
        [Header("Alert Response")]
        [Tooltip("How long to investigate the alerted position before giving up if no target is found.")]
        [SerializeField] private float alertDuration = 8f;
        [SerializeField] private float stopDistance = 0.5f;

        private Vector3 alertPosition;
        private float alertTimer;
        private AgentTargeting targeting;

        // Set by RestoreAlert, consumed by the next OnEnable.
        private bool restoredAlert;

        // ── Persisted state ───────────────────────────────────────────────────────
        public Vector3 AlertPosition => alertPosition;

        /// <summary>Seconds of investigation left. Zero means no alert is being acted on.</summary>
        public float AlertTimer => alertTimer;

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive - 1); // 19 — yields to ChaseModule when it has a target

        private void OnEnable()
        {
            // A squad an ally alerted seconds before the save has to come back still converging on
            // the reported position, not idling in formation.
            if (restoredAlert)
            {
                restoredAlert = false;
                return;
            }

            ClearAlert();
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Only the position and the clock. The alerted target itself is not this module's to put
        /// back — <c>ReceiveAlert</c> hands it straight to <c>AgentTargeting</c>, and
        /// <c>AgentStateSaveable</c> owns that.
        /// </summary>
        public void RestoreAlert(Vector3 position, float timer)
        {
            alertPosition = position;
            alertTimer = Mathf.Max(0f, timer);
            restoredAlert = true;
        }

        // Called by AlertBroadcaster and by SettlementAlarm.
        public void ReceiveAlert(Transform target, Vector3 lastKnownPosition)
        {
            alertPosition = lastKnownPosition;
            alertTimer = alertDuration;

            if (!target)
                return;

            // Through the grudge when there is one, not a bare ForceTarget. A receiver whose
            // faction is Neutral toward the target (any nomad) cannot re-acquire it by itself, and
            // AgentTargeting's staleness pass drops a forced target within seconds — the documented
            // "a gunshot target does not stick" failure. ProvocationModule re-asserts it every frame
            // for as long as the leash holds, which is what makes the alert stick. Announce is off:
            // a target that arrived by alert must not be re-broadcast, or one sighting cascades.
            //
            // GetOrAdd rather than a cached Awake reference: alerts can arrive before
            // AgentController has run its own resolve.
            if (!TryGetComponent(out ProvocationModule provocation))
            {
                Targeting.ForceTarget(target);
                return;
            }

            // Who the alert is ABOUT decides what it is worth, and the faction stance already knows.
            //
            //   Somebody this agent is Hostile toward — a Clanker told where the player is — is an
            //   instant grudge. It was going to attack on sight anyway; making it work up to that
            //   through a meter would be a robot cowboy hesitating, and would mean a patrol that
            //   only half-answers its own alarm.
            //
            //   Somebody it is Neutral toward — a nomad told a tribesman has been hurt — is worth
            //   allyHurtGain instead. The first scream makes him wary and the second draws his gun,
            //   so a caravan that hears trouble closes up and warns you before it shoots. That
            //   escalation is the whole point of having a meter rather than a flag, and it only
            //   applies to the people who had no quarrel with you to begin with.
            //
            // A hit landing on this body is an instant fight either way, because hitGain says so.
            // AddAggression no-ops once provoked, so an alert mid-fight costs nothing.
            if (IsHostileTo(target))
                provocation.Provoke(target, announce: false);
            else
                provocation.AddAggression(AggressionInput.AllyHurt, 1f, target);
        }

        /// <summary>
        /// Does this agent's faction already call <paramref name="target"/> an enemy? Read through
        /// EntityFaction so the grudge and goodwill layers get their say too
        /// (<see cref="FactionRelations.Resolve"/>) — an agent that has been pushed into a grudge
        /// answers its own alerts without climbing the meter a second time.
        /// </summary>
        private bool IsHostileTo(Transform target)
        {
            return TryGetComponent(out EntityFaction self)
                   && self.IsHostileTo(target);
        }

        private AgentTargeting Targeting =>
            targeting != null ? targeting : targeting = AgentTargeting.GetOrAdd(gameObject);

        public void ClearAlert()
        {
            alertTimer = 0f;
        }

        public override string ModuleDescription =>
            "Receives alerts from allied AlertBroadcasters and investigates the reported position.\n\n" +
            "• When an ally spots a target, this module hands it straight to AgentTargeting.\n" +
            "• While the agent still has no target, moves to the last known alert position instead.\n" +
            "• alertDuration — how long to investigate before giving up\n" +
            "• Add AlertBroadcaster to the allies that should raise alerts.";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (alertTimer <= 0f)
                return null;

            alertTimer -= deltaTime;

            // Once the agent has actually acquired a target, ChaseModule handles the movement.
            if (context.Targeting != null && context.Targeting.HasTarget)
                return null;

            // Move toward the last known alert position while we don't independently have the target.
            return MoveIntent.MoveTo(alertPosition, stopDistance, 1.2f);
        }

        protected override void OnValidate()
        {
            alertDuration = Mathf.Max(0.1f, alertDuration);
            stopDistance = Mathf.Max(0.01f, stopDistance);
        }
    }
}
