// Ranged combat module driven by three ScriptableObject assets:
//   AgentWeaponDefinition  — projectile, damage, audio
//   AgentFireProfile       — range, cooldown, burst cadence
//   AgentAimProfile        — spread, lead prediction, LoS requirement
//
// Always a side-effect module (ClaimsMovement == false).
// Pair with ChaseModule or KeepDistanceModule for positioning.
// OnFire  — fires each shot (position of muzzle)
// OnMiss  — fires when a projectile lands but hits no IDamageable
// OnKill  — fires when a shot kills the target
using System;
using FMODUnity;
using UnityEngine;
using UnityEngine.Events;

public class AgentRangedCombatModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Weapon")]
    [SerializeField] private AgentWeaponDefinition weapon;
    [SerializeField] private AgentFireProfile fireProfile;
    [SerializeField] private AgentAimProfile aimProfile;

    [Tooltip("World-space transform used as the projectile spawn point. If left empty, uses this transform.")]
    [SerializeField] private Transform muzzleSocket;
    [Tooltip("When true, spawns the weapon model from the weapon asset at runtime. " +
             "Disable if the weapon is already placed in the prefab hierarchy (e.g. parented to a hand bone).")]
    [SerializeField] private bool spawnWeaponModel = false;
    [Tooltip("Optional. When assigned (or found on this object), overrides weapon and muzzleSocket with the active slot.")]
    [SerializeField] private WeaponMount weaponMount;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [Tooltip("Trigger to fire on each shot. Leave empty to disable.")]
    [SerializeField] private string shootAnimTrigger = "AssualtShoot";
    [Tooltip("Bool to set while the agent is in firing range and aiming. Leave empty to disable.")]
    [SerializeField] private string aimAnimBool = "IsAiming";

    [Header("Events")]
    public UnityEvent<Vector3> OnFire;
    public UnityEvent<Vector3> OnMiss;
    public event Action OnFireEvent;
    public event Action OnKillEvent;

    public override bool ClaimsMovement => false;

    private float cooldownTimer;
    private int burstRemaining;
    private float burstTimer;
    private IDamageable targetDamageable;
    private int currentBurstSpread;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);
    private void OnEnable() { cooldownTimer = 0f; burstRemaining = 0; }
    private void OnDisable() => SetAiming(false);

    private void Awake()
    {
        FindChildByName("Gun")?.SetActive(IsActive);
        if (!animator)
            animator = GetComponentInChildren<Animator>();
        if (!weaponMount)
            weaponMount = GetComponentInChildren<WeaponMount>();
    }

    private void Start()
    {
        if (!spawnWeaponModel)
            return;

        if (weapon == null || weapon.weaponModelPrefab == null)
        {
            Debug.Log($"[RangedCombat] {name} no weapon model to spawn (weapon={weapon}, prefab={weapon?.weaponModelPrefab})");
            return;
        }

        // If no muzzle socket assigned, create a child transform at chest height as default.
        if (muzzleSocket == null)
        {
            GameObject socketGo = new GameObject("MuzzleSocket");
            socketGo.transform.SetParent(transform);
            socketGo.transform.localPosition = new Vector3(0.3f, 1.4f, 0.5f);
            socketGo.transform.localRotation = Quaternion.identity;
            muzzleSocket = socketGo.transform;
        }

        Instantiate(weapon.weaponModelPrefab, muzzleSocket.position, muzzleSocket.rotation, muzzleSocket);
    }

    public override string ModuleDescription =>
        "Fires projectiles at a hostile target using three ScriptableObject assets.\n\n" +
        "• AgentWeaponDefinition — projectile prefab, speed, damage, fire sound\n" +
        "• AgentFireProfile — min/max range, cooldown, burst count\n" +
        "• AgentAimProfile — spread angle, burst spread growth, lead factor, LoS check\n" +
        "• muzzleSocket — where projectiles spawn (assign a bone or empty child transform)\n\n" +
        "OnFire(Vector3 muzzlePos) — fires each shot\n" +
        "OnMiss(Vector3 hitPos)    — fires when no IDamageable was hit";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();

        if (!target || weapon == null || fireProfile == null || aimProfile == null)
        {
            SetAiming(false);
            Debug.Log($"[RangedCombat] {name} blocked: target={target}, weapon={weapon}, fire={fireProfile}, aim={aimProfile}");
            return null;
        }

        if (!fireProfile.allowFireWhileRunning && context.IsMoving)
        {
            SetAiming(false);
            return null;
        }

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance < fireProfile.minRange || distance > fireProfile.maxRange)
        {
            SetAiming(false);
            Debug.Log($"[RangedCombat] {name} out of range: dist={distance:F1} min={fireProfile.minRange} max={fireProfile.maxRange}");
            return null;
        }

        if (aimProfile.requireLineOfSight && !HasLineOfSight())
        {
            SetAiming(false);
            Debug.Log($"[RangedCombat] {name} no LoS to target");
            return null;
        }

        SetAiming(true);

        // Continue an active burst.
        if (burstRemaining > 0)
        {
            burstTimer -= deltaTime;
            if (burstTimer <= 0f)
            {
                FireOne();
                burstRemaining--;
                burstTimer = fireProfile.burstInterval;
            }
            return null;
        }

        cooldownTimer -= deltaTime;
        if (cooldownTimer <= 0f)
        {
            // Start a new burst.
            currentBurstSpread = 0;
            FireOne();
            burstRemaining = fireProfile.burstCount - 1;
            burstTimer = fireProfile.burstInterval;
            cooldownTimer = fireProfile.fireCooldown;
        }

        return null;
    }

    private void FireOne()
    {
        AgentWeaponDefinition activeWeapon = weaponMount != null ? weaponMount.ActiveDefinition : weapon;
        Transform activeMuzzle = weaponMount != null ? weaponMount.ActiveMuzzle : muzzleSocket;

        if (activeWeapon == null || activeWeapon.projectilePrefab == null)
        {
            Debug.LogWarning($"[RangedCombat] {name} fired but projectilePrefab is null on weapon asset!");
            return;
        }
        Debug.Log($"[RangedCombat] {name} FIRING at {target.name}");

        Transform muzzle = activeMuzzle != null ? activeMuzzle : transform;
        Vector3 aimDir = ComputeAimDirection(muzzle.position);

        float totalSpread = aimProfile.baseSpreadAngle + aimProfile.spreadGrowthPerBurstShot * currentBurstSpread;
        if (totalSpread > 0f)
        {
            Quaternion spreadRot = Quaternion.Euler(
                UnityEngine.Random.Range(-totalSpread, totalSpread),
                UnityEngine.Random.Range(-totalSpread, totalSpread),
                0f);
            aimDir = spreadRot * aimDir;
        }
        currentBurstSpread++;

        GameObject projectile = Instantiate(activeWeapon.projectilePrefab, muzzle.position, Quaternion.LookRotation(aimDir));

        AgentProjectile agentProjectile = projectile.GetComponent<AgentProjectile>();
        if (agentProjectile != null)
            agentProjectile.Init(activeWeapon.damagePerHit, OnProjectileResult, gameObject);

        Rigidbody rb = projectile.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = aimDir * activeWeapon.projectileSpeed;

        if (!activeWeapon.fireSound.IsNull)
            RuntimeManager.PlayOneShot(activeWeapon.fireSound, muzzle.position);

        if (animator && !string.IsNullOrEmpty(shootAnimTrigger))
            animator.SetTrigger(shootAnimTrigger);

        OnFire?.Invoke(muzzle.position);
        OnFireEvent?.Invoke();
    }

    private Vector3 ComputeAimDirection(Vector3 from)
    {
        if (!target)
            return transform.forward;

        Vector3 targetPos = target.position + Vector3.up * 1.2f;

        if (aimProfile.aimLeadFactor > 0f)
        {
            Rigidbody targetRb = target.GetComponent<Rigidbody>();
            if (targetRb != null && weapon.projectileSpeed > 0f)
            {
                float dist = Vector3.Distance(from, targetPos);
                float travelTime = dist / weapon.projectileSpeed;
                targetPos += targetRb.linearVelocity * (travelTime * aimProfile.aimLeadFactor);
            }
        }

        return (targetPos - from).normalized;
    }

    private bool HasLineOfSight()
    {
        if (!target)
            return false;

        Transform muzzle = muzzleSocket != null ? muzzleSocket : transform;
        Vector3 targetPos = target.position + Vector3.up * 1.2f;
        Vector3 origin = muzzle.position;
        Vector3 dir = targetPos - origin;
        float dist = dir.magnitude;

        RaycastHit[] hits = Physics.RaycastAll(origin, dir.normalized, dist, aimProfile.lineOfSightMask);
        foreach (RaycastHit hit in hits)
        {
            // Ignore own hierarchy.
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            // If we hit the target it's clear.
            if (hit.transform == target || hit.transform.IsChildOf(target))
                return true;
            // Something else is in the way.
            return false;
        }
        return true;
    }

    private void OnProjectileResult(bool hitDamageable, Vector3 hitPos)
    {
        if (!hitDamageable)
        {
            OnMiss?.Invoke(hitPos);
            return;
        }

        if (targetDamageable != null && !targetDamageable.Alive)
            OnKillEvent?.Invoke();
    }

    private void TryResolveTarget()
    {
        if (target)
        {
            if (targetDamageable == null)
                targetDamageable = target.GetComponentInChildren<IDamageable>();
            return;
        }

        Transform candidate = EntityTargetRegistry.Resolve(targetTag, transform.position);
        if (candidate != null && EntityFaction.IsValidTarget(transform, candidate, requiredRelationship))
        {
            target = candidate;
            targetDamageable = candidate.GetComponentInChildren<IDamageable>();
        }
    }

    private void SetAiming(bool aiming)
    {
        if (animator && !string.IsNullOrEmpty(aimAnimBool))
            animator.SetBool(aimAnimBool, aiming);
    }

    private GameObject FindChildByName(string childName)
    {
        Transform result = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == childName) { result = t; break; }
        return result != null ? result.gameObject : null;
    }

    protected override void OnValidate()
    {
        SetMinPriority(ModulePriority.Reactive + 1);
    }
}
