// What is underneath a rope, so it can be drawn lying ON the world rather than sunk through it.
//
// Split out of LeashRope because that file is deliberately free of physics — it reads two knots and
// a length and writes points into a LineRenderer, and nothing there should be casting rays. The
// rope asks this; this is the only place in the leash that touches Physics outside the constraint.
//
// The two rules below are not obvious and both were learned elsewhere in this project, so they are
// spelled out rather than left to be rediscovered: a height probe must reject loose bodies, and it
// must reject the thing the rope is tied to.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A downward probe sized for a rope. Owns its buffer, so a per-frame query allocates nothing.
    /// </summary>
    public class LeashGround
    {
        private readonly LayerMask mask;

        // RaycastNonAlloc does not sort and stops filling once it is out of room, so a buffer that
        // fills up discards hits arbitrarily — including, sometimes, the actual ground. Grown and
        // re-cast rather than letting a rope conclude there is no floor here. Same reasoning, and
        // the same failure, as WalkerGround's.
        private RaycastHit[] buffer = new RaycastHit[16];
        private const int MaxBuffer = 128;

        private Transform endA;
        private Transform endB;

        public LeashGround(LayerMask mask) => this.mask = mask;

        /// <summary>
        /// The two things this rope is tied to. Their own surfaces are not ground for it.
        ///
        /// <para>
        /// Without this a rope tied to a creature's flank is lifted onto its back, and one tied to
        /// the underside of anything is lifted straight through it. The knots themselves are never
        /// moved, so this only concerns the span between them.
        /// </para>
        /// </summary>
        public void TiedBetween(Transform a, Transform b)
        {
            endA = a;
            endB = b;
        }

        /// <summary>
        /// The top of the world under <paramref name="at"/>, searching <paramref name="reach"/>
        /// metres downward, or <see cref="float.NegativeInfinity"/> if there is nothing there.
        ///
        /// <para>
        /// Negative infinity rather than a bool, because every caller wants to feed the answer
        /// straight into a <c>Mathf.Max</c> and "no ground" should lose that comparison.
        /// </para>
        /// </summary>
        public float HeightUnder(Vector3 at, float reach)
        {
            if (!(reach > 0f)) return float.NegativeInfinity;   // also rejects NaN

            int n = Cast(at, reach);

            float nearest = float.MaxValue;
            float height = float.NegativeInfinity;

            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = buffer[i];
                Collider col = hit.collider;

                if (col == null) continue;
                if (hit.distance >= nearest) continue;
                if (IsLooseBody(col)) continue;
                if (BelongsToAnEnd(col)) continue;

                nearest = hit.distance;
                height = hit.point.y;
            }

            return height;
        }

        private int Cast(Vector3 from, float reach)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, buffer, reach,
                                            mask, QueryTriggerInteraction.Ignore);

            while (n >= buffer.Length && buffer.Length < MaxBuffer)
            {
                buffer = new RaycastHit[buffer.Length * 2];
                n = Physics.RaycastNonAlloc(from, Vector3.down, buffer, reach,
                                            mask, QueryTriggerInteraction.Ignore);
            }

            return n;
        }

        /// <summary>
        /// Whether a hit belongs to something standing on the world rather than being the world.
        ///
        /// <para>
        /// Ground is terrain and buildings, which are static colliders. Anything under its own
        /// physics is cargo, and a rope does not rest on cargo — it rests on what the cargo is
        /// standing on. Skipping this is how a height probe ends up chasing the very object it is
        /// meant to be measuring against, which is the bug that once walked a machine into the sky
        /// on top of its own passenger.
        /// </para>
        /// </summary>
        private static bool IsLooseBody(Collider col)
        {
            Rigidbody rb = col.attachedRigidbody;
            return rb != null && !rb.isKinematic;
        }

        private bool BelongsToAnEnd(Collider col)
        {
            Transform t = col.transform;
            return (endA != null && t.IsChildOf(endA)) || (endB != null && t.IsChildOf(endB));
        }
    }
}
