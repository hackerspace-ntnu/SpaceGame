using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Every rope in the session that something could cut, and the one query that cuts them.
    ///
    /// <para>
    /// <b>Why a registry rather than colliders.</b> No rope in this game has a collider: a leash is
    /// a derived polyline redrawn every frame, a lasso is a Verlet cable, a grappling hook's cable
    /// is two points and a sag. Giving them colliders to be raycast would mean maintaining a chain
    /// of them at physics rate for three systems, and it would put ropes in front of every other
    /// query in the game — the beam's own self-skip loop, the grapple's aim, gunfire — where none
    /// of them belong. So the cutter brings its own segment and asks the ropes directly.
    /// </para>
    /// <para>
    /// <b>Server only.</b> <see cref="CutAlong"/> is called on the machine that already decides what
    /// the cutting thing does to the world, and each rope's <see cref="ICuttableRope.Cut"/>
    /// announces the parting on the channel that rope already owns. A cut judged on every machine
    /// would be three verdicts on three slightly different rope shapes, which is the same trap the
    /// leash's break verdict was moved out of once already.
    /// </para>
    /// </summary>
    public static class CuttableRopes
    {
        private static readonly List<ICuttableRope> Live = new();

        /// <summary>Scratch for one rope's shape. Cleared per rope, never held across a call.</summary>
        private static readonly List<Vector3> Points = new();

        /// <summary>
        /// The ropes a call has decided to part, gathered before any of them is cut.
        ///
        /// <para>
        /// Cutting mutates <see cref="Live"/>: a leash's <c>Snap</c> destroys its GameObject and the
        /// <c>OnDisable</c> that follows unregisters it, inside the loop that found it. Deciding
        /// first and cutting second is what keeps that out of the enumeration.
        /// </para>
        /// </summary>
        private static readonly List<ICuttableRope> Victims = new();

        public static IReadOnlyList<ICuttableRope> All => Live;

        public static void Register(ICuttableRope rope)
        {
            if (rope == null || Live.Contains(rope)) return;
            Live.Add(rope);
        }

        public static void Unregister(ICuttableRope rope) => Live.Remove(rope);

        /// <summary>
        /// Part every rope passing within <paramref name="radius"/> of the segment
        /// <paramref name="from"/> → <paramref name="to"/>, and say how many that was.
        ///
        /// <para>
        /// The segment is the cut itself, so a caller passes what it actually reaches: the laser
        /// staff passes muzzle to the point its beam stopped at, which means a wall between the
        /// staff and a rope protects that rope with no line-of-sight test written here.
        /// </para>
        /// </summary>
        public static int CutAlong(Vector3 from, Vector3 to, float radius)
        {
            if (Live.Count == 0) return 0;

            float radiusSq = radius * radius;

            Victims.Clear();

            for (int i = 0; i < Live.Count; i++)
            {
                ICuttableRope rope = Live[i];
                if (rope == null) continue;

                Points.Clear();
                rope.AppendSpan(Points);

                // Fewer than two points is a rope that is not out. See ICuttableRope.AppendSpan.
                for (int p = 0; p + 1 < Points.Count; p++)
                {
                    if (SegmentDistanceSq(from, to, Points[p], Points[p + 1]) > radiusSq) continue;

                    Victims.Add(rope);
                    break;
                }
            }

            for (int i = 0; i < Victims.Count; i++) Victims[i].Cut();

            return Victims.Count;
        }

        /// <summary>
        /// Squared distance between segment <paramref name="p1"/>→<paramref name="q1"/> and segment
        /// <paramref name="p2"/>→<paramref name="q2"/>.
        ///
        /// <para>
        /// Ericson's closest-point-between-two-segments, squared because nothing here needs the
        /// root. Written out rather than borrowed: the one other segment routine in the codebase
        /// (<c>CaveSdfField.ClosestPointOnSegmentXZ</c>) is deliberately horizontal-only, and a
        /// rope hanging above or below a beam is exactly the case it would get wrong.
        /// </para>
        /// <para>
        /// The degenerate branches are not defensive padding. A rope drawn as two coincident points
        /// is what every one of these systems produces for a frame or two while it is being built or
        /// torn down, and an unguarded division there is a NaN distance, which compares false
        /// against every threshold and so fails silently rather than loudly.
        /// </para>
        /// </summary>
        public static float SegmentDistanceSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            const float Epsilon = 1e-8f;

            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;

            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            float s;
            float t;

            if (a <= Epsilon && e <= Epsilon) return r.sqrMagnitude;

            if (a <= Epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);

                if (e <= Epsilon)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;

                    // Zero when the two segments are parallel, and then any point on the first will
                    // do — the clamps below pick the one that is actually nearest.
                    s = denom > Epsilon ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;

                    t = (b * s + f) / e;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            return ((p1 + d1 * s) - (p2 + d2 * t)).sqrMagnitude;
        }
    }
}
