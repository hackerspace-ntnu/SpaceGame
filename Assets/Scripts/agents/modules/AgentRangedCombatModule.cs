// Ranged combat module driven by three ScriptableObject assets:
//   AgentWeaponDefinition  — projectile, damage, audio
//   AgentFireProfile       — range, cooldown, burst cadence
//   AgentAimProfile        — spread, lead prediction, LoS requirement
//
// Owns positioning for the whole engagement, not just the trigger:
//   out of range        — passes, so ChaseModule closes the gap.
//   inside preferred    — backs off to preferredRange, still firing and still facing the target.
//   in the band         — holds station, strafing if the profile allows it.
// Facing is claimed separately (IFacingModule) so the agent keeps its gun on the target while
// side-stepping or retreating rather than turning to look where it is walking.
//
// Handing movement to ChaseModule while a target is in weapon range does not work: Chase's only
// goal is to close the distance, so the agent walks into its target's face while shooting.
// CloseCombatModule at higher priority still preempts if something gets right on top of us.
// OnFire  — fires each shot (position of muzzle)
// OnMiss  — fires when a projectile lands but hits no IDamageable
// OnKill  — fires when a shot kills the target
using System;
using FMODUnity;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public class AgentRangedCombatModule : BehaviourModuleBase, IFacingModule
{
    [Header("Engagement")]
    [Tooltip("Fraction of the fire profile's maxRange the target must exceed before the agent " +
             "stops holding position and lets Chase close again. Without this gap the winner " +
             "alternates every frame at the edge of the fire band and the NavMesh path is " +
             "discarded and re-requested until the agent visibly stutters.")]
    [SerializeField] [Range(1f, 2f)] private float rangeExitFactor = 1.1f;

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
    private int currentBurstSpread;
    private PerceptionModule perception;
    // True while holding position to fire. With rangeExitFactor this is the hysteresis:
    // entering the band costs maxRange, leaving it costs maxRange * rangeExitFactor.
    private bool engaged;
    // What the facing channel points at while engaged, so the agent keeps its gun on the target
    // while backing off or strafing instead of turning to face where it is walking.
    private Transform faceTarget;
    private float strafeTimer;
    private Vector3 strafeDestination;
    private bool hasStrafeDestination;
    // The target this module was firing at when the last shot landed, so OnKillEvent can be
    // attributed even after AgentTargeting has already moved on to someone else.
    private IDamageable firingAt;

    // Read by AgentTargeting at Awake so acquisition range covers the fire band — otherwise an
    // agent can be equipped for a fight it will never notice it could start.
    public float MaxRange => fireProfile != null ? fireProfile.maxRange : 0f;

    private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack);
    private void OnEnable()
    {
        cooldownTimer = 0f;
        burstRemaining = 0;
        engaged = false;
        hasStrafeDestination = false;
        strafeTimer = 0f;
    }
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
        perception = GetComponent<PerceptionModule>();
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

        AgentTargeting targeting = context.Targeting;
        Transform target = targeting != null && targeting.HasTarget ? targeting.Target : null;

        if (target == null || weapon == null || fireProfile == null || aimProfile == null)
        {
            engaged = false;
            faceTarget = null;
            SetAiming(false);
            return null;
        }

        float distance = targeting.DistanceToTarget;
        float bandExit = fireProfile.maxRange * rangeExitFactor;
        if (distance > (engaged ? bandExit : fireProfile.maxRange))
        {
            engaged = false;
            faceTarget = null;
            SetAiming(false);
            return null;
        }

        if (aimProfile.requireLineOfSight && !HasLineOfSight(target))
        {
            // Losing line of sight is a reason to move, not a reason to stand there aiming at a
            // wall — drop the engagement so Chase can reposition.
            engaged = false;
            faceTarget = null;
            SetAiming(false);
            return null;
        }

        engaged = true;
        SetAiming(true);
        faceTarget = target;

        // Where to stand. This module claims movement for the whole engagement rather than
        // deferring to ChaseModule, because Chase's only goal is to close the distance — hand it
        // the frame while a target is in weapon range and the agent walks into its face while
        // shooting. Melee (higher priority) still preempts if something gets right on top of us.
        bool tooClose = distance < fireProfile.preferredRange - fireProfile.rangeTolerance;
        MoveIntent movement = tooClose
            ? BackAwayTo(context.Position, target.position, fireProfile.preferredRange)
            : HoldStation(context.Position, target.position, deltaTime);

        // Fire whenever the band allows it, including on the very first frame the target comes
        // into range — no wind-up, no closing first.
        bool canFireNow = distance <= fireProfile.maxRange
                          && distance >= fireProfile.minRange
                          && (fireProfile.allowFireWhileRunning || movement.Type != AgentIntentType.MoveToPosition);

        if (canFireNow)
        {
            if (burstRemaining > 0)
            {
                if (burstTimer <= 0f)
                {
                    FireOne(target);
                    burstRemaining--;
                    burstTimer = fireProfile.burstInterval;
                }
            }
            else if (cooldownTimer <= 0f)
            {
                // Start a new burst.
                currentBurstSpread = 0;
                FireOne(target);
                burstRemaining = fireProfile.burstCount - 1;
                burstTimer = fireProfile.burstInterval;
                cooldownTimer = fireProfile.fireCooldown;
            }
        }

        return movement;
    }

    // Backs off to the preferred distance. Facing is left to the facing channel so the agent
    // retreats while still pointing its gun at the target rather than turning its back.
    private MoveIntent BackAwayTo(Vector3 self, Vector3 targetPos, float standoff)
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

        Vector3 candidate = targetPos + away.normalized * standoff;

        // Backed into a wall with nowhere to go — hold and keep shooting rather than grinding
        // against geometry.
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            return MoveIntent.StopAndFace(targetPos);

        // Running, not walking: something closed the gap, and backing off at walk speed just
        // means being followed at walk speed.
        return MoveIntent.MoveTo(hit.position, 0.4f, 1f, isRunning: true).WithFacing(targetPos);
    }

    // In the band: sidestep periodically instead of standing still. A stationary shooter is
    // trivially easy to hit and reads as a turret rather than a combatant.
    private MoveIntent HoldStation(Vector3 self, Vector3 targetPos, float deltaTime)
    {
        // Strafing is off for weapons that can't fire on the move — otherwise the agent would
        // sidestep its way through the entire engagement without ever taking a shot.
        if (!fireProfile.strafeWhileEngaged
            || !fireProfile.allowFireWhileRunning
            || fireProfile.strafeDistance <= 0.01f)
        {
            return MoveIntent.StopAndFace(targetPos);
        }

        strafeTimer -= deltaTime;
        if (strafeTimer <= 0f || !hasStrafeDestination)
        {
            strafeTimer = fireProfile.strafeInterval;

            Vector3 toTarget = targetPos - self;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                return MoveIntent.StopAndFace(targetPos);

            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            Vector3 lateral = Vector3.Cross(Vector3.up, toTarget.normalized) * (side * fireProfile.strafeDistance);
            hasStrafeDestination = NavMesh.SamplePosition(self + lateral, out NavMeshHit hit, 2f, NavMesh.AllAreas);
            if (hasStrafeDestination)
                strafeDestination = hit.position;
        }

        if (!hasStrafeDestination)
            return MoveIntent.StopAndFace(targetPos);

        return MoveIntent.MoveTo(strafeDestination, 0.4f, 1f).WithFacing(targetPos);
    }

    // ── IFacingModule ─────────────────────────────────────────────────────────
    // Outranks every ambient look-around so the gun stays on target while the body walks.
    public int FacingPriority => Priority;

    public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
    {
        if (!engaged || faceTarget == null)
        {
            facePosition = default;
            return false;
        }

        facePosition = faceTarget.position;
        return true;
    }

    private void FireOne(Transform target)
    {
        AgentWeaponDefinition activeWeapon = weaponMount != null ? weaponMount.ActiveDefinition : weapon;
        Transform activeMuzzle = weaponMount != null ? weaponMount.ActiveMuzzle : muzzleSocket;

        if (activeWeapon == null || activeWeapon.projectilePrefab == null)
        {
            Debug.LogWarning($"[RangedCombat] {name} fired but projectilePrefab is null on weapon asset!");
            return;
        }

        firingAt = target.GetComponentInChildren<IDamageable>();

        Transform muzzle = activeMuzzle != null ? activeMuzzle : transform;
        Vector3 spawnPos = muzzle.position + muzzle.forward * muzzleForwardOffset;
        Vector3 aimDir = ComputeAimDirection(target, spawnPos, activeWeapon);

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

    // Lead prediction uses the weapon actually being fired, not the serialized fallback — a
    // WeaponMount slot swap changes projectile speed, and aiming with the old number puts every
    // shot behind or ahead of a moving target.
    private Vector3 ComputeAimDirection(Transform target, Vector3 from, AgentWeaponDefinition activeWeapon)
    {
        if (!target)
            return transform.forward;

        Vector3 targetPos = target.position + Vector3.up * 1.2f;

        if (aimProfile.aimLeadFactor > 0f)
        {
            Rigidbody targetRb = target.GetComponentInParent<Rigidbody>();
            if (targetRb != null && activeWeapon.projectileSpeed > 0f)
            {
                float dist = Vector3.Distance(from, targetPos);
                float travelTime = dist / activeWeapon.projectileSpeed;
                targetPos += targetRb.linearVelocity * (travelTime * aimProfile.aimLeadFactor);
            }
        }

        return (targetPos - from).normalized;
    }

    private bool HasLineOfSight(Transform target)
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

        if (firingAt != null && !firingAt.Alive)
            OnKillEvent?.Invoke();
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
