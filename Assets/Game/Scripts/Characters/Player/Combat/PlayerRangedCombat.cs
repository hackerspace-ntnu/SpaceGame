using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.Characters
{
    // Player-side equivalent of AgentRangedCombatModule's FireOne(), reusing the same
    // AgentWeaponDefinition/AgentProjectile pair bots use so damage and friendly-fire
    // behavior (AgentProjectile.IsAlliedWith check) are identical for players and bots
    // in a mixed deathmatch. Server-authoritative: firing is requested via RPC and the
    // projectile is spawned only on the server, matching NetworkedHealthComponent's
    // server-owns-truth pattern elsewhere on this prefab.
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerRangedCombat : NetworkBehaviour
    {
        [SerializeField] private AgentWeaponDefinition weapon;
        [SerializeField] private AimProvider aimProvider;
        [SerializeField] private Transform muzzle;
        [SerializeField] private float fireCooldown = 0.3f;

        private float nextFireTime;

        public void TryFire()
        {
            if (!IsOwner) return;
            if (Time.time < nextFireTime) return;
            if (weapon == null || aimProvider == null || muzzle == null) return;

            nextFireTime = Time.time + fireCooldown;

            Vector3 aimDirection = aimProvider.GetAimRay().direction;
            FireServerRpc(muzzle.position, aimDirection);
        }

        [Rpc(SendTo.Server)]
        private void FireServerRpc(Vector3 spawnPosition, Vector3 aimDirection)
        {
            if (weapon == null || weapon.projectilePrefab == null) return;

            GameObject projectile = Instantiate(weapon.projectilePrefab, spawnPosition, Quaternion.LookRotation(aimDirection));

            AgentProjectile agentProjectile = projectile.GetComponent<AgentProjectile>();
            if (agentProjectile != null)
                agentProjectile.Init(weapon.damagePerHit, null, gameObject);

            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = aimDirection * weapon.projectileSpeed;

            NetworkObject projectileNetObj = projectile.GetComponent<NetworkObject>();
            if (projectileNetObj != null)
                projectileNetObj.Spawn();
        }
    }
}
