using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How close a player has to be, and whether they have to see it, to take something off one
    /// particular wall.
    ///
    /// <para>
    /// The defaults are no rule at all, which is every wall in the game but one: a gear wall is
    /// reached with the crosshair at the Interactor's own range, and a pack-item ray that ignores
    /// everything between the eye and the gear. That is right for a board in a room. It is wrong
    /// for the satellite dish's transmitter cradle, which sits 40 m above the catwalk on purpose —
    /// the crew are meant to grapple out to it, not lift it off through the feed cabin from the
    /// far side.
    /// </para>
    /// <para>
    /// Asked twice, with two different eyes. The taker's own machine asks from the camera before
    /// it offers the take, so the crosshair never promises one the server will refuse. The server
    /// asks again on the request, from the point of the taker's BODY nearest the item: the camera
    /// is not something the server can see, and the body is never further away than the eye that
    /// stands inside it, so an honest client is never refused while a dishonest one gains at most
    /// the height of its own capsule.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WallTakeReach
    {
        [Tooltip("How far from the item, in metres, a player can take it from. 0 leaves the " +
                 "wall's ordinary reach, the crosshair's own range.")]
        [SerializeField, Min(0f)] private float maxDistance;

        [Tooltip("Refuse a take when anything solid stands between the player and the item. Off " +
                 "for an ordinary wall, whose take ray ignores what is in front of the gear.")]
        [SerializeField] private bool needsLineOfSight;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>True when this wall asks nothing of the taker — every wall that predates it.</summary>
        public bool IsUnlimited => maxDistance <= 0f && !needsLineOfSight;

        public float MaxDistance => maxDistance;

        public bool NeedsLineOfSight => needsLineOfSight;

        /// <summary>
        /// Can someone whose eye is at <paramref name="eye"/> take the item at
        /// <paramref name="item"/>? <paramref name="taker"/> and <paramref name="wall"/> are what
        /// the sight line looks past: the taker's own body, and the wall with the gear on it.
        /// </summary>
        public bool Allows(Vector3 eye, Vector3 item, Transform taker, Transform wall)
        {
            if (maxDistance > 0f && Vector3.Distance(eye, item) > maxDistance) return false;

            return !needsLineOfSight || IsClear(eye, item, taker, wall);
        }

        /// <summary>
        /// The server's question: the same rule, from the point of the taker's body nearest the
        /// item. See the class remarks for why the body and not the eye.
        /// </summary>
        public bool AllowsBody(GameObject taker, Vector3 item, Transform wall)
        {
            if (IsUnlimited) return true;
            if (taker == null) return false;

            return Allows(NearestBodyPoint(taker, item), item, taker.transform, wall);
        }

        /// <summary>
        /// The point of the body's solid colliders nearest <paramref name="target"/>, or its pivot
        /// when it has none. Gear riding the body (a worn pack's display copies) is not the body.
        /// </summary>
        public static Vector3 NearestBodyPoint(GameObject taker, Vector3 target)
        {
            Vector3 best = taker.transform.position;
            float bestSqr = float.MaxValue;

            foreach (Collider c in taker.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || c.GetComponentInParent<PackContainer>() != null) continue;

                Vector3 p = c.bounds.ClosestPoint(target);
                float sqr = (p - target).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = p;
            }

            return best;
        }

        /// <summary>
        /// Nothing solid between the two points, the taker's own hierarchy and the wall's aside.
        ///
        /// <para>
        /// Cast both ways. A ray never hits the back of a mesh collider, so a ray that starts
        /// inside the feed cabin leaves through its wall unseen; the reverse ray, from the gear,
        /// meets that same wall face-on.
        /// </para>
        /// </summary>
        private static bool IsClear(Vector3 from, Vector3 to, Transform taker, Transform wall) =>
            !Blocked(from, to, taker, wall) && !Blocked(to, from, taker, wall);

        /// <summary>
        /// Filtered on the collider's transform, never the hit's: under a moving rig the hit's
        /// transform is the rigidbody's root, which is the whole tower.
        /// </summary>
        private static bool Blocked(Vector3 from, Vector3 to, Transform taker, Transform wall)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / length, Hits, length,
                                                ~LayerMask.GetMask("Player"),
                                                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null || Hits[i].distance <= 0f) continue;

                Transform t = c.transform;
                if (taker != null && t.IsChildOf(taker.root)) continue;
                if (wall != null && t.IsChildOf(wall)) continue;

                return true;
            }

            return false;
        }
    }
}
