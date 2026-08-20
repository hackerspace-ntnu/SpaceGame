using UnityEngine;
using Unity.Netcode;

namespace SpaceGame.Weapons
{
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

        // override, and base first. Declared as a plain private OnEnable this HID Weapon.OnEnable
        // rather than extending it, so Unity called only this one: the magazine was never resolved
        // or refilled, which left CanUse() returning false and the gun unable to fire a single shot.
        protected override void OnEnable()
        {
            base.OnEnable();

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

            // Only the authority's bullet may hurt anybody — every other machine is running this
            // same method to show the shot, and all of them would otherwise apply the damage.
            projectile.Cosmetic = !ShotDealsDamage;

            // Set owner for damage checks and networking
            Transform ownerRoot = networkOwner != null ? networkOwner.transform : transform.root;
            projectile.Initialize(fireDir, ownerRoot, spawnPos);

            // Start the lifetime counter now that projectile is launched
            projectile.StartLifetime();

            // The report belongs to Present(), which runs on every machine — playing it here would
            // sound it only where the shot was resolved, and twice on the machine that did both.
        }

        // override, not `new`: hiding the base method meant every base-class caller — GetAimPoint
        // and GetFireDirection among them — silently kept using the base version, so the barrel this
        // gun declares was only honoured on the one call site that knew to look for it.
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
