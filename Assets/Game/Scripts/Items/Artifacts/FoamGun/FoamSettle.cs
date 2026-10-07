// Where a dab of foam actually comes to rest, as opposed to where the stream hit.
//
// THE PILLAR THIS EXISTS TO STOP. The arc lands a dab on the first thing it meets, foam included,
// and the dab used to be spawned exactly there. Hold the trigger on one spot and every dab lands on
// the upstream face of the one before it, so the mass grows straight back along the stream: a
// leaning column, which is not what foam does. Real foam lands wet, slides off whatever it landed
// on and runs downhill until the slope under it is shallow enough to hold it — a mound, widening as
// it rises, which is the shape a player expects from spraying one spot.
//
// IT IS A STATIC SOLVE, NOT A SIMULATION. The rest point is resolved once, on the server, BEFORE the
// blob is spawned, and the spawn payload carries it. That is deliberate: a FoamBlob has no
// NetworkTransform (see FoamBlob's header — everything about a lump is derived from two replicated
// numbers), so a lump that flowed over time would have to start replicating its position, at the dab
// rate, for the whole of a spray. Solving first costs one spawn position and nothing on the wire.
//
// THE REPOSE ANGLE IS EMERGENT, NOT AUTHORED (GDC-L1-SYS-0002 — author the rule, not the outcome).
// There is no "pile at 34 degrees" number here. The lump falls a step, is pushed out of every lump
// it now overlaps, and repeats; a slope steeper than the packing can hold keeps pushing it further
// than the fall gained, so it walks down. What comes out is the angle a pile of loosely packed
// ellipsoids stands at, and it changes on its own when the blob shape does.
using SpaceGame.Characters;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Turns the point a spray HIT into the point its lump comes to REST: down the pile, out of the
    /// foam already standing, and stopped by the world.
    /// </summary>
    public static class FoamSettle
    {
        /// <summary>
        /// Scratch for the per-step sweep. Nothing is held between calls — the solve is server-side
        /// and single-threaded, like every other query in this artifact.
        /// </summary>
        private static readonly RaycastHit[] Swept = new RaycastHit[8];

        /// <summary>
        /// Walk a dab downhill from where the stream hit until it rests.
        ///
        /// <para>
        /// Each step drops the candidate by <paramref name="stepShare"/> of a radius, pushes it back
        /// out of every committed foam volume it now sits inside, stops that whole move against the
        /// world, and finally seats it on whatever is under it. The push is what carries it
        /// sideways: a lump dropped onto the crown of another is ejected along the line between
        /// their centres, which points outward and down unless the two are exactly stacked.
        /// </para>
        /// <para>
        /// The loop ends as soon as a step moves the candidate less than a twentieth of itself,
        /// which is the lump having found a seat rather than a step budget running out. The budget
        /// is the guard against the case that never settles — a dab in a crevice being pushed back
        /// and forth between two walls.
        /// </para>
        /// </summary>
        /// <param name="landing">Where the arc met a surface or a committed foam volume.</param>
        /// <param name="contactNormal">That surface's outward normal, which is where the lump starts.</param>
        /// <param name="radius">The lump's full half-extent, so it rests ON things rather than in them.</param>
        /// <param name="world">What counts as solid. The same mask the arc was traced against.</param>
        /// <param name="self">The sprayer, ignored: a lump must not seat itself on its own body.</param>
        /// <param name="carrier">Whatever the sprayer is riding, ignored for the same reason.</param>
        /// <param name="steps">How many times the lump may fall and be pushed back out.</param>
        /// <param name="stepShare">How far one fall is, as a share of the lump's radius.</param>
        /// <param name="overlap">
        /// How deeply two lumps are allowed to interpenetrate, as a share of a radius. It is not
        /// slack: the weld field fuses neighbours that OVERLAP, so lumps packed to exactly touching
        /// read as a heap of separate balls with fillets between them.
        /// </param>
        /// <param name="maxDrop">
        /// The furthest a lump may fall in one step when nothing is under it, in metres. The gun
        /// hands it its own reach: foam cannot come to rest further below the stream than the gun
        /// could have thrown it, and it is not a knob of its own for exactly that reason.
        /// </param>
        public static Vector3 Resolve(Vector3 landing, Vector3 contactNormal, float radius,
                                      LayerMask world, Transform self, Transform carrier,
                                      int steps, float stepShare, float overlap, float maxDrop)
        {
            if (radius <= 0f) return landing;

            // Clearance is the pair's separation minus this lump's own extents, which is what
            // FoamBlob.PushOutOfCommitted adds to its own. Both halves of an overlapping pair are
            // paid for here, on the one that is still moving.
            float clearance = radius * Mathf.Clamp01(1f - overlap);
            float step = Mathf.Max(0.01f, radius * Mathf.Max(0.05f, stepShare));

            // Seated on what it hit rather than buried in it. Without this the first push-out is
            // against the ground, which fires the lump straight back up the stream it came down.
            Vector3 point = landing + contactNormal * clearance;

            float settled = step * 0.05f;
            settled *= settled;

            for (int i = 0; i < steps; i++)
            {
                Vector3 from = point;

                Vector3 dropped = from + Vector3.down * step;
                Vector3 pushed = FoamField.PushOutOfCommitted(dropped, clearance, out bool onFoam);

                point = SeatOnGround(StopAgainstWorld(from, pushed, clearance, world, self, carrier),
                                     clearance, maxDrop, onFoam, world, self, carrier);

                if ((point - from).sqrMagnitude <= settled) break;
            }

            return point;
        }

        /// <summary>
        /// Put the lump on top of whatever is under it: never buried, and never hanging.
        ///
        /// <para>
        /// THIS IS THE ONE THAT KEEPS FOAM OUT OF THE TERRAIN, and it exists because the swept test
        /// alone cannot. A ray that STARTS inside a collider reports nothing — that is how
        /// <c>Physics.Raycast</c> works — so a lump that a neighbour's push drove under the ground
        /// had nothing left to stop it and simply fell for ever. The cast here starts a clearance
        /// ABOVE the lump's centre, so it begins outside anything the lump is sitting on OR sunk
        /// into, and the seat is written as an absolute height rather than as a correction.
        /// </para>
        /// <para>
        /// It also lands a lump that walked off the edge of the mound. Once the foam stops pushing
        /// — <paramref name="onFoam"/> false, meaning nothing is holding this lump up any more —
        /// the seat is applied DOWNWARD as well, dropping it the whole way to the ground in one
        /// step instead of a fraction of a radius at a time. While foam is still pushing, only the
        /// upward half applies, or a lump resting on the crown of a mound would be yanked down
        /// through it to the floor.
        /// </para>
        /// </summary>
        private static Vector3 SeatOnGround(Vector3 point, float clearance, float maxDrop,
                                            bool onFoam, LayerMask world, Transform self,
                                            Transform carrier)
        {
            Vector3 above = point + Vector3.up * clearance;
            float range = Mathf.Max(clearance * 2f, maxDrop);

            int count = Physics.RaycastNonAlloc(above, Vector3.down, Swept, range, world,
                                                QueryTriggerInteraction.Ignore);

            if (!AimProvider.NearestOutside(Swept, count, self, carrier, out RaycastHit hit))
                return point;

            float seat = hit.point.y + clearance;
            if (point.y >= seat && onFoam) return point;

            point.y = seat;
            return point;
        }

        /// <summary>
        /// Cut a step short where it runs into the world — a wall the lump slid into, rather than
        /// the ground under it, which <see cref="SeatOnGround"/> owns.
        ///
        /// <para>
        /// The ray is cast from the lump's CENTRE and over the move plus a clearance, because the
        /// centre is a clearance clear of every surface the lump is already resting on: a ray only
        /// as long as the move would never reach the wall a lump is already leaning on.
        /// </para>
        /// </summary>
        private static Vector3 StopAgainstWorld(Vector3 from, Vector3 to, float clearance,
                                                LayerMask world, Transform self, Transform carrier)
        {
            Vector3 move = to - from;
            float distance = move.magnitude;
            if (distance < 1e-5f) return to;

            int count = Physics.RaycastNonAlloc(from, move / distance, Swept, distance + clearance,
                                                world, QueryTriggerInteraction.Ignore);

            // Blind to the sprayer and their ride, exactly as the arc that produced this landing
            // point is: a bell held at chest height lands foam at the player's own feet on purpose,
            // and a lump that seated itself on the sprayer's capsule would ride away with them.
            return AimProvider.NearestOutside(Swept, count, self, carrier, out RaycastHit hit)
                ? hit.point + hit.normal * clearance
                : to;
        }
    }
}
