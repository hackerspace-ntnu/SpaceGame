// Seeks out the nearest hostile anywhere on the map and walks at it.
//
// AgentTargeting only acquires inside its acquisition range and (by default) needs FOV plus
// line-of-sight. That is the right shape for an ambient world where agents also patrol or
// wander — but an arena match spawns entities hundreds of metres apart with no patrol route to
// bring them together, so without something like this they stand on their spawn points.
//
// Deliberately ignores perception and the acquisition range: this is "everyone knows this is a
// deathmatch and goes looking", not a stealth-aware search. Once the target is close enough to
// be acquired properly, ChaseModule (Reactive) and the attack modules all outrank this.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class HuntModule : BehaviourModuleBase
    {
        [Header("Target")]
        [Tooltip("Only hunt entities with this relationship. Requires EntityFaction on both entities.")]
        [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

        [Tooltip("Seconds between re-resolving the nearest hostile. Keeps the agent from " +
                 "committing to one target while a closer one walks past.")]
        [SerializeField] private float retargetInterval = 2f;

        [Header("Movement")]
        [Tooltip("How close the hunt drives the agent. Chase and the attack modules outrank this " +
                 "long before it matters, so this only needs to be inside their detect range.")]
        [SerializeField] private float stopDistance = 3f;
        [SerializeField] private float speedMultiplier = 1f;
        [SerializeField] private bool run = true;

        private EntityFaction selfFaction;
        private Transform target;
        private float retargetTimer;

        private void Awake() => selfFaction = GetComponent<EntityFaction>();

        // One below Ambient, not at it: AgentController sorts modules with List.Sort,
        // which is unstable, so a tie with a sibling Ambient module (KeepDistance's
        // kiting, in particular) would resolve arbitrarily per agent. Hunting is the
        // coarsest movement an agent has short of wandering and should always lose
        // those ties.
        private void Reset() => SetPriorityDefault(ModulePriority.Ambient - 1);

        /// <summary>Set by a restore, consumed by the next <see cref="OnEnable"/>.</summary>
        private bool restoredThisEnable;

        private void OnEnable()
        {
            if (restoredThisEnable)
            {
                restoredThisEnable = false;
                return;
            }

            target = null;
            retargetTimer = 0f;
        }

        // ─────────── For the save system ───────────
        // Mostly re-derivable from AgentTargeting, but not for free: an arena bot that reloads with
        // no quarry and a zeroed timer stands on its spawn point until the next scan lands.

        public Transform HuntTarget => target;
        public float RetargetTimer => retargetTimer;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreHunt(Transform huntTarget, float timer)
        {
            target = huntTarget;
            retargetTimer = Mathf.Max(0f, timer);
            restoredThisEnable = true;
        }

        public override string ModuleDescription =>
            "Walks toward the nearest hostile entity anywhere on the map, ignoring field-of-view and " +
            "line-of-sight. Fills the gap between spawning and being close enough for ChaseModule to " +
            "aggro — intended for arena/deathmatch agents that have no patrol or wander route.\n\n" +
            "• requiredRelationship — faction relationship a candidate must have (default: Hostile)\n" +
            "• retargetInterval — seconds between re-resolving the nearest hostile\n" +
            "• stopDistance — how close the hunt drives before yielding to Chase/attack modules\n" +
            "• speedMultiplier / run — locomotion while hunting";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Prefer the agent's committed target so the hunt walks toward whoever the combat
            // modules are going to engage. Only fall back to a map-wide scan when nothing has been
            // acquired — which, in an arena, is the situation this module exists for.
            AgentTargeting targeting = context.Targeting;
            if (targeting != null && targeting.HasTarget)
            {
                target = targeting.Target;
                retargetTimer = retargetInterval;
            }
            else
            {
                target = TargetResolution.Refresh(target, ref retargetTimer, retargetInterval,
                                                  deltaTime, selfFaction, requiredRelationship,
                                                  context.Position);
            }

            if (target == null)
                return null;

            return MoveIntent.MoveTo(target.position, stopDistance, speedMultiplier, isRunning: run);
        }

        protected override void OnValidate()
        {
            retargetInterval = Mathf.Max(0.1f, retargetInterval);
            stopDistance = Mathf.Max(0.1f, stopDistance);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }
    }
}
