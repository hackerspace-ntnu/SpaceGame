using UnityEngine;

/// <summary>
/// BasicProjectile - A simple hitscan-like projectile that moves in a straight line.
/// Implements the Projectile base class for basic weapon functionality.
/// Features straightforward collision detection and damage application.
/// </summary>
public class BasicProjectile : Projectile
{
    [Header("Movement")]
    [SerializeField] private float speed = 50f;
    [SerializeField] private float checkInterval = 0.01f;

    private float lastCollisionCheck = 0f;
    private Vector3 lastPosition;

    protected override void UpdateMovement()
    {
        if (!initialized)
        {
            return;
        }

        // Move projectile forward
        transform.position += direction * speed * Time.deltaTime;

        // Periodic collision checks to avoid missing fast-moving collisions
        if (Time.time - lastCollisionCheck >= checkInterval)
        {
            CheckCollision();
            lastCollisionCheck = Time.time;
        }

        lastPosition = transform.position;
    }

    private void Update()
    {
        UpdateMovement();
    }

    /// <summary>
    /// Check for collision with environment or entities.
    /// </summary>
    private void CheckCollision()
    {
        if (!initialized)
        {
            return;
        }

        // Raycast from last position to current position to detect hits
        float distance = Vector3.Distance(lastPosition, transform.position);
        RaycastHit hit;

        if (Physics.Raycast(lastPosition, direction, out hit, distance + collisionRadius, hitMask))
        {
            // Ignore owner hits
            if (IsOwnerHit(hit.transform))
            {
                return;
            }

            // Handle the collision
            HandleHit(hit);
        }
    }

    /// <summary>
    /// Called when projectile hits something. 
    /// Override to add visual effects (explosions, impacts, etc.)
    /// </summary>
    protected override void OnImpact(Vector3 position, Vector3 normal, Collider hitCollider)
    {
        // Base implementation - can be extended for visual effects
        // such as impact particles, sounds, etc.
        Debug.Log($"BasicProjectile hit {hitCollider.gameObject.name} at {position}");
    }
}
