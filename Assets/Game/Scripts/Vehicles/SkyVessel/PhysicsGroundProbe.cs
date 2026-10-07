// IGroundProbe over the scene: raycasts and box checks on the layers vision treats as solid,
// and NavMesh.SamplePosition. Triggers are ignored explicitly — this project's physics settings
// report them to queries by default, and a trigger volume is not ground or an obstacle.
//
// A vessel asking about the ground under itself would otherwise find itself: the ground ray starts
// high above the query point and the hull is the first thing in its way, and a headroom check over
// a site the hull is already descending onto is full of hull. IgnoreHierarchy names what the probe
// looks straight through — the vessel, and everyone seated under it.
//
// And anything with a Rigidbody is never GROUND, though it is still an obstacle: an NPC who has just
// stepped off stands under the hull (and, with transforms not auto-synced, its collider is still up
// on the deck for the rest of the frame), so the next passenger's ground ray landed on its head.
using System;
using SpaceGame.Agents;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Vehicles
{
    [Serializable]
    public sealed class PhysicsGroundProbe : IGroundProbe
    {
        // Query buffer capacity: comfortably more than a hull's own colliders, its passengers and the
        // ground beneath them in one column. Physics fills it in no particular order, so a query
        // crossing more colliders than this could miss one — never the case for one vessel's column.
        private const int HitCapacity = 64;

        [Tooltip("The ground ray starts this far above the queried point and looks as far below it. " +
                 "Must exceed the terrain relief between a quarry and its landing rings.")]
        [SerializeField] private float castReach = 300f;

        [Tooltip("Gap between the footprint's resting height and the bottom of the headroom check, so " +
                 "the ground the hull stands on is not read as an obstacle.")]
        [SerializeField] private float surfaceSkin = 0.5f;

        [NonSerialized] private Transform ignored;
        [NonSerialized] private RaycastHit[] rayHits;
        [NonSerialized] private Collider[] overlaps;

        /// <summary>Every collider under <paramref name="root"/> is invisible to this probe from now on.</summary>
        public void IgnoreHierarchy(Transform root) => ignored = root;

        public bool TryGround(Vector3 xzPoint, out Vector3 point, out Vector3 normal)
        {
            if (CastDown(xzPoint + Vector3.up * castReach, castReach * 2f, out point, out normal)) return true;
            point = xzPoint;
            return false;
        }

        /// <summary>
        /// The ground strictly below <paramref name="from"/>, never above it: for something falling,
        /// which must not come to rest on a structure overhead (a wreck under the sky city).
        /// </summary>
        public bool TryGroundBelow(Vector3 from, out Vector3 point) => CastDown(from, castReach, out point, out _);

        private bool CastDown(Vector3 origin, float distance, out Vector3 point, out Vector3 normal)
        {
            rayHits ??= new RaycastHit[HitCapacity];

            int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, distance,
                                                PerceptionModule.SolidGeometryLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            point = origin;
            normal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayHits[i];
                if (hit.distance >= nearest || IsIgnored(hit.collider) || hit.collider.attachedRigidbody != null) continue;
                nearest = hit.distance;
                point = hit.point;
                normal = hit.normal;
            }
            return nearest < float.PositiveInfinity;
        }

        public bool IsClear(Vector3 center, float radius, float height)
        {
            overlaps ??= new Collider[HitCapacity];

            // A box, not a capsule: a capsule's rounded ends reach the full radius only well above the
            // ground, so a rock by the rim between two footprint samples would pass.
            Vector3 middle = center + Vector3.up * (surfaceSkin + height * 0.5f);
            var halfExtents = new Vector3(radius, height * 0.5f, radius);
            int count = Physics.OverlapBoxNonAlloc(middle, halfExtents, overlaps, Quaternion.identity,
                                                   PerceptionModule.SolidGeometryLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
                if (!IsIgnored(overlaps[i])) return false;
            return true;
        }

        public bool TryNavMesh(Vector3 point, float maxDistance, out Vector3 onMesh)
        {
            bool found = NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, NavMesh.AllAreas);
            onMesh = found ? hit.position : point;
            return found;
        }

        private bool IsIgnored(Collider collider) => ignored != null && collider.transform.IsChildOf(ignored);
    }
}
