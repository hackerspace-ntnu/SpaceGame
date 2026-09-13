// A slot cut through a wall, that a rope rides inside and slides along.
//
// A rail is not a pin. The rope's bend is wherever on the slot makes the whole rope shortest, so it
// MOVES as either end moves — which is what gives one player two continuous axes while walking:
// along the rail sweeps the far end sideways, away from the rail spends rope and draws it in.
//
// The bend is solved in closed form, and that is not an optimisation. Nothing about a rope's shape
// is ever sent between machines; both solve it from the same replicated positions and have to agree
// without talking. A closed form has no iteration count to disagree about and no raycast to land
// differently on two machines.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>One slot. Rope rides it, slides along it, and hands over to a connected one at its ends.</summary>
    public class LeashRail : MonoBehaviour
    {
        [Tooltip("The two ends of the slot. Leave empty to use this transform for both.")]
        [SerializeField] private Transform from;
        [SerializeField] private Transform to;

        [Tooltip("Rails whose mouths this one hands a rope over to. SET BOTH WAYS — a one-way " +
                 "link is a rail a rope can get onto and never come off.")]
        [SerializeField] private LeashRail[] connections = System.Array.Empty<LeashRail>();

        [Tooltip("How close a bend must come before this rail takes it, in metres.")]
        [SerializeField, Min(0.01f)] private float captureRadius = 0.6f;

        [Tooltip("How close two rails' mouths must be to hand a rope over, in metres.")]
        [SerializeField, Min(0.01f)] private float junctionRadius = 0.5f;

        public Vector3 A => from != null ? from.position : transform.position;

        public Vector3 B => to != null ? to.position : transform.position;

        public float CaptureRadius => captureRadius;

        /// <summary>
        /// The point on a rail that makes the total rope shortest.
        ///
        /// <para>
        /// Written along the rail's own axis, each end reduces to a distance <c>t</c> along it and a
        /// perpendicular height <c>h</c>, which turns this into the flat reflection problem:
        /// <c>t* = (t_from·h_to + t_to·h_from) / (h_from + h_to)</c>. Exact, one expression, no loop.
        /// </para>
        /// <para>
        /// Both ends lying on the rail's own line gives <c>h_from + h_to == 0</c> and no unique
        /// answer — every point between them is equally short. The midpoint is chosen because it is
        /// stable: any other tie-break makes the bend jump as the degenerate case is entered and
        /// left, which reads as the rope snagging on nothing.
        /// </para>
        /// </summary>
        public static Vector3 ClosestBend(Vector3 a, Vector3 b, Vector3 from, Vector3 to)
        {
            Vector3 axis = b - a;
            float span = axis.magnitude;
            if (span < 0.0001f) return a;

            Vector3 direction = axis / span;

            float tFrom = Vector3.Dot(from - a, direction);
            float tTo = Vector3.Dot(to - a, direction);

            float hFrom = Vector3.Distance(from, a + direction * tFrom);
            float hTo = Vector3.Distance(to, a + direction * tTo);

            float total = hFrom + hTo;

            float t = total > 0.0001f
                ? (tFrom * hTo + tTo * hFrom) / total
                : (tFrom + tTo) * 0.5f;

            return a + direction * Mathf.Clamp(t, 0f, span);
        }

        /// <summary>Where a rope running between these two points bends on this rail.</summary>
        public Vector3 BendFor(Vector3 from, Vector3 to) => ClosestBend(A, B, from, to);

        /// <summary>Whether a bend has slid all the way to one of this rail's mouths.</summary>
        public bool AtEnd(Vector3 bend)
        {
            const float Epsilon = 0.02f;
            return Vector3.Distance(bend, A) < Epsilon || Vector3.Distance(bend, B) < Epsilon;
        }

        /// <summary>
        /// The rail this bend should hand over to, or null.
        ///
        /// <para>
        /// Only ever asked of a bend that has already run out of rail. Walking on past the end of one
        /// slot is the entire input for changing slots — there is no key, because the moment a player
        /// needs to change rails is the moment their partner is holding the load alone, and that is
        /// the worst possible time to ask somebody to stop and aim at something.
        /// </para>
        /// </summary>
        public LeashRail HandOverAt(Vector3 bend)
        {
            for (int i = 0; i < connections.Length; i++)
            {
                LeashRail next = connections[i];
                if (next == null || next == this) continue;

                if (Vector3.Distance(bend, next.A) <= junctionRadius) return next;
                if (Vector3.Distance(bend, next.B) <= junctionRadius) return next;
            }

            return null;
        }

        // ── Live registry ──────────────────────────────────────────────────────

        private static readonly List<LeashRail> LiveRails = new();

        private void OnEnable() => LiveRails.Add(this);

        private void OnDisable() => LiveRails.Remove(this);

        /// <summary>
        /// The rail that should take a bend at this point, or null.
        ///
        /// <para>
        /// A registry rather than a trigger volume or a scene search: this is asked once per wrap per
        /// physics step, and it has to answer identically on two machines — which a physics query
        /// against colliders that streamed in at different moments does not reliably do.
        /// </para>
        /// </summary>
        public static LeashRail Capturing(Vector3 point)
        {
            LeashRail best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < LiveRails.Count; i++)
            {
                LeashRail rail = LiveRails[i];
                if (rail == null) continue;

                // Both arguments equal reduces the reflection solve to the plain closest point on
                // the segment, which is exactly the distance wanted here.
                float distance = Vector3.Distance(point, rail.BendFor(point, point));
                if (distance > rail.captureRadius || distance >= nearest) continue;

                nearest = distance;
                best = rail;
            }

            return best;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(A, B);
            Gizmos.DrawWireSphere(A, 0.15f);
            Gizmos.DrawWireSphere(B, 0.15f);
        }
    }
}
