// Fires projectiles at a target with optional lead prediction, burst fire, and spread.
// Does NOT move — pair with KeepDistanceModule or ChaseModule for positioning.
// Returns null always (never claims movement), runs as a side-effect module like EntityCombatModule.
using System;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;

public class RangedAttackModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Attack Range")]
    [SerializeField] private float minRange = 3f;
    [SerializeField] private float maxRange = 15f;

    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform muzzleTransform;
    [SerializeField] private float projectileSpeed = 20f;

    [Header("Fire Pattern")]
    [SerializeField] private float fireCooldown = 1f;
    [SerializeField] private int burstCount = 1;
    [SerializeField] private float burstInterval = 0.12f;
    [SerializeField, Range(0f, 45f)] private float spreadAngle = 0f;

    [Header("Lead Prediction")]
    [Tooltip("Compensate for target movement so projectiles lead the target.")]
    [SerializeField] private bool leadTarget = false;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [Tooltip("Trigger to fire on each shot. Leave empty to disable.")]
    [SerializeField] private string shootAnimTrigger = "AssualtShoot";
    [Tooltip("Bool to set while the agent is in firing range and aiming. Leave empty to disable.")]
    [SerializeField] private string aimAnimBool = "IsAiming";

    [Header("Events")]
    public UnityEvent<Vector3> OnFire;
    public event Action OnFireEvent;

    [Header("Audio")]
    [SerializeField] private EventReference fireSound;

    private float cooldownTimer;
    private int burstRemaining;
    private float burstTimer;

    public override bool ClaimsMovement => false;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);
    private void OnEnable() { cooldownTimer = 0f; burstRemaining = 0; }
    private void OnDisable() => SetAiming(false);

    private void Awake()
    {
        if (!animator)
            animator = GetComponentInChildren<Animator>();
    }

    public override string ModuleDescription =>
        "Fires a projectile at a target when within range. Never claims movement — pair with ChaseModule, StrafeModule, or KeepDistanceModule for positioning.\n\n" +
        "• projectilePrefab — the object to spawn (needs a Rigidbody)\n" +
        "• muzzleTransform — where projectiles spawn (assign gun barrel bone)\n" +
        "• minRange / maxRange — only fires within this distance band\n" +
        "• fireCooldown — seconds between shots\n" +
        "• burstCount — shots per trigger pull\n" +
        "• spreadAngle — random spread cone in degrees\n" +
        "• leadTarget — tick to aim ahead of a moving target";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
        {
            SetAiming(false);
            return null;
        }

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance < minRange || distance > maxRange)
        {
            SetAiming(false);
            return null;
        }

        SetAiming(true);

        // Handle burst
        if (burstRemaining > 0)
        {
            burstTimer -= deltaTime;
            if (burstTimer <= 0f)
            {
                FireOne(context.Position);
                burstRemaining--;
                burstTimer = burstInterval;
            }
            return null;
        }

        cooldownTimer -= deltaTime;
        if (cooldownTimer <= 0f)
        {
            burstRemaining = burstCount - 1;
            burstTimer = burstInterval;
            cooldownTimer = fireCooldown;
            FireOne(context.Position);
        }

        return null;
    }

    private void FireOne(Vector3 origin)
    {
        if (!projectilePrefab)
            return;

        Transform muzzle = muzzleTransform ? muzzleTransform : transform;
        Vector3 aimDirection = ComputeAimDirection(muzzle.position);

        if (spreadAngle > 0f)
        {
            Quaternion spread = Quaternion.Euler(
                UnityEngine.Random.Range(-spreadAngle, spreadAngle),
                UnityEngine.Random.Range(-spreadAngle, spreadAngle),
                0f);
            aimDirection = spread * aimDirection;
        }

        GameObject projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(aimDirection));
        Rigidbody rb = projectile.GetComponent<Rigidbody>();
        if (rb)
            rb.linearVelocity = aimDirection * projectileSpeed;

        if (!fireSound.IsNull)
            RuntimeManager.PlayOneShot(fireSound, muzzle.position);

        if (animator && !string.IsNullOrEmpty(shootAnimTrigger))
            animator.SetTrigger(shootAnimTrigger);

        OnFire?.Invoke(muzzle.position);
        OnFireEvent?.Invoke();
    }

    private Vector3 ComputeAimDirection(Vector3 from)
    {
        if (!target)
            return transform.forward;

        Vector3 targetPos = target.position;

        if (leadTarget)
        {
            Rigidbody targetRb = target.GetComponent<Rigidbody>();
            if (targetRb)
            {
                float dist = Vector3.Distance(from, targetPos);
                float travelTime = dist / Mathf.Max(0.1f, projectileSpeed);
                targetPos += targetRb.linearVelocity * travelTime;
            }
        }

        return (targetPos - from).normalized;
    }

    private void TryResolveTarget()
    {
        if (target)
            return;
        Transform candidate = EntityTargetRegistry.Resolve(targetTag, transform.position);
        if (candidate && EntityFaction.IsValidTarget(transform, candidate, requiredRelationship))
            target = candidate;
    }

    private void SetAiming(bool aiming)
    {
        if (animator && !string.IsNullOrEmpty(aimAnimBool))
            animator.SetBool(aimAnimBool, aiming);
    }

    protected override void OnValidate()
    {
        minRange = Mathf.Max(0f, minRange);
        maxRange = Mathf.Max(minRange + 0.1f, maxRange);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        fireCooldown = Mathf.Max(0.05f, fireCooldown);
        burstCount = Mathf.Max(1, burstCount);
        burstInterval = Mathf.Max(0.01f, burstInterval);
        SetMinPriority(ModulePriority.Reactive + 1);
    }
}
