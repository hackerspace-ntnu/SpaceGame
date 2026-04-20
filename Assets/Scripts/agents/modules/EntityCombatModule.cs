// Drives melee attacks when a target is within range.
// Extends BehaviourModuleBase so it participates in the module system:
// it can be toggled, suppressed by MountSuppressor, and uses the shared priority field.
// Does NOT move the entity — pair with ChaseModule (same or higher priority) for movement.
// Optionally assign an AgentWeaponDefinition to drive damage from a shared asset.
// For ranged combat use AgentRangedCombatModule instead.
using System;
using UnityEngine;
using UnityEngine.Events;

public class EntityCombatModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Weapon")]
    [Tooltip("Optional. When assigned, damage is taken from the weapon asset. Falls back to attackDamage below.")]
    [SerializeField] private AgentWeaponDefinition weaponDefinition;

    [Header("Attack")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private int attackDamage = 10;

    [Header("Events")]
    [Tooltip("Fired each time an attack lands. Wire to animator triggers, SFX, VFX, etc.")]
    public UnityEvent<Transform> OnAttack;
    [Tooltip("Fired when entering attack range.")]
    public UnityEvent OnEnterRange;
    [Tooltip("Fired when leaving attack range.")]
    public UnityEvent OnExitRange;

    public event Action OnAttackEvent;

    private float cooldownTimer;
    private bool inRange;
    private IDamageable targetDamageable;

    public override bool ClaimsMovement => false;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

    private void OnEnable()
    {
        cooldownTimer = 0f;
        inRange = false;
    }

    public override string ModuleDescription =>
        "Deals melee damage to a target when within attackRange. Never claims movement — runs alongside ChaseModule which handles positioning.\n\n" +
        "• weaponDefinition — optional ScriptableObject; overrides attackDamage when assigned\n" +
        "• attackRange — distance at which hits land\n" +
        "• attackCooldown — seconds between attacks\n" +
        "• attackDamage — HP removed per hit (ignored when weaponDefinition is set)\n" +
        "• OnAttack event — wire to animator triggers, VFX, or sound";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);
        bool nowInRange = distance <= attackRange;

        if (nowInRange && !inRange)
        {
            inRange = true;
            OnEnterRange?.Invoke();
        }
        else if (!nowInRange && inRange)
        {
            inRange = false;
            OnExitRange?.Invoke();
        }

        if (inRange)
        {
            cooldownTimer -= deltaTime;
            if (cooldownTimer <= 0f)
            {
                Attack();
                cooldownTimer = attackCooldown;
            }
        }

        return null; // Never claims movement.
    }

    private void Attack()
    {
        int damage = weaponDefinition != null ? weaponDefinition.damagePerHit : attackDamage;
        if (targetDamageable != null && targetDamageable.Alive)
            targetDamageable.Damage(damage);

        OnAttack?.Invoke(target);
        OnAttackEvent?.Invoke();
    }

    private void TryResolveTarget()
    {
        if (target)
        {
            if (targetDamageable == null)
                targetDamageable = target.GetComponentInChildren<IDamageable>();
            return;
        }

        Transform resolved = EntityTargetRegistry.Resolve(targetTag, transform.position);
        if (resolved && EntityFaction.IsValidTarget(transform, resolved, requiredRelationship))
        {
            target = resolved;
            targetDamageable = resolved.GetComponentInChildren<IDamageable>();
        }
    }

    protected override void OnValidate()
    {
        attackRange = Mathf.Max(0.1f, attackRange);
        attackCooldown = Mathf.Max(0.1f, attackCooldown);
        attackDamage = Mathf.Max(0, attackDamage);
    }
}
