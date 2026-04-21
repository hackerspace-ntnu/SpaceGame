// Deals melee damage to a target when within attack range.
// Does NOT move — pair with ChaseModule for positioning.
// Never claims movement, runs as a side-effect module alongside movement modules.
using System;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;

public class CloseCombatModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Attack")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private int attackDamage = 10;

    [Header("Animation")]
    [Tooltip("Trigger to fire on each attack. Leave empty to disable.")]
    [SerializeField] private string attackAnimTrigger = "Meele";

    [Header("Events")]
    public UnityEvent<Transform> OnAttack;
    public event Action OnAttackEvent;

    [Header("Audio")]
    [SerializeField] private EventReference attackSound;

    private float cooldownTimer;
    private Animator animator;

    public override bool ClaimsMovement => false;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);
    private void OnEnable() => cooldownTimer = 0f;

    private void Awake()
    {
        FindChildByName("Sword")?.SetActive(IsActive);
        animator = GetComponentInChildren<Animator>();
    }

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance > attackRange)
            return null;

        cooldownTimer -= deltaTime;
        if (cooldownTimer > 0f)
            return null;

        Attack();
        cooldownTimer = attackCooldown;

        return null;
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
        if (target) return;
        Transform candidate = EntityTargetRegistry.Resolve(targetTag, transform.position);
        if (candidate && EntityFaction.IsValidTarget(transform, candidate, requiredRelationship))
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
    }
}
