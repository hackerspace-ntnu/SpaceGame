using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay;
using SpaceGame.Portals;

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

        /// <summary>How far past an exit aperture a shot is placed, so its next trace starts clear of the wall behind it.</summary>
        private const float PortalExitClearance = 0.05f;

        /// <summary>
        /// Carry this shot through any aperture this frame's move crosses,
        /// rewriting the move to the far side and turning <see cref="direction"/>
        /// with it.
        ///
        /// Projectiles need their own path through a portal. They are not pushed
        /// around by physics — they rewrite their own transform and resolve hits
        /// with a cast — so they raise no trigger callback for the portal's
        /// traversal volume to catch, and at fifty metres a second they would
        /// step over a once-a-frame sample of it even if they did. A segment
        /// test against the plane has neither problem.
        ///
        /// Callers must run this BEFORE their collision cast and trace from the
        /// segment it returns. Tracing the original one instead resolves the
        /// shot against the room it was leaving, which means every portalled
        /// bullet detonates on the wall the aperture is cut into.
        ///
        /// One crossing per frame on purpose: two apertures facing each other
        /// would otherwise let a single shot bounce between them without bound
        /// inside one call.
        /// </summary>
        protected bool CrossPortal(ref Vector3 start, ref Vector3 end)
        {
            Portal portal = Portal.Crossing(start, end, out Vector3 entry, out Matrix4x4 transfer);
            if (portal == null || portal.Linked == null) return false;

            Vector3 remainder = end - entry;
            Vector3 outward = portal.Linked.transform.forward;

            start = transfer.MultiplyPoint3x4(entry) + outward * PortalExitClearance;
            end = start + transfer.MultiplyVector(remainder);

            direction = transfer.MultiplyVector(direction).normalized;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            return true;
        }

        /// <summary>
        /// Handle collision with environment or entities.
        /// </summary>
        protected virtual void HandleHit(RaycastHit hit)
        {
            // Through NetDamage, so the hit registers on the server rather than only on the
            // machine that fired. GetComponentInParent is left to NetDamage's own lookup.
            //
            // The damage source is the SHOOTER, not this projectile. It used to be `transform`,
            // and that is worse than merely imprecise: HealthComponent stores it as
            // LastDamageSource, and everything that asks "who hit me" resolves an EntityFaction
            // from it — AgentTargeting's last-attacker bias, and the provocation that wakes a
            // peaceful creature. A loose projectile has no EntityFaction above it and is destroyed
            // on this very frame, so both of those resolved to a dead object and silently gave up.
            // Shooting a passive creature with a player firearm simply never made it angry.
            //
            // ownerRoot is already tracked for the self-hit test; falling back to `transform` keeps
            // a projectile spawned without an owner behaving exactly as before.
            if (!Cosmetic)
                NetDamage.Apply(hit.collider.gameObject, damage,
                                ownerRoot != null ? ownerRoot : transform);

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
