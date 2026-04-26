// Deals melee damage to a target when within attack range.
// Claims movement: returns StopAndFace while in range (preempting ChaseModule) and null otherwise,
// so ChaseModule at lower priority can drive the approach when the target is out of melee reach.
using System;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;

public class CloseCombatModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Attack")]
    [SerializeField] private float attackRange = 5f;
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
    [SerializeField] private EventReference attackSound;

    private float cooldownTimer;
    // Ticks down after a swing fires; while > 0, the module keeps returning StopAndFace regardless
    // of target distance so the in-progress swing can't be interrupted by Chase.
    private float commitTimer;
    private Animator animator;
    private EntityFaction selfFaction;

    // Read by ChaseModule at Awake so it can tighten chaseStopDistance and skip herd-spread
    // offsets that would park the agent outside melee reach.
    public float AttackRange => attackRange;

    private void Reset() => SetPriorityDefault(ModulePriority.MeleeAttack);
    private void OnEnable() { cooldownTimer = 0f; commitTimer = 0f; }

    private void Awake()
    {
        FindChildByName("Sword")?.SetActive(IsActive);
        animator = GetComponentInChildren<Animator>();
        selfFaction = GetComponent<EntityFaction>();
    }

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        // Advance timers every frame so a target stepping out and back can't instant-hit,
        // and so the commit window decays even on frames we're not returning an intent.
        cooldownTimer -= deltaTime;
        commitTimer -= deltaTime;

        TryResolveTarget();
        if (!target)
            return null;

        // Mid-swing: keep the agent planted and facing the target regardless of distance,
        // so Chase can't reclaim the frame and start walking while the attack animation plays.
        if (commitTimer > 0f)
            return MoveIntent.StopAndFace(target.position);

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance > attackRange)
            return null;

        if (cooldownTimer <= 0f)
        {
            Attack();
            cooldownTimer = attackCooldown;
            commitTimer = attackCommitDuration;
        }

        return MoveIntent.StopAndFace(target.position);
    }

    private void Attack()
    {
        var health = target.GetComponentInChildren<HealthComponent>();
        if (health != null && health.Alive)
            health.Damage(attackDamage, transform);

        if (!attackSound.IsNull)
            RuntimeManager.PlayOneShot(attackSound, transform.position);

        if (animator && !string.IsNullOrEmpty(attackAnimTrigger))
            animator.SetTrigger(attackAnimTrigger);

        OnAttack?.Invoke(target);
        OnAttackEvent?.Invoke();
    }

    private void TryResolveTarget()
    {
        if (target)
        {
            // Dying entities stay active during their despawn delay, so the Transform ref survives.
            // Drop the current target as soon as it reports dead and re-resolve to a live one.
            IDamageable currentDamageable = target.GetComponentInChildren<IDamageable>();
            if (currentDamageable != null && !currentDamageable.Alive)
                target = null;
            else
                return;
        }

        Transform candidate = EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship, transform.position);
        if (!candidate)
            return;

        IDamageable candidateDamageable = candidate.GetComponentInChildren<IDamageable>();
        if (candidateDamageable != null && !candidateDamageable.Alive)
            return;

        target = candidate;
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
