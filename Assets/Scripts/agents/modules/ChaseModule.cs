// Drives the agent toward the target AgentTargeting picked.
//
// This module used to own detection as well: its own registry query, its own detect/lose
// ranges, its own perception lease. That made it one of five modules on the same agent each
// choosing a target independently, so the agent could chase one entity while shooting another.
// Acquisition now lives in AgentTargeting; this module only answers "how do I get there".
//
// Movement-only: always returns a MoveTo while a target is held. Attack modules (higher
// priority) preempt the frame when they can hit, which is what produces stand-and-fire.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class ChaseModule : BehaviourModuleBase
    {
        [Header("Movement")]
        [Tooltip("NavMesh stopping distance on the approach. Attack modules gate the actual halt, " +
                 "so this only needs to be inside their range.")]
        [SerializeField] private float chaseStopDistance = 1.3f;
        [SerializeField] private float chaseSpeedMultiplier = 1.3f;

        private HerdModule herdModule;

        // Herd slot offset radius. Shrunk to zero for melee agents so they close on the target
        // instead of parking on a ring outside their own attack range.
        private float effectiveSpreadRadius;

        public bool HasTarget { get; private set; }

        private void Awake()
        {
            herdModule = GetComponent<HerdModule>();
            ConfigureMeleeMovement();
        }

        // For agents with a CloseCombatModule: disable the herd ring and tighten chaseStopDistance
        // so the final arrival position (slot radius + stop distance + nav jitter) lands strictly
        // inside attackRange. Without this the agent parks at the ring edge and the attackRange
        // check rejects by half a metre, producing "chases me but never hits".
        //
        // Uses the smallest sibling melee range so every CloseCombatModule on the agent can fire.
        private void ConfigureMeleeMovement()
        {
            float meleeAttackRange = float.MaxValue;
            foreach (CloseCombatModule c in GetComponents<CloseCombatModule>())
                meleeAttackRange = Mathf.Min(meleeAttackRange, c.AttackRange);

            effectiveSpreadRadius = herdModule != null ? herdModule.CombatSpreadRadius : 0f;

            if (meleeAttackRange < float.MaxValue)
            {
                // Herd members clustering on a shared target is the correct shape for melee;
                // spreading them would park everyone outside swing reach.
                effectiveSpreadRadius = 0f;

                // Stop just inside attackRange: in reach, but with visible daylight between
                // colliders so agents don't hug and shove each other.
                float stopCap = Mathf.Max(0.3f, meleeAttackRange - 0.4f);
                if (chaseStopDistance > stopCap)
                    chaseStopDistance = stopCap;
            }
        }

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

        private void OnEnable() => HasTarget = false;

        public override string ModuleDescription =>
            "Drives the agent toward the target chosen by AgentTargeting. Attack modules at higher " +
            "priority preempt with StopAndFace once they can hit.\n\n" +
            "• chaseStopDistance — NavMesh stopping distance on the approach (auto-tightened when a " +
            "CloseCombatModule is present, so the agent ends up inside swing range)\n" +
            "• chaseSpeedMultiplier — speed scale while closing\n\n" +
            "Detection ranges, line-of-sight and target choice all live on AgentTargeting — add a " +
            "TargetingProfile there to tune them.";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            AgentTargeting targeting = context.Targeting;
            if (targeting == null)
                return null;

            HasTarget = targeting.HasTarget;
            if (!HasTarget)
                return null;

            // While the target is out of sight but still acquired, head for where it was last seen.
            // Once AgentTargeting drops it entirely, SearchModule takes over the investigation.
            Vector3 chasePosition = targeting.CanSeeTarget || !targeting.HasLastKnownPosition
                ? targeting.Target.position
                : targeting.LastKnownPosition;

            Vector3 destination = herdModule != null
                ? herdModule.GetSlotPositionAround(chasePosition, effectiveSpreadRadius)
                : chasePosition;

            return MoveIntent.MoveTo(destination, chaseStopDistance, chaseSpeedMultiplier, isRunning: true);
        }

        protected override void OnValidate()
        {
            chaseStopDistance = Mathf.Max(0.01f, chaseStopDistance);
            chaseSpeedMultiplier = Mathf.Max(0.01f, chaseSpeedMultiplier);
        }
    }
}
