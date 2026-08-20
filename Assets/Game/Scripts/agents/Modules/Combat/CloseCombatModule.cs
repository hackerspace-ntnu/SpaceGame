// Deals melee damage to a target when within attack range.
// Claims movement: returns StopAndFace while in range (preempting ChaseModule) and null otherwise,
// so ChaseModule at lower priority can drive the approach when the target is out of melee reach.
using System;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public class CloseCombatModule : BehaviourModuleBase
    {
        [Header("Attack")]
        [SerializeField] private float attackRange = 5f;
        [Tooltip("Fraction of attackRange the target must exceed before the agent gives up the swing " +
                 "and starts closing again. 1.15 means it holds position out to 115% of attackRange. " +
                 "Without this gap the winner alternates every frame at the range boundary and the " +
                 "NavMesh path is discarded and re-requested until the agent visibly stutters.")]
        [SerializeField] [Range(1f, 2f)] private float rangeExitFactor = 1.15f;
        [SerializeField] private float attackCooldown = 1.2f;
        [SerializeField] private int attackDamage = 10;
        [Tooltip("Seconds the agent stays locked in StopAndFace after a swing fires — keeps the attack committed so it can't start walking mid-animation if the target drifts out of attackRange. Typically set to the length of the attack animation.")]
        [SerializeField] private float attackCommitDuration = 0.5f;

        [Header("Animation")]
        [Tooltip("Trigger to fire on each attack. Leave empty to disable.")]
        [SerializeField] private string attackAnimTrigger = "Meele";

        [Header("Events")]
        public UnityEvent<Transform> OnAttack;
        public event Action OnAttackEvent;

        [Header("Audio")]
        [SerializeField] private SfxId attackId = SfxId.EntityAttack;
        [SerializeField] private EventReference attackSound;

        private float cooldownTimer;
        // Ticks down after a swing fires; while > 0, the module keeps returning StopAndFace regardless
        // of target distance so the in-progress swing can't be interrupted by Chase.
        private float commitTimer;
        // True while the agent is holding position to fight. Combined with rangeExitFactor this is
        // the hysteresis: entering costs attackRange, leaving costs attackRange * rangeExitFactor.
        private bool engaged;
        private Animator animator;

        // Read by ChaseModule (to tighten chaseStopDistance and skip herd-spread offsets that would
        // park the agent outside melee reach) and by AgentTargeting (to cover the range in its
        // acquisition window).
        public float AttackRange => attackRange;

        private void Reset() => SetPriorityDefault(ModulePriority.MeleeAttack);
        private void OnEnable() { cooldownTimer = 0f; commitTimer = 0f; engaged = false; }

        private void Awake()
        {
            FindChildByName("Sword")?.SetActive(IsActive);
            animator = GetComponentInChildren<Animator>();
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Advance timers every frame so a target stepping out and back can't instant-hit,
            // and so the commit window decays even on frames we're not returning an intent.
            cooldownTimer -= deltaTime;
            commitTimer -= deltaTime;

            AgentTargeting targeting = context.Targeting;
            Transform target = targeting != null && targeting.HasTarget ? targeting.Target : null;
            if (target == null)
            {
                engaged = false;
                return null;
            }

            // Mid-swing: keep the agent planted and facing the target regardless of distance,
            // so Chase can't reclaim the frame and start walking while the attack animation plays.
            if (commitTimer > 0f)
                return MoveIntent.StopAndFace(target.position);

            float distance = targeting.DistanceToTarget;
            float threshold = engaged ? attackRange * rangeExitFactor : attackRange;
            if (distance > threshold)
            {
                engaged = false;
                return null;
            }

            engaged = true;

            // Only swing when genuinely inside attackRange — the exit factor exists to stop the
            // agent walking away, not to extend its reach.
            if (cooldownTimer <= 0f && distance <= attackRange)
            {
                Attack(target);
                cooldownTimer = attackCooldown;
                commitTimer = attackCommitDuration;
            }

            return MoveIntent.StopAndFace(target.position);
        }

        private void Attack(Transform target)
        {
            var health = target.GetComponentInChildren<HealthComponent>();
            if (health != null && health.Alive)
                NetDamage.Apply(health.gameObject, attackDamage, transform);

            Sfx.Play(attackId, transform.position, attackSound, GetInstanceID());

            if (animator && !string.IsNullOrEmpty(attackAnimTrigger))
                animator.SetTrigger(attackAnimTrigger);

            OnAttack?.Invoke(target);
            OnAttackEvent?.Invoke();
        }

        private GameObject FindChildByName(string childName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.gameObject;
            return null;
        }

        protected override void OnValidate()
        {
            attackRange = Mathf.Max(0.1f, attackRange);
            attackCooldown = Mathf.Max(0.1f, attackCooldown);
            attackDamage = Mathf.Max(0, attackDamage);
            attackCommitDuration = Mathf.Max(0f, attackCommitDuration);
            SetMinPriority(ModulePriority.MeleeAttack);
        }
    }
}
