// Stationary rocket launcher that periodically fires a projectile prefab from
// a muzzle transform along its forward axis. Drop this on the Rocket prefab,
// assign a muzzle and a projectile, and it fires wherever it is placed.
//
// Damage / friendly-fire is handled by TurretProjectile (auto-added to the
// projectile if missing) so an EntityFaction on this GameObject is honored
// when assigning the shooter, but it is optional — without it the launcher
// just damages anything it hits.
using UnityEngine;

public class RocketLauncherTurret : MonoBehaviour
{
    [Header("Hierarchy")]
    [Tooltip("Rotating head/tube of the launcher. Its local pitch is set so the barrel tilts up by Pitch Angle. Optional — leave unset for a fixed launcher.")]
    [SerializeField] private Transform rotatingHead;
    [Tooltip("Transform marking the muzzle. Projectiles spawn here and fly along its forward axis. Falls back to rotatingHead, then this transform.")]
    [SerializeField] private Transform muzzle;

    [Header("Aim")]
    [Tooltip("Upward pitch (degrees) applied to rotatingHead. 0 = level, 90 = straight up.")]
    [SerializeField, Range(-30f, 90f)] private float pitchAngle = 45f;
    [Tooltip("Yaw (degrees) applied to rotatingHead around world up. 0 = head's authored facing.")]
    [SerializeField, Range(-180f, 180f)] private float yawAngle = 0f;

    [Header("Firing")]
    [SerializeField] private GameObject projectilePrefab;
    [Tooltip("Initial speed (m/s) of the projectile along the muzzle's forward axis.")]
    [SerializeField] private float projectileSpeed = 35f;
    [Tooltip("Damage dealt by each projectile direct hit on an IDamageable.")]
    [SerializeField] private int damagePerHit = 50;
    [Tooltip("Seconds between shots.")]
    [SerializeField] private float fireInterval = 2f;
    [Tooltip("Seconds before the first shot after enable.")]
    [SerializeField] private float firstShotDelay = 1f;
    [Tooltip("If true, projectile uses gravity (parabolic arc). If false, it flies straight.")]
    [SerializeField] private bool useGravity = true;

    private float cooldownTimer;

    private void OnEnable()
    {
        cooldownTimer = firstShotDelay;
    }

    private void Update()
    {
        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer <= 0f)
        {
            Fire();
            cooldownTimer = fireInterval;
        }
    }

    // LateUpdate so we apply rotation AFTER any Animator on the head runs.
    private void LateUpdate()
    {
        if (rotatingHead == null)
            return;
        Quaternion yaw = Quaternion.AngleAxis(yawAngle, Vector3.up);
        Quaternion pitch = Quaternion.AngleAxis(-pitchAngle, Vector3.right);
        rotatingHead.rotation = transform.rotation * yaw * pitch;
    }

    private void Fire()
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"[RocketLauncherTurret] {name} has no projectilePrefab assigned.");
            return;
        }

        Transform spawn = muzzle != null ? muzzle : (rotatingHead != null ? rotatingHead : transform);
        Vector3 launchDir = spawn.forward;

        GameObject proj = Instantiate(projectilePrefab, spawn.position, Quaternion.LookRotation(launchDir));

        TurretProjectile tp = proj.GetComponent<TurretProjectile>();
        if (tp == null)
            tp = proj.AddComponent<TurretProjectile>();
        tp.Init(damagePerHit, gameObject);

        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb == null)
            rb = proj.AddComponent<Rigidbody>();
        rb.useGravity = useGravity;
        rb.linearVelocity = launchDir * projectileSpeed;
    }

    private void OnDrawGizmosSelected()
    {
        Transform spawn = muzzle != null ? muzzle : (rotatingHead != null ? rotatingHead : transform);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(spawn.position, spawn.position + spawn.forward * 5f);
    }
}
