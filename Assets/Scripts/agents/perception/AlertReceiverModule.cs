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

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive - 1); // 19 — yields to ChaseModule when it has a target
        private void OnEnable() => ClearAlert();

        // Called by AlertBroadcaster.
        public void ReceiveAlert(Transform target, Vector3 lastKnownPosition)
        {
            alertPosition = lastKnownPosition;
            alertTimer = alertDuration;

            // Hand the target straight to the shared targeting decision so chase and both attack
            // modules act on it together. GetOrAdd rather than a cached Awake reference: alerts can
            // arrive before AgentController has run its own resolve.
            if (target)
                Targeting.ForceTarget(target);
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
