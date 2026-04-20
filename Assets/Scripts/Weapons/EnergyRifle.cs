using UnityEngine;

/// <summary>
/// Energy Rifle weapon implementation.
/// Demonstrates hitscan weapon type extending abstract Weapon base.
/// Shoots instant rays with damage application and visual feedback.
/// </summary>
public class EnergyRifle : Weapon
{
    [Header("Hitscan Settings")]
    [SerializeField] private float rayDistance = 500f;
    [SerializeField] private float shotSpread = 0f; // 0 = perfectly accurate
    [SerializeField] private int raysPerShot = 1;
    [SerializeField] private float spreadAngle = 2f; // Degrees of spread per ray

    [Header("Visual Feedback")]
    [SerializeField] private LineRenderer shotVisualizerPrefab;
    [SerializeField] private ParticleSystem muzzleFlashPrefab;
    [SerializeField] private ParticleSystem impactEffectPrefab;
    [SerializeField] private Color shotColor = new Color(0.0f, 1.0f, 0.8f);
    [SerializeField] private float shotVisualizerDuration = 0.1f;

    [Header("Impact Settings")]
    [SerializeField] private float damageDropoff = 0f; // Damage reduction per unit distance (0 = no dropoff)

    private Transform fireOrigin;

    private void Start()
    {
        fireOrigin = GetFireOrigin();
    }

    protected override void Fire()
    {
        Transform origin = GetFireOrigin();
        Vector3 baseDirection = GetFireDirection();

        for (int i = 0; i < raysPerShot; i++)
        {
            Vector3 spreadDirection = baseDirection;

            // Apply spread if configured
            if (raysPerShot > 1 || shotSpread > 0f)
            {
                float spreadAmount = (raysPerShot > 1) ? ((i / (float)(raysPerShot - 1)) - 0.5f) * spreadAngle : 0f;
                spreadAmount += Random.Range(-shotSpread, shotSpread);

                Quaternion spreadRotation = Quaternion.AngleAxis(spreadAmount, origin.right) * 
                                           Quaternion.AngleAxis(Random.Range(-spreadAngle * 0.5f, spreadAngle * 0.5f), origin.up);
                spreadDirection = spreadRotation * baseDirection;
            }

            FireRay(origin.position, spreadDirection);
        }

        // Play muzzle flash
        if (muzzleFlashPrefab != null)
        {
            Instantiate(muzzleFlashPrefab, origin.position, origin.rotation);
        }

        // Play fire sound
        PlayFireSound();
    }

    private void FireRay(Vector3 startPos, Vector3 direction)
    {
        Ray ray = new Ray(startPos, direction);

        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            // Apply damage to hit target
            HealthComponent targetHealth = hit.collider.GetComponent<HealthComponent>();
            if (targetHealth != null)
            {
                float distanceFactor = Mathf.Max(0f, 1f - (hit.distance * damageDropoff / 100f));
                int actualDamage = Mathf.RoundToInt(25f * distanceFactor); // Base energy rifle damage
                targetHealth.Damage(actualDamage);
            }

            // Spawn impact effect
            if (impactEffectPrefab != null)
            {
                Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            }

            // Draw ray visualizer
            VisualizeShot(startPos, hit.point);
        }
        else
        {
            // Draw ray to max distance
            VisualizeShot(startPos, startPos + direction * rayDistance);
        }
    }

    private void VisualizeShot(Vector3 start, Vector3 end)
    {
        if (shotVisualizerPrefab == null)
        {
            return;
        }

        LineRenderer visualizer = Instantiate(shotVisualizerPrefab);
        visualizer.SetPosition(0, start);
        visualizer.SetPosition(1, end);
        visualizer.startColor = shotColor;
        visualizer.endColor = shotColor;

        Destroy(visualizer.gameObject, shotVisualizerDuration);
    }
}
