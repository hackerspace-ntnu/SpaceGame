using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.Weapons
{
    /// <summary>
    /// Abstract base class for all projectile types.
    /// Handles initialization, lifecycle, collision, and damage.
    /// Subclasses define specific movement and visual behavior.
    /// </summary>
    public abstract class Projectile : MonoBehaviour
    {
        [Header("Lifecycle")]
        [SerializeField] protected float lifeTime = 5f;
    
        [Header("Collision")]
        [SerializeField] protected float collisionRadius = 0.1f;
        [SerializeField] protected LayerMask hitMask = ~0;
        [SerializeField] protected bool destroyOnHit = true;
    
        [Header("Damage")]
        [SerializeField] protected int damage = 10;

        [Header("Impact Audio")]
        [Tooltip("Played when the shot lands on something that can bleed.")]
        [SerializeField] protected SfxId fleshImpactId = SfxId.ImpactFlesh;
        [Tooltip("Played when the shot lands on anything else — walls, ground, machinery.")]
        [SerializeField] protected SfxId hardImpactId = SfxId.ImpactMetal;
        [Tooltip("Overrides both of the above.")]
        [SerializeField] protected EventReference impactSound;

        protected Transform ownerRoot;
        protected bool initialized;
        protected float spawnTime;
        protected Vector3 direction = Vector3.forward;

        /// <summary>
        /// True for a projectile that exists only so somebody can watch it.
        ///
        /// A shot is resolved once, on the machine with authority, and shown on all the others — so
        /// every machine has a copy of the same bullet in the air. Exactly one of them may bill the
        /// target for it. Without this flag each peer's copy would call <see cref="NetDamage"/> on
        /// impact, which forwards to the server, and a four-player session would deal the damage
        /// four times.
        /// </summary>
        public bool Cosmetic { get; set; }

        /// <summary>
        /// Initialize the projectile with owner and direction.
        /// Called by the weapon after instantiation.
        /// </summary>
        public virtual void Initialize(Vector3 forwardDirection, Transform owner, Vector3 startPosition)
        {
            direction = forwardDirection.sqrMagnitude > 0.0001f ? forwardDirection.normalized : Vector3.forward;
            ownerRoot = owner ? owner.root : null;
            initialized = true;
            spawnTime = Time.time;

            transform.position = startPosition;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            // Do NOT start lifetime here - it will be started when the projectile actually launches
            // This allows projectiles to be spawned for charging without being destroyed prematurely
        }

        /// <summary>
        /// Backward compatibility variant without position parameter.
        /// </summary>
        public virtual void Initialize(Vector3 forwardDirection, Transform owner)
        {
            Initialize(forwardDirection, owner, transform.position);
        }

        /// <summary>
        /// Update projectile movement and physics each frame.
        /// Called by Update in derived classes.
        /// </summary>
        protected abstract void UpdateMovement();

        /// <summary>
        /// Handle collision with environment or entities.
        /// </summary>
        protected virtual void HandleHit(RaycastHit hit)
        {
            // Through NetDamage, so the hit registers on the server rather than only on the
            // machine that fired. GetComponentInParent is left to NetDamage's own lookup.
            if (!Cosmetic)
                NetDamage.Apply(hit.collider.gameObject, damage, transform);

            OnImpact(hit.point, hit.normal, hit.collider);

            if (destroyOnHit)
            {
                DestroyProjectile();
            }
        }

        /// <summary>
        /// Called when projectile hits something. Override for custom impact effects.
        ///
        /// <para>
        /// The impact sound belongs here rather than beside the NetDamage call above, and
        /// deliberately outside the <c>Cosmetic</c> check: every peer carries its own copy of the
        /// shot, and only one of them is allowed to bill the target — but all of them should hear it
        /// land. Gating this on authority would leave the impact silent for everyone except whoever
        /// happened to be resolving the damage.
        /// </para>
        /// </summary>
        protected virtual void OnImpact(Vector3 position, Vector3 normal, Collider hitCollider)
        {
            SfxId id = HasHealth(hitCollider) ? fleshImpactId : hardImpactId;

            Sfx.Play(id, position, impactSound, GetInstanceID());
        }

        /// <summary>
        /// Whether what was hit is a living thing, which decides between the wet and the hard impact.
        /// <para>
        /// GetComponentInParent, matching how NetDamage resolves its target — colliders on this
        /// project's rigs hang off bones well below the object carrying the HealthComponent, so a
        /// plain GetComponent would call every body shot a wall hit.
        /// </para>
        /// </summary>
        private static bool HasHealth(Collider hitCollider)
        {
            if (hitCollider == null) return false;

            return hitCollider.GetComponentInParent<HealthComponent>() != null;
        }

        /// <summary>
        /// Check if the hit is from the projectile owner (to prevent self-damage).
        /// </summary>
        protected bool IsOwnerHit(Transform hitTransform)
        {
            if (ownerRoot == null || hitTransform == null)
            {
                return false;
            }

            return hitTransform.root == ownerRoot;
        }

        /// <summary>
        /// Destroy the projectile and clean up resources.
        /// </summary>
        protected virtual void DestroyProjectile()
        {
            Destroy(gameObject);
        }

        /// <summary>
        /// Start the lifetime counter for the projectile.
        /// Called when the projectile is launched/released.
        /// </summary>
        public virtual void StartLifetime()
        {
            CancelInvoke(nameof(DestroyProjectile));
            Invoke(nameof(DestroyProjectile), lifeTime);
        }

        /// <summary>
        /// Restart the lifetime counter.
        /// Used for projectiles that delay launching (like charging).
        /// </summary>
        public virtual void RestartLifetime()
        {
            StartLifetime();
        }

        /// <summary>
        /// Get elapsed time since projectile was spawned.
        /// </summary>
        protected float GetElapsedTime()
        {
            return Time.time - spawnTime;
        }
    }
}
