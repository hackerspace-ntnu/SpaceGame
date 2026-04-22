// Ranged combat module driven by three ScriptableObject assets:
//   AgentWeaponDefinition  — projectile, damage, audio
//   AgentFireProfile       — range, cooldown, burst cadence
//   AgentAimProfile        — spread, lead prediction, LoS requirement
//
// Claims movement: returns StopAndFace while in band (preempting ChaseModule) and null otherwise,
// so ChaseModule at lower priority drives the approach when the target is out of band.
// Stands and fires; never fires while running.
// OnFire  — fires each shot (position of muzzle)
// OnMiss  — fires when a projectile lands but hits no IDamageable
// OnKill  — fires when a shot kills the target
using System;
using FMODUnity;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public class AgentRangedCombatModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Weapon")]
    [SerializeField] private AgentWeaponDefinition weapon;
    [SerializeField] private AgentFireProfile fireProfile;
    [SerializeField] private AgentAimProfile aimProfile;

    [Tooltip("World-space transform used as the projectile spawn point. " +
             "If left empty, falls back to the child named 'Gun', then to this transform.")]
    [SerializeField] private Transform muzzleSocket;
    [Tooltip("Meters in front of the muzzle (along muzzle.forward) where the projectile actually spawns. " +
             "Keeps the projectile clear of the gun model and the agent's armature on the first frame.")]
    [SerializeField] private float muzzleForwardOffset = 0.4f;
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

    private float cooldownTimer;
    private int burstRemaining;
    private float burstTimer;
    private IDamageable targetDamageable;
    private int currentBurstSpread;
    // Cached at Awake: if there's no melee fallback, this module retreats to minRange when the
    // target gets too close rather than sitting still while Chase pushes the agent closer.
    private bool hasMeleeFallback;
    private PerceptionModule perception;
    private EntityFaction selfFaction;

    // Read by ChaseModule at Awake to ensure its loseTargetRange is at least this wide,
    // so the agent doesn't drop aggro the moment the target steps out of fire range.
    public float MaxRange => fireProfile != null ? fireProfile.maxRange : 0f;

    private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack);
    private void OnEnable() { cooldownTimer = 0f; burstRemaining = 0; }
    private void OnDisable() => SetAiming(false);

    private void Awake()
    {
        GameObject gun = FindChildByName("Gun");
        gun?.SetActive(IsActive);
        if (!muzzleSocket && gun != null)
            muzzleSocket = gun.transform;
        if (!animator)
            animator = GetComponentInChildren<Animator>();
        if (!weaponMount)
            weaponMount = GetComponentInChildren<WeaponMount>();
        hasMeleeFallback = GetComponent<CloseCombatModule>() != null;
        perception = GetComponent<PerceptionModule>();
        selfFaction = GetComponent<EntityFaction>();
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
        // Advance timers every frame so cooldown keeps ticking while the agent is out of range.
        cooldownTimer -= deltaTime;
        if (burstRemaining > 0)
            burstTimer -= deltaTime;

        TryResolveTarget();

        if (!target || weapon == null || fireProfile == null || aimProfile == null)
        {
            SetAiming(false);
            return null;
        }

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance > fireProfile.maxRange)
        {
            SetAiming(false);
            return null;
        }

        if (distance < fireProfile.minRange)
        {
            SetAiming(false);
            if (hasMeleeFallback)
                return null;
            return ComputeRetreatIntent(context.Position, target.position, fireProfile.minRange);
        }

        if (aimProfile.requireLineOfSight && !HasLineOfSight())
        {
            SetAiming(false);
            return null;
        }

        SetAiming(true);

        if (burstRemaining > 0)
        {
            if (burstTimer <= 0f)
            {
                FireOne();
                burstRemaining--;
                burstTimer = fireProfile.burstInterval;
            }
        }
        else if (cooldownTimer <= 0f)
        {
            // Start a new burst.
            currentBurstSpread = 0;
            FireOne();
            burstRemaining = fireProfile.burstCount - 1;
            burstTimer = fireProfile.burstInterval;
            cooldownTimer = fireProfile.fireCooldown;
        }

        return MoveIntent.StopAndFace(target.position);
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
        Vector3 spawnPos = muzzle.position + muzzle.forward * muzzleForwardOffset;
        Vector3 aimDir = ComputeAimDirection(spawnPos);

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

        GameObject projectile = Instantiate(activeWeapon.projectilePrefab, spawnPos, Quaternion.LookRotation(aimDir));

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

        // Route through PerceptionModule — single source of truth for occlusion layers and self-hit rules.
        // The raycast originates at the muzzle so we're checking "can the bullet reach the target",
        // not "can the eye see the target".
        if (perception == null)
        {
            Debug.LogWarning($"[RangedCombat] {name} requires a PerceptionModule on the same GameObject for line-of-sight checks. Disable aimProfile.requireLineOfSight or add a PerceptionModule.");
            return true;
        }

        Transform muzzle = muzzleSocket != null ? muzzleSocket : transform;
        return perception.HasLineOfSightFrom(muzzle.position, target);
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

    private MoveIntent ComputeRetreatIntent(Vector3 self, Vector3 targetPos, float minRange)
    {
        Vector3 away = self - targetPos;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = UnityEngine.Random.insideUnitSphere;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = Vector3.forward;
        }

        float retreatDistance = minRange + 0.5f;
        Vector3 candidate = targetPos + away.normalized * retreatDistance;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            return MoveIntent.MoveTo(hit.position, 0.2f, 1f);

        return MoveIntent.StopAndFace(targetPos);
    }

    private void TryResolveTarget()
    {
        if (target)
        {
            if (targetDamageable == null)
                targetDamageable = target.GetComponentInChildren<IDamageable>();

            // Drop dead targets so a new live one can be resolved. The dying robot stays active
            // for its despawn delay, so the Transform reference survives — we have to gate on Alive.
            if (targetDamageable != null && !targetDamageable.Alive)
            {
                target = null;
                targetDamageable = null;
            }
            else
            {
                return;
            }
        }

        Transform candidate = EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship, transform.position);
        if (candidate == null)
            return;

        IDamageable candidateDamageable = candidate.GetComponentInChildren<IDamageable>();
        if (candidateDamageable != null && !candidateDamageable.Alive)
            return;

        target = candidate;
        targetDamageable = candidateDamageable;
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
        SetMinPriority(ModulePriority.RangedAttack);
    }
}
