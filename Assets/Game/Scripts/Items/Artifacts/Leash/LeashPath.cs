// The shape of a rope, once it is allowed to touch the world.
//
// A leash used to be the chord between its two knots: it measured the straight line, pulled along the
// straight line, and passed through anything in between. LeashGround papered over the visible half of
// that by DRAWING the rope draped on the ground while the constraint still measured the line
// underneath — so the rope looked like it lay on the hill and behaved like it went through it.
//
// This is the honest version. The rope is a polyline; the constraint measures it; rope spent going
// round a corner is rope the far end no longer has. That last sentence is the whole feature: it is
// what lets someone reel a load in by walking away from the corner their rope is bent around,
// without a winch existing anywhere in the system.
//
// Casting is injected rather than called directly so the geometry can be tested without a physics
// scene. LeashWorldCast, at the bottom, is the only part of this file that knows Physics exists —
// the same split LeashGround used to hold.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>Is the straight line between two points blocked? Reports the first contact along it.</summary>
    public delegate bool LeashCast(Vector3 from, Vector3 to,
                                   out Vector3 point, out Vector3 normal, out Collider surface);

    /// <summary>A rope's path: its two ends, and every place it bends between them.</summary>
    public class LeashPath
    {
        /// <summary>What the wrap step is allowed to do. Authored on the leash artifact.</summary>
        public struct Tuning
        {
            /// <summary>How far off a surface a bend point sits, in metres.</summary>
            public float clearance;

            /// <summary>Ceiling on bends in one rope. Past it the rope stops wrapping.</summary>
            public int maxWraps;
        }

        private readonly List<LeashWrap> wraps = new();
        private readonly List<Vector3> points = new();

        public IReadOnlyList<LeashWrap> Wraps => wraps;

        public int Count => wraps.Count;

        public void Clear() => wraps.Clear();

        // ── Pure geometry ──────────────────────────────────────────────────────

        /// <summary>Total length along a polyline. Fewer than two points measures nothing.</summary>
        public static float PolylineLength(IReadOnlyList<Vector3> pts)
        {
            if (pts == null || pts.Count < 2) return 0f;

            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);

            return total;
        }

        /// <summary>
        /// A candidate bend, or nothing.
        ///
        /// <para>
        /// Refused when the offset point would sit within two clearances of one of the points it is
        /// meant to bend between. That is the degenerate case, and it is not rare: a rope lying along
        /// a flat wall contacts it everywhere, and without this the list fills with waypoints a
        /// millimetre apart in a single step and the rope's measured length collapses.
        /// </para>
        /// </summary>
        public static bool TryMake(Vector3 contact, Vector3 normal, Collider surface,
                                   Vector3 from, Vector3 to, in Tuning tuning, out LeashWrap wrap)
        {
            wrap = default;

            Vector3 position = contact + normal * tuning.clearance;
            float minSegment = Mathf.Max(0.002f, tuning.clearance * 2f);

            if (Vector3.Distance(position, from) < minSegment) return false;
            if (Vector3.Distance(position, to) < minSegment) return false;

            wrap = new LeashWrap(position, normal, surface);
            return true;
        }

        // ── The path ───────────────────────────────────────────────────────────

        /// <summary>
        /// A → every wrap → B, in a buffer this path owns.
        ///
        /// <para>
        /// The returned list is overwritten by the next call. Every caller consumes it immediately,
        /// and handing out a fresh array fifty times a second per rope is garbage for nothing.
        /// </para>
        /// </summary>
        public IReadOnlyList<Vector3> PointsBetween(Vector3 endA, Vector3 endB)
        {
            points.Clear();
            points.Add(endA);
            for (int i = 0; i < wraps.Count; i++) points.Add(wraps[i].Position);
            points.Add(endB);

            return points;
        }

        /// <summary>How much rope this path uses to get from one end to the other.</summary>
        public float TotalLength(Vector3 endA, Vector3 endB) =>
            PolylineLength(PointsBetween(endA, endB));

        /// <summary>
        /// Unit vector from one end toward the point the rope actually pulls it at — its nearest
        /// bend, or the far end when the rope is straight.
        ///
        /// <para>
        /// This is the difference between a wrap that means something and a wrap that is decoration.
        /// Pull an end toward the FAR end and a rope bent ninety degrees round a pillar still drags
        /// its load straight through the pillar; pull it toward the bend and the load comes round the
        /// corner.
        /// </para>
        /// </summary>
        public Vector3 DirectionFrom(bool isA, Vector3 endA, Vector3 endB)
        {
            Vector3 self = isA ? endA : endB;

            Vector3 neighbour = wraps.Count == 0
                ? (isA ? endB : endA)
                : (isA ? wraps[0].Position : wraps[wraps.Count - 1].Position);

            Vector3 delta = neighbour - self;
            float distance = delta.magnitude;

            return distance > 0.0001f ? delta / distance : Vector3.forward;
        }

        /// <summary>
        /// Bring the path up to date: drop dead wraps, unwrap what no longer bends, then wrap what
        /// now does.
        ///
        /// <para>
        /// Unwrap runs BEFORE wrap. The other order re-tests a waypoint inserted this same step
        /// against a neighbour it has not been introduced to yet, and removes it again immediately.
        /// </para>
        /// </summary>
        public void Step(Vector3 endA, Vector3 endB, LeashCast cast, in Tuning tuning)
        {
            if (cast == null) return;

            DropDead();
            Unwrap(endA, endB, cast);
            Wrap(endA, endB, cast, tuning);
            SlideRails(endA, endB);
        }

        /// <summary>A rope bent around a crate that has since been carried off is bent around nothing.</summary>
        private void DropDead()
        {
            // A rail-bound bend is exempt: it is held by an authored slot, not by a collider, and
            // it legitimately has no surface of its own.
            for (int i = wraps.Count - 1; i >= 0; i--)
                if (wraps[i].Rail == null && wraps[i].Surface == null) wraps.RemoveAt(i);
        }

        /// <summary>
        /// Re-solve every rail-bound bend, and hand a bend that has run out of rail to the next one.
        ///
        /// <para>
        /// Runs after wrapping, so a bend the probe has just made against a rim wall is picked up by
        /// the slot cut through that wall in the same step rather than a frame later — a bend that
        /// spends one step frozen on the wall and the next sliding on the rail visibly jumps.
        /// </para>
        /// <para>
        /// A rail is a PREFERRED WRAP POINT, not a parallel system. Everything downstream — the
        /// length measurement, the pull direction, the drawing — treats one of these exactly as it
        /// treats a bend on a rock.
        /// </para>
        /// </summary>
        private void SlideRails(Vector3 endA, Vector3 endB)
        {
            for (int i = 0; i < wraps.Count; i++)
            {
                LeashWrap wrap = wraps[i];

                LeashRail rail = wrap.Rail != null ? wrap.Rail : LeashRail.Capturing(wrap.Position);
                if (rail == null) continue;

                Vector3 before = i == 0 ? endA : wraps[i - 1].Position;
                Vector3 after = i == wraps.Count - 1 ? endB : wraps[i + 1].Position;

                Vector3 bend = rail.BendFor(before, after);

                // Slid off the end: if a connected slot's mouth is here, keep walking onto it.
                if (rail.AtEnd(bend))
                {
                    LeashRail next = rail.HandOverAt(bend);
                    if (next != null)
                    {
                        rail = next;
                        bend = rail.BendFor(before, after);
                    }
                }

                wraps[i] = new LeashWrap(bend, wrap.Normal, wrap.Surface, rail);
            }
        }

        /// <summary>
        /// A wrap dies when its two neighbours can see each other.
        ///
        /// <para>
        /// The textbook test is whether the turn at the waypoint has reversed sign, which is well
        /// behaved for a rope in a plane and unreliable against arbitrary 3D meshes — the turn
        /// direction at a bend over a curved surface is ambiguous, and a waypoint that cannot decide
        /// sticks forever. Line of sight is one cast, is obviously correct, and cannot get stuck.
        /// </para>
        /// </summary>
        private void Unwrap(Vector3 endA, Vector3 endB, LeashCast cast)
        {
            int i = 0;

            while (i < wraps.Count)
            {
                Vector3 before = i == 0 ? endA : wraps[i - 1].Position;
                Vector3 after = i == wraps.Count - 1 ? endB : wraps[i + 1].Position;

                if (cast(before, after, out _, out _, out _))
                {
                    i++;
                    continue;
                }

                wraps.RemoveAt(i);

                // Removing this one may have freed the one before it, which has already been passed.
                if (i > 0) i--;
            }
        }

        /// <summary>
        /// At most one new bend per end per step, and never past the cap.
        ///
        /// <para>
        /// One per end per step rather than looping until clear: a rope dragged hard into a corner
        /// can otherwise insert its whole budget in a single frame, and every one of those bends is
        /// measured against endpoint positions that a physics step has not yet reacted to.
        /// </para>
        /// </summary>
        private void Wrap(Vector3 endA, Vector3 endB, LeashCast cast, in Tuning tuning)
        {
            if (wraps.Count >= tuning.maxWraps) return;

            Vector3 neighbourOfA = wraps.Count == 0 ? endB : wraps[0].Position;

            if (cast(endA, neighbourOfA, out Vector3 point, out Vector3 normal, out Collider surface)
                && TryMake(point, normal, surface, endA, neighbourOfA, tuning, out LeashWrap madeAtA))
                wraps.Insert(0, madeAtA);

            if (wraps.Count >= tuning.maxWraps) return;

            Vector3 neighbourOfB = wraps.Count == 0 ? endA : wraps[wraps.Count - 1].Position;

            if (cast(endB, neighbourOfB, out point, out normal, out surface)
                && TryMake(point, normal, surface, endB, neighbourOfB, tuning, out LeashWrap madeAtB))
                wraps.Add(madeAtB);
        }
    }

    /// <summary>
    /// The real world, as a <see cref="LeashCast"/>. Owns its buffer, so a per-step query allocates
    /// nothing.
    ///
    /// <para>
    /// Two rules here are inherited wholesale from the LeashGround probe this replaces, because both
    /// were learned the hard way and neither is obvious. A rope must not catch on the things it is
    /// TIED to, or a rope knotted to a creature's flank instantly wraps around the creature. And a
    /// NonAlloc buffer that fills up discards hits arbitrarily — including, sometimes, the wall you
    /// care about — so it is grown and re-cast rather than trusted when full.
    /// </para>
    /// <para>
    /// The mask must exclude dynamic bodies. Every machine derives a rope's shape independently from
    /// replicated endpoints and nothing about that shape is ever sent; two machines agree about where
    /// a wall is and do not agree about where a rolling barrel was forty milliseconds ago.
    /// </para>
    /// </summary>
    public sealed class LeashWorldCast
    {
        private const int MaxBuffer = 128;

        private readonly LayerMask mask;
        private readonly float radius;

        private RaycastHit[] buffer = new RaycastHit[16];

        private Transform endA;
        private Transform endB;

        public LeashWorldCast(LayerMask mask, float radius)
        {
            this.mask = mask;
            this.radius = Mathf.Max(0.005f, radius);
        }

        /// <summary>The two things this rope is tied to. Their own surfaces are not obstacles for it.</summary>
        public void Ignoring(Transform a, Transform b)
        {
            endA = a;
            endB = b;
        }

        public bool Cast(Vector3 from, Vector3 to,
                         out Vector3 point, out Vector3 normal, out Collider surface)
        {
            point = default;
            normal = default;
            surface = null;

            Vector3 delta = to - from;
            float distance = delta.magnitude;

            // A segment no longer than the probe itself has nothing to report: the sphere starts
            // already covering the far end.
            if (distance <= radius * 2f) return false;

            Vector3 direction = delta / distance;
            Vector3 origin = from + direction * radius;
            float reach = distance - radius * 2f;

            int count = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, reach, mask,
                                                   QueryTriggerInteraction.Ignore);

            while (count == buffer.Length && buffer.Length < MaxBuffer)
            {
                buffer = new RaycastHit[buffer.Length * 2];
                count = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, reach, mask,
                                                   QueryTriggerInteraction.Ignore);
            }

            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = buffer[i];

                if (hit.collider == null) continue;
                if (hit.distance >= nearest) continue;
                if (Under(hit.collider.transform, endA) || Under(hit.collider.transform, endB)) continue;

                // A sweep that starts already overlapping reports distance 0 and a zero normal. There
                // is no bend to be had from that, and offsetting along a zero normal would put the
                // waypoint inside the surface.
                if (hit.normal.sqrMagnitude < 0.5f) continue;

                nearest = hit.distance;
                point = hit.point;
                normal = hit.normal;
                surface = hit.collider;
            }

            return surface != null;
        }

        private static bool Under(Transform candidate, Transform root) =>
            root != null && candidate != null && candidate.IsChildOf(root);
    }
}
