// Keeps a creature at its nest: it stays home, gets its aggression up when someone lingers close,
// and walks back once a fight is over.
//
// The fight itself is not this module's: an intruder inside the nest's guard radius feeds the
// creature's aggression meter as Trespass (ProvocationModule), and when the meter fills, the meter
// provokes — AgentTargeting takes the intruder, and ChaseModule/CloseCombatModule above this module
// on the ladder do the rest. This module only answers "no target": stay home, face the intruder.
// While the creature has a target it passes.
//
// What it looks like — lying down, standing up, the warning roar — is NestPresentation's, which runs
// on every machine; this runs only on the deciding one.
//
// No state of its own to save: the nest is static scene dressing looked up on enable, and the
// aggression meter is ProvocationModule's (ProvocationSaveable).
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public class NestModule : BehaviourModuleBase, IFacingModule
    {
        [Header("Home")]
        [Tooltip("How far from the creature a CreatureNest is looked for.")]
        [SerializeField] private float nestSearchRadius = 40f;
        [Tooltip("Speed multiplier for walking back to the nest after a fight.")]
        [SerializeField] private float returnSpeedMultiplier = 0.6f;

        [Header("Guarding")]
        [Tooltip("Seconds between player sweeps. Every nesting creature pays this.")]
        [SerializeField] private float sweepInterval = 0.25f;
        [Tooltip("Trespass seconds counted per second an intruder spends inside the guard radius. " +
                 "With the meter's default calmRate equal to trespassGainPerSecond, 1 holds level " +
                 "and anything above 1 climbs.")]
        [SerializeField] private float guardTrespassScale = 3f;
        [Tooltip("The same inside the warn radius but outside the guard radius. 1 holds the meter " +
                 "level: the creature watches, but does not work itself up.")]
        [SerializeField] private float warnTrespassScale = 1f;
        [SerializeField] private int facingPriority = 10;

        private readonly List<Transform> players = new List<Transform>(4);

        private CreatureNest nest;
        private ProvocationModule provocation;
        private Transform intruder;
        private float sweepTimer;

        public int FacingPriority => facingPriority;

        private void Reset() => SetPriorityDefault(ModulePriority.Fallback + 2);

        public override string ModuleDescription =>
            "Stays at the nearest CreatureNest. An intruder inside the nest's guard radius climbs " +
            "the aggression meter until it attacks (ProvocationModule does the attacking); with no " +
            "target it walks home. Pair with NestPresentation for lying down and the warning roar.\n\n" +
            "• guardTrespassScale — how fast the guard radius provokes it";

        private void Awake()
        {
            provocation = GetComponent<ProvocationModule>();
            if (provocation == null)
                Debug.LogError($"[NestModule] {name} has no ProvocationModule; it can never defend its nest.", this);
        }

        private void OnEnable()
        {
            nest = CreatureNest.Nearest(transform.position, nestSearchRadius);
            intruder = null;
            sweepTimer = 0f;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (nest == null)
                nest = CreatureNest.Nearest(context.Position, nestSearchRadius);
            if (nest == null)
                return null;

            if (context.Targeting != null && context.Targeting.HasTarget)
            {
                intruder = null;
                return null;
            }

            sweepTimer -= deltaTime;
            if (sweepTimer <= 0f)
            {
                Sweep(sweepInterval - sweepTimer);
                sweepTimer = sweepInterval;
            }

            Vector3 offset = context.Position - nest.Position;
            offset.y = 0f;
            if (offset.sqrMagnitude > nest.HomeRadius * nest.HomeRadius)
                return MoveIntent.MoveTo(nest.Position, nest.HomeRadius * 0.5f, returnSpeedMultiplier);

            // At home, and staying is the behaviour, so this claims the frame.
            return MoveIntent.Idle();
        }

        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            facePosition = intruder != null ? intruder.position : default;
            return intruder != null && !context.IsMoving;
        }

        private void Sweep(float elapsed)
        {
            SessionPlayers.Collect(players);
            Transform nearest = null;
            float nearestSqr = nest.WarnRadius * nest.WarnRadius;
            foreach (Transform player in players)
            {
                float sqr = (player.position - nest.Position).sqrMagnitude;
                if (sqr > nearestSqr) continue;
                nearestSqr = sqr;
                nearest = player;
            }
            intruder = nearest;
            if (intruder == null || provocation == null)
                return;

            bool inside = nearestSqr <= nest.GuardRadius * nest.GuardRadius;
            provocation.AddAggression(AggressionInput.Trespass,
                                      elapsed * (inside ? guardTrespassScale : warnTrespassScale), intruder);
        }

        protected override void OnValidate()
        {
            nestSearchRadius = Mathf.Max(1f, nestSearchRadius);
            returnSpeedMultiplier = Mathf.Max(0.05f, returnSpeedMultiplier);
            sweepInterval = Mathf.Max(0.05f, sweepInterval);
            guardTrespassScale = Mathf.Max(0f, guardTrespassScale);
            warnTrespassScale = Mathf.Max(0f, warnTrespassScale);
        }
    }
}
