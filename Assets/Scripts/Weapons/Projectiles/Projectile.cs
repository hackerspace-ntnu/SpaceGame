using UnityEngine;

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

    protected Transform ownerRoot;
    protected bool initialized;
    protected float spawnTime;
    protected Vector3 direction = Vector3.forward;

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

        CancelInvoke(nameof(DestroyProjectile));
        Invoke(nameof(DestroyProjectile), lifeTime);
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
        // Apply damage if target has health
        HealthComponent health = hit.collider.GetComponentInParent<HealthComponent>();
        if (health != null)
        {
            health.Damage(damage);
        }

        OnImpact(hit.point, hit.normal, hit.collider);

        if (destroyOnHit)
        {
            DestroyProjectile();
        }
    }

    /// <summary>
    /// Called when projectile hits something. Override for custom impact effects.
    /// </summary>
    protected virtual void OnImpact(Vector3 position, Vector3 normal, Collider hitCollider)
    {
        // Override in subclasses for particle effects, sounds, etc.
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
    /// Get elapsed time since projectile was spawned.
    /// </summary>
    protected float GetElapsedTime()
    {
        return Time.time - spawnTime;
    }
}
