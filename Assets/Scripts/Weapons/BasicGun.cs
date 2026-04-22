using UnityEngine;
using Unity.Netcode;

/// <summary>
/// BasicGun weapon implementation.
/// A simple weapon that fires a single projectile with each shot.
/// Extends the abstract Weapon class for inventory integration.
/// </summary>
public class BasicGun : Weapon
{
    [Header("Basic Projectile")]
    [SerializeField] private BasicProjectile projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;

    private NetworkObject networkOwner;

    private void OnEnable()
    {
        if (projectileSpawnPoint == null)
        {
            projectileSpawnPoint = transform;
        }

        if (networkOwner == null)
        {
            networkOwner = GetComponentInParent<NetworkObject>();
        }
    }

    protected override void Fire()
    {
        if (projectilePrefab == null)
        {
            Debug.LogError("BasicGun: Projectile prefab not assigned!", this);
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();
        Vector3 fireDir = GetFireDirection();

        // Spawn projectile instance
        BasicProjectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        // Set owner for damage checks and networking
        Transform ownerRoot = networkOwner != null ? networkOwner.transform : transform.root;
        projectile.Initialize(fireDir, ownerRoot, spawnPos);
        
        // Start the lifetime counter now that projectile is launched
        projectile.StartLifetime();

        // Play firing sound via base class
        PlayFireSound();
    }

    private new Vector3 GetSpawnPosition()
    {
        if (projectileSpawnPoint == null)
        {
            projectileSpawnPoint = transform;
        }

        return projectileSpawnPoint.position + projectileSpawnPoint.forward * spawnOffset;
    }
}
