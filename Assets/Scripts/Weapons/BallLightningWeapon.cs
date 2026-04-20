using UnityEngine;
using Unity.Netcode;

/// <summary>
/// BallLightning weapon implementation.
/// Extends abstract Weapon class to spawn BallLightningProjectile with
/// proper ownership and networking support.
/// </summary>
public class BallLightningWeapon : Weapon
{
    [Header("BallLightning Projectile")]
    [SerializeField] private BallLightningProjectile projectilePrefab;
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
            Debug.LogError("BallLightningWeapon: Projectile prefab not assigned!", this);
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();
        Vector3 fireDir = GetFireDirection();

        // Spawn projectile instance
        BallLightningProjectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        // Set owner for damage checks and networking
        Transform ownerRoot = networkOwner != null ? networkOwner.transform : transform.root;
        projectile.Initialize(fireDir, ownerRoot, spawnPos);

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
