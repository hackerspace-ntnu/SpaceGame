using UnityEngine;
using Unity.Netcode;

namespace SpaceGame.Weapons
{
    /// <summary>
    /// BallLightning weapon implementation.
    /// Extends abstract Weapon class to spawn BallLightningProjectile with
    /// proper ownership and networking support.
    /// Supports charging mechanics for gradual power-up.
    /// </summary>
    public class BallLightningWeapon : Weapon
    {
        [Header("BallLightning Projectile")]
        [SerializeField] private BallLightningProjectile projectilePrefab;
        [SerializeField] private Transform projectileSpawnPoint;

        private NetworkObject networkOwner;
        private BallLightningProjectile currentProjectile; // Reference to the currently charging projectile

        protected override void OnEnable()
        {
            // Call parent OnEnable first
            base.OnEnable();

            if (projectileSpawnPoint == null)
            {
                projectileSpawnPoint = firePoint != null ? firePoint : transform;
            }

            if (networkOwner == null)
            {
                networkOwner = GetComponentInParent<NetworkObject>();
            }
        }

        /// <summary>
        /// Spawn a projectile for charging (called on first press when charging enabled).
        /// </summary>
        protected override void SpawnChargeProjectile()
        {
            if (projectilePrefab == null)
            {
                Debug.LogError("BallLightningWeapon: Projectile prefab not assigned!", this);
                return;
            }

            Vector3 spawnPos = GetSpawnPosition();
            Vector3 initialDir = GetFireDirection();

            // Spawn projectile instance
            BallLightningProjectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

            if (projectile == null)
            {
                Debug.LogError("BallLightningWeapon: Failed to instantiate projectile!", this);
                return;
            }

            // Only the authority's orb may hurt anybody — every other machine runs this to show it.
            projectile.Cosmetic = !ShotDealsDamage;

            // Set owner for damage checks and networking
            Transform ownerRoot = networkOwner != null ? networkOwner.transform : transform.root;
            projectile.Initialize(initialDir, ownerRoot, spawnPos);

            // Pass the barrel transform so projectile follows it during charging
            Transform barrel = projectileSpawnPoint != null ? projectileSpawnPoint : firePoint;
            projectile.SetBarrelTransform(barrel);
        
            currentProjectile = projectile;
            chargedProjectile = projectile as IChargeable; // Set the reference for the weapon
        }

        /// <summary>
        /// Fire/launch the charged projectile (called on second press when charging enabled).
        /// </summary>
        protected override void Fire()
        {
            if (currentProjectile == null)
            {
                Debug.LogError("BallLightningWeapon: No charged projectile to launch!", this);
                return;
            }

            // Get current fire direction (where player is aiming NOW, not where they were when charging started)
            Vector3 fireDir = GetFireDirection();
        
            // Update the projectile's direction
            currentProjectile.SetLaunchDirection(fireDir);
        
            // Tell the projectile to actually launch (enables movement)
            currentProjectile.LaunchCharged();

            // The report belongs to Present(), which runs on every machine — see Weapon.
        }

        // override, not `new` — see the same note on BasicGun.
        protected override Vector3 GetSpawnPosition()
        {
            if (projectileSpawnPoint == null)
            {
                projectileSpawnPoint = transform;
            }

            return projectileSpawnPoint.position + projectileSpawnPoint.forward * spawnOffset;
        }
    }
}
