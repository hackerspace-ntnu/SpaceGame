// Mortar-style projectile fired by TurretModule.
// Damages the first IDamageable it touches, then despawns. Gravity is applied by
// the Rigidbody (set by TurretModule.Fire), so the trajectory is a parabolic arc.
//
// Friendly-fire is filtered the same way AgentProjectile does it: if the shooter
// has an EntityFaction allied with the hit target's faction, the hit is ignored.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(Rigidbody))]
    public class TurretProjectile : MonoBehaviour
    {
        [SerializeField] private float lifetime = 8f;
        [SerializeField] private GameObject impactVfxPrefab;
        [Tooltip("Optional radius for splash damage. 0 = direct hit only.")]
        [SerializeField] private float splashRadius = 0f;
        [Tooltip("Layers the splash overlap considers. Default = everything.")]
        [SerializeField] private LayerMask splashMask = ~0;

        private int damage;
        private bool hasHit;
        private EntityFaction shooterFaction;
        private Transform shooterTransform;

        /// <summary>
        /// True for a shell that exists only so somebody can watch the arc.
        ///
        /// <para>
        /// A turret fires on every machine — that is what keeps the mortar visible to a player who
        /// is not hosting, and it costs nothing on the wire — but only the machine that simulates
        /// the turret may bill anybody for the landing. <see cref="NetDamage"/> forwards a client's
        /// hit to the server as a request and the server honours all of them, so without this a
        /// four-player session takes four shells of damage from one shell.
        /// </para>
        /// <para>
        /// Set by whoever fires; see <see cref="SpaceGame.Weapons.Projectile.Cosmetic"/>, which is
        /// the same flag for the player's weapons. Splash included: a cosmetic shell splashes
        /// nobody, not even the neighbours the authority's copy will decide about for itself.
        /// </para>
        /// </summary>
        public bool Cosmetic { get; set; }

        public void Init(int damageAmount, GameObject shooter)
        {
            damage = damageAmount;
            shooterFaction = shooter != null ? shooter.GetComponentInParent<EntityFaction>() : null;
            shooterTransform = shooter != null ? shooter.transform : null;

            if (shooter != null)
            {
                Collider self = GetComponent<Collider>();
                if (self != null)
                {
                    foreach (Collider shooterCol in shooter.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(self, shooterCol);
                }
            }
        }

        private void Start()
        {
            Destroy(gameObject, lifetime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (hasHit)
                return;

            Vector3 hitPos = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
            Vector3 hitNormal = collision.contacts.Length > 0 ? collision.contacts[0].normal : -transform.forward;

            EntityFaction hitFaction = collision.transform.GetComponentInParent<EntityFaction>();
            if (shooterFaction != null && hitFaction != null && shooterFaction.IsAlliedWith(hitFaction))
                return;

            hasHit = true;

            // Outside the Cosmetic check, deliberately: every machine that put this shell in the
            // air should show it landing. Only the damage below is the authority's alone.
            if (impactVfxPrefab != null)
                Instantiate(impactVfxPrefab, hitPos, Quaternion.LookRotation(hitNormal));

            if (!Cosmetic)
            {
                if (splashRadius > 0f)
                    ApplySplash(hitPos);
                else
                    ApplyDirect(collision.transform);
            }

            Destroy(gameObject);
        }

        private void ApplyDirect(Transform hit)
        {
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.Alive)
                return;

            NetDamage.Apply((damageable as Component)?.gameObject, damage, shooterTransform);
        }

        private void ApplySplash(Vector3 center)
        {
            Collider[] hits = Physics.OverlapSphere(center, splashRadius, splashMask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                EntityFaction f = c.GetComponentInParent<EntityFaction>();
                if (shooterFaction != null && f != null && shooterFaction.IsAlliedWith(f))
                    continue;

                IDamageable damageable = c.GetComponentInParent<IDamageable>();
                if (damageable == null || !damageable.Alive)
                    continue;

                NetDamage.Apply((damageable as Component)?.gameObject, damage, shooterTransform);
            }
        }
    }
}
