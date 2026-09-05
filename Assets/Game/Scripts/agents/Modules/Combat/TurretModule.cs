// Stationary turret that rotates a child transform toward the nearest hostile entity
// and fires mortar-like projectiles from a barrel transform.
//
// Target acquisition uses the same EntityFaction + EntityTargetRegistry pipeline as
// AgentRangedCombatModule — drop an EntityFaction on the same GameObject and the
// turret picks the nearest entity whose relationship to it is Hostile.
//
// The "rotating part" child is yawed to face the target on the horizontal plane and
// pitched upward by `barrelTiltAngle` (default 20°) so the barrel arcs the shot.
// Projectiles are spawned at the barrel transform with velocity along the barrel's
// forward axis; gravity does the rest, giving a mortar-style trajectory.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(EntityFaction))]
    public class TurretModule : MonoBehaviour
    {
        [Header("Hierarchy")]
        [Tooltip("Child transform that rotates to face the target (yaw + tilt). Required.")]
        [SerializeField] private Transform rotatingPart;
        [Tooltip("Transform marking the gun barrel / muzzle. Projectiles spawn here and fly along its forward axis.")]
        [SerializeField] private Transform gunBarrel;

        [Header("Targeting")]
        [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;
        [Tooltip("Minimum engagement range in meters. Targets closer than this are ignored.")]
        [SerializeField, Min(0f)] private float minRange = 10f;
        [Tooltip("Maximum engagement range in meters.")]
        [SerializeField, Min(0f)] private float maxRange = 60f;
        [Tooltip("How often (seconds) the turret refreshes its target lookup.")]
        [SerializeField] private float retargetInterval = 0.5f;

        [Header("Aim")]
        [Tooltip("Pitch used when the target is out of ballistic range (target unreachable at projectileSpeed).")]
        [SerializeField, Range(-30f, 80f)] private float fallbackTiltAngle = 45f;
        [Tooltip("Degrees per second the rotating part is allowed to rotate. 0 = snap.")]
        [SerializeField] private float rotationSpeed = 180f;
        [Tooltip("If true, use the high-arc (mortar) ballistic solution. If false, use the low-arc (direct) solution.")]
        [SerializeField] private bool useHighArc = false;

        [Header("Firing")]
        [SerializeField] private GameObject projectilePrefab;
        [Tooltip("Initial speed (m/s) of the projectile along the barrel's forward axis.")]
        [SerializeField] private float projectileSpeed = 35f;
        [Tooltip("Metres a shot from this gun is heard over by NoiseReceiverModules. "
                 + "0 = silent to AI.")]
        [SerializeField] private float gunshotNoiseRadius = 55f;
        [Tooltip("Damage per direct hit on an IDamageable.")]
        [SerializeField] private int damagePerHit = 25;
        [Tooltip("Seconds between shots.")]
        [SerializeField] private float fireCooldown = 2f;
        [Tooltip("Vertical aim offset added to the target position (meters), for aiming center-of-mass.")]
        [SerializeField] private float targetHeightOffset = 1.0f;

        private EntityFaction selfFaction;
        private Transform target;
        private float retargetTimer;
        private float cooldownTimer;

        // Cached rather than asked per shot: a turret fires on a cooldown but aims every frame, and
        // the lookup this replaces walks the hierarchy for a NetworkObject. See AgentAuthority.
        private AgentAuthority authority;

        public float MinRange => minRange;
        public float MaxRange => maxRange;
        public Transform CurrentTarget => target;

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // A placeable turret is world furniture: it is still there next session and it was still
        // shooting at somebody. Three things go with it.
        //
        // The COOLDOWN, for the same reason as every other weapon here — a turret that reloads at
        // zero fires the instant the world hydrates, which is a free mortar shell for whoever
        // reloaded and a free one against them if it is hostile.
        //
        // The TARGET, so the turret does not spend up to `retargetInterval` staring at nothing and
        // then re-acquire from scratch.
        //
        // The BARREL, which is the one nothing else covers: the aim lives on `rotatingPart`, a
        // CHILD transform, and TransformSaveable is only ever on the root. Without this the gun
        // snaps back to its authored heading and slews round again from there. Captured as a LOCAL
        // rotation so it survives the turret itself having been placed at a different angle.
        //
        // No latch is needed here, unlike the combat modules: this component has no OnEnable, so
        // nothing resets any of it behind a restore.
        public float RetargetTimer => retargetTimer;
        public float CooldownTimer => cooldownTimer;
        public Transform RotatingPart => rotatingPart;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreTurretState(float retarget, float cooldown)
        {
            retargetTimer = retarget;
            cooldownTimer = cooldown;
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreTarget(Transform restored) => target = restored;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreBarrelRotation(Quaternion localRotation)
        {
            if (rotatingPart != null) rotatingPart.localRotation = localRotation;
        }

        private void Reset()
        {
            // Pick a sensible default: a child named "Rotator" or "Turret" if present.
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                string n = t.name.ToLowerInvariant();
                if (rotatingPart == null && (n.Contains("rotat") || n.Contains("turret") || n.Contains("head")))
                    rotatingPart = t;
                if (gunBarrel == null && (n.Contains("barrel") || n.Contains("muzzle") || n.Contains("gun")))
                    gunBarrel = t;
            }
        }

        private void Awake()
        {
            authority = new AgentAuthority(this);
            selfFaction = GetComponent<EntityFaction>();
            if (rotatingPart == null)
                Debug.LogWarning($"[Turret] {name} has no rotatingPart assigned — turret will not rotate.");
            if (gunBarrel == null)
                Debug.LogWarning($"[Turret] {name} has no gunBarrel assigned — falling back to rotatingPart/transform for spawn point.");
        }

        // A turret bolted to a vehicle changes which NetworkObject is above it when the vehicle
        // does. See AgentAuthority.Invalidate.
        private void OnTransformParentChanged() => authority?.Invalidate();

        private void Update()
        {
            float dt = Time.deltaTime;
            cooldownTimer -= dt;
            retargetTimer -= dt;

            if (retargetTimer <= 0f)
            {
                retargetTimer = retargetInterval;
                AcquireTarget();
            }

            if (target == null)
                return;

            float distance = Vector3.Distance(transform.position, target.position);
            if (distance > maxRange || distance < minRange)
            {
                target = null;
                return;
            }

            if (cooldownTimer <= 0f && IsAimedAtTarget())
            {
                Fire();
                cooldownTimer = fireCooldown;
            }
        }

        // LateUpdate so we apply rotation AFTER any Animator on the rotating part runs
        // and overwrites local rotation. Without this, an Animator on a child of the
        // rotating part can leave our Update-time rotation visually stuck.
        private void LateUpdate()
        {
            AimAtTarget(Time.deltaTime);
        }

        private void AcquireTarget()
        {
            // Drop dead/out-of-range targets so the registry can hand us a fresh one. Viability
            // covers retired entities too — an eliminated player object stays active forever with
            // only its EntityFaction disabled, and a turret tracking one would never fire again.
            if (target != null)
            {
                float d = Vector3.Distance(transform.position, target.position);
                if (!TargetResolution.IsViable(target) || d > maxRange || d < minRange)
                    target = null;
            }
            if (target != null)
                return;

            Transform candidate = EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship, transform.position);
            if (candidate == null)
                return;

            float dist = Vector3.Distance(transform.position, candidate.position);
            if (dist > maxRange || dist < minRange)
                return;

            IDamageable dmg = candidate.GetComponentInChildren<IDamageable>();
            if (dmg != null && !dmg.Alive)
                return;

            target = candidate;
        }

        private void AimAtTarget(float dt)
        {
            if (rotatingPart == null || target == null)
                return;

            Vector3 launchPos = gunBarrel != null ? gunBarrel.position : rotatingPart.position;
            Vector3 aimPoint = target.position + Vector3.up * targetHeightOffset;
            Vector3 toTarget = aimPoint - launchPos;
            Vector3 horizontal = new Vector3(toTarget.x, 0f, toTarget.z);
            float horizontalDist = horizontal.magnitude;
            if (horizontalDist < 0.0001f)
                return;

            float pitchDeg = SolveBallisticPitch(horizontalDist, toTarget.y, projectileSpeed, useHighArc);

            Quaternion yaw = Quaternion.LookRotation(horizontal / horizontalDist, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(-pitchDeg, Vector3.right);
            Quaternion desired = yaw * pitch;

            if (rotationSpeed <= 0f)
                rotatingPart.rotation = desired;
            else
                rotatingPart.rotation = Quaternion.RotateTowards(rotatingPart.rotation, desired, rotationSpeed * dt);
        }

        // Returns barrel pitch (degrees, positive = up) needed to hit a point at horizontal
        // distance `d` and vertical delta `y` (target - launcher) given projectile speed `v`
        // under Physics.gravity. Falls back to `fallbackTiltAngle` if unreachable.
        //
        // Solves: y = x*tan(θ) - g*x²/(2*v²*cos²(θ))
        // Two solutions exist when reachable; low arc is the more direct shot.
        private float SolveBallisticPitch(float d, float y, float v, bool highArc)
        {
            float g = -Physics.gravity.y; // positive magnitude
            if (g <= 0f || v <= 0f || d <= 0f)
                return fallbackTiltAngle;

            float v2 = v * v;
            float discriminant = v2 * v2 - g * (g * d * d + 2f * y * v2);
            if (discriminant < 0f)
                return fallbackTiltAngle; // out of range at this speed

            float sqrt = Mathf.Sqrt(discriminant);
            float numerator = highArc ? (v2 + sqrt) : (v2 - sqrt);
            float angleRad = Mathf.Atan2(numerator, g * d);
            return angleRad * Mathf.Rad2Deg;
        }

        private bool IsAimedAtTarget()
        {
            if (rotatingPart == null || target == null)
                return false;
            Vector3 horizontalAim = rotatingPart.forward;
            horizontalAim.y = 0f;
            Vector3 horizontalToTarget = target.position - rotatingPart.position;
            horizontalToTarget.y = 0f;
            if (horizontalAim.sqrMagnitude < 0.0001f || horizontalToTarget.sqrMagnitude < 0.0001f)
                return false;
            float angle = Vector3.Angle(horizontalAim, horizontalToTarget);
            return angle < 5f;
        }

        private void Fire()
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[Turret] {name} has no projectilePrefab assigned.");
                return;
            }

            Transform spawn = gunBarrel != null ? gunBarrel : (rotatingPart != null ? rotatingPart : transform);
            Vector3 launchDir = spawn.forward;

            GameObject proj = Instantiate(projectilePrefab, spawn.position, Quaternion.LookRotation(launchDir));

            // Damage component (TurretProjectile) — added if missing so any prefab works.
            TurretProjectile tp = proj.GetComponent<TurretProjectile>();
            if (tp == null)
                tp = proj.AddComponent<TurretProjectile>();

            // Every machine fires; only the one that simulates this turret bills anybody. That
            // split is chosen over gating the whole turret because a mortar shell is a slow, very
            // visible arc — a client that never drew one would watch its allies take damage out of
            // an empty sky — and because a shell shown this way costs nothing on the wire. The
            // price is that peers aim from their own copy of the target list and can send the arc
            // somewhere slightly different; the damage is unaffected, since theirs cannot deal any.
            tp.Cosmetic = !authority.SimulatedHere;
            tp.Init(damagePerHit, gameObject);

            Rigidbody rb = proj.GetComponent<Rigidbody>();
            if (rb == null)
                rb = proj.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.linearVelocity = launchDir * projectileSpeed;

            // Same split as the damage above: every machine draws the shell, only the one
            // that simulates this turret reports the bang to the AI.
            if (authority.SimulatedHere && gunshotNoiseRadius > 0f)
                Noise.Emit(NoiseType.Gunshot, spawn.position, gunshotNoiseRadius,
                           transform, transform);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, maxRange);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, minRange);
            if (gunBarrel != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(gunBarrel.position, gunBarrel.position + gunBarrel.forward * 3f);
            }
        }
    }
}
