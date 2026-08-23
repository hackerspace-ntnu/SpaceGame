using UnityEngine;

namespace SpaceGame.Weapons
{
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

            Vector3 start = transform.position;
            Vector3 end = start + direction * speed * Time.deltaTime;

            // Before the move is committed and before anything is traced: a shot
            // that went through an aperture has to resolve against the room it
            // came out into, and `direction` — which CheckCollision traces along
            // — is turned here.
            bool crossed = CrossPortal(ref start, ref end);

            transform.position = end;

            // The exit, not wherever the shot was a frame ago, is where the far
            // side of this move begins.
            if (crossed) lastPosition = start;

            // Periodic collision checks to avoid missing fast-moving collisions.
            // A crossing is always checked, whichever side of the interval it
            // fell on, since it is the one frame the shot changes rooms.
            if (crossed || Time.time - lastCollisionCheck >= checkInterval)
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
            // The base picks the flesh or hard impact sound and plays it.
            base.OnImpact(position, normal, hitCollider);
        }
    }
}
