// The player's body capsule in world space, measured from the authored collider.
//
// The capsule is authored 2 m tall on a transform stretched to 3, and the pivot sits a metre above the
// soles, so every "where are the feet / does the body fit here" question has to go through the
// collider's lossy scale. LadderClimber answered those privately; the ledge climber asks the same
// questions, so the answers live here once.
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// World-space shape of the player's body capsule, and the overlap/sweep queries traversal needs.
    /// Colliders on <c>self</c> (hitboxes, ragdoll capsules riding the same rigidbody) are never
    /// counted as obstacles.
    /// </summary>
    public readonly struct PlayerBodyShape
    {
        private readonly CapsuleCollider capsule;
        private readonly Rigidbody self;

        public PlayerBodyShape(CapsuleCollider capsule, Rigidbody self)
        {
            this.capsule = capsule;
            this.self = self;
        }

        /// <summary>The body's world height: the capsule is authored 2 m on a transform stretched to 3.</summary>
        public float Height => capsule.height * capsule.transform.lossyScale.y;

        public float Radius
        {
            get
            {
                Vector3 scale = capsule.transform.lossyScale;
                return capsule.radius * Mathf.Max(scale.x, scale.z);
            }
        }

        public Vector3 Feet
        {
            get
            {
                Bounds bounds = capsule.bounds;
                return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }
        }

        /// <summary>The body's capsule end-points, in world space, with its feet at <paramref name="feet"/>.</summary>
        public void At(Vector3 feet, out Vector3 low, out Vector3 high)
        {
            float radius = Radius;
            low = feet + Vector3.up * radius;
            high = feet + Vector3.up * Mathf.Max(radius, Height - radius);
        }

        public bool IsSelf(Collider c) => c.attachedRigidbody != null && c.attachedRigidbody == self;

        /// <summary>Whether the body standing with its feet at <paramref name="feet"/> would be inside something.</summary>
        public bool BlockedAt(Vector3 feet)
        {
            At(feet, out Vector3 low, out Vector3 high);
            foreach (Collider c in Physics.OverlapCapsule(low, high, Radius, Physics.DefaultRaycastLayers,
                                                          QueryTriggerInteraction.Ignore))
                if (!IsSelf(c))
                    return true;
            return false;
        }

        /// <summary>
        /// Whether the body can sweep <paramref name="distance"/> along <paramref name="direction"/> from
        /// <paramref name="feet"/> without meeting anything. Contacts it starts in (distance 0 — the floor
        /// under it, a wall it leans on) are not in its way.
        /// </summary>
        public bool CastClear(Vector3 feet, Vector3 direction, float distance)
        {
            At(feet, out Vector3 low, out Vector3 high);
            foreach (RaycastHit hit in Physics.CapsuleCastAll(low, high, Radius, direction, distance,
                                                             Physics.DefaultRaycastLayers,
                                                             QueryTriggerInteraction.Ignore))
                if (!IsSelf(hit.collider) && hit.distance > 0f)
                    return false;
            return true;
        }
    }
}
