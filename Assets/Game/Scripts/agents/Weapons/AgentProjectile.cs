// Lightweight projectile fired by AgentRangedCombatModule.
// Deals damage on first collision with an IDamageable and reports hit/miss back via callback.
// Destroy after lifetime even if nothing is hit.
using System;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(Rigidbody))]
    public class AgentProjectile : MonoBehaviour
    {
        [SerializeField] private float lifetime = 4f;
        [SerializeField] private GameObject impactVfxPrefab;

        private int damage;
        private Action<bool, Vector3> onResult; // (hitDamageable, hitPosition)
        private bool hasHit;
        private EntityFaction shooterFaction;
        private Transform shooterTransform;

        /// <summary>
        /// True for a shot that exists only so somebody can watch it.
        ///
        /// <para>
        /// The same flag <see cref="SpaceGame.Weapons.Projectile.Cosmetic"/> carries, for the same
        /// reason and deliberately under the same name — these are two unrelated classes (this one
        /// is a Rigidbody that reports its own hit back to the module that fired it; that one is a
        /// raycasting base class for player weapons) which share exactly one problem. Whenever more
        /// than one machine puts a copy of the same shot in the air, exactly one of them may bill
        /// the target: <see cref="NetDamage"/> applies a hit on the server and forwards it as a
        /// request from a client, and the server honours every request, so four peers firing the
        /// same bullet deal the damage four times.
        /// </para>
        /// <para>
        /// The spawner sets it, because the spawner is the only thing that knows whose shot this
        /// is. Impact VFX, sound and the result callback are all left running on a cosmetic copy —
        /// the whole point of it being in the air is that everybody sees it land.
        /// </para>
        /// </summary>
        public bool Cosmetic { get; set; }

        public void Init(int damageAmount, Action<bool, Vector3> resultCallback, GameObject shooter = null)
        {
            damage = damageAmount;
            onResult = resultCallback;
            shooterFaction = shooter != null ? shooter.GetComponentInParent<EntityFaction>() : null;
            shooterTransform = shooter != null ? shooter.transform : null;

            if (shooter != null)
            {
                foreach (Collider shooterCol in shooter.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(GetComponent<Collider>(), shooterCol);
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

            hasHit = true;
            Vector3 hitPos = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
            Vector3 hitNormal = collision.contacts.Length > 0 ? collision.contacts[0].normal : -transform.forward;

            if (impactVfxPrefab != null)
                Instantiate(impactVfxPrefab, hitPos, Quaternion.LookRotation(hitNormal));

            IDamageable damageable = collision.gameObject.GetComponentInParent<IDamageable>();
            EntityFaction hitFaction = collision.transform.GetComponentInParent<EntityFaction>();

            // Friendly fire off: pass through explicitly allied damageables only.
            // Unfactioned damageables are valid targets because targeting modules allow them.
            if (damageable != null && shooterFaction != null && hitFaction != null && shooterFaction.IsAlliedWith(hitFaction))
            {
                hasHit = false;
                return;
            }

            if (damageable != null && damageable.Alive)
            {
                // NetDamage picks the authority: applied here on the server or offline, relayed to
                // the server when a client's projectile lands. Without it an AI's shot only ever
                // hurt the copy of the target on the machine that happened to simulate the bullet.
                //
                // Cosmetic is what stops that relay from being sent by every peer at once — see the
                // property. The callback still fires either way: it is what drives the shooter's
                // OnMiss/OnKill events, and a peer that showed the shot should show its outcome.
                if (!Cosmetic)
                    NetDamage.Apply((damageable as Component)?.gameObject, damage, shooterTransform);

                onResult?.Invoke(true, hitPos);
            }
            else
            {
                onResult?.Invoke(false, hitPos);
            }

            Destroy(gameObject);
        }
    }
}
