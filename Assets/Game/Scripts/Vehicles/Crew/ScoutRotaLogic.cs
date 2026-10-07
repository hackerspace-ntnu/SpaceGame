// The decisions a walking city makes about its monowheel scouts, with nothing in them but numbers:
// who rides out next, where the loop goes, and when a sweep is over. ScoutRota feeds it what it
// counts and does what it says; this file never touches a scene.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    /// <summary>One scout as the rota sees it on a single tick.</summary>
    public struct ScoutRecord
    {
        /// <summary>The wheel is alive and has a rider at its tiller: a scout that can ride out.</summary>
        public bool Alive;
        /// <summary>On a sweep right now.</summary>
        public bool Out;
        /// <summary>
        /// Its rider has a target: a scout in a fight is not sent (it stays with the column until the
        /// fight is over). One already on a sweep whose rider picks a target keeps riding the loop.
        /// </summary>
        public bool Fighting;
        /// <summary>Seconds since it last came home (or since the rota first saw it).</summary>
        public float HomeFor;
    }

    public static class ScoutRotaLogic
    {
        /// <summary>
        /// The scouts to send now: the living, idle ones home longest, enough to bring the number out
        /// up to <paramref name="wantOut"/>. Fewer when fewer are free; none when enough are out.
        /// </summary>
        public static List<int> PickNext(IReadOnlyList<ScoutRecord> scouts, int wantOut)
        {
            var picked = new List<int>();
            if (scouts == null) return picked;

            int alreadyOut = 0;
            var free = new List<int>();
            for (int i = 0; i < scouts.Count; i++)
            {
                ScoutRecord scout = scouts[i];
                if (scout.Out) alreadyOut++;
                else if (scout.Alive && !scout.Fighting) free.Add(i);
            }

            int wanted = wantOut - alreadyOut;
            if (wanted <= 0) return picked;

            // Longest home first; ties (a fresh city) go out in the order they are listed.
            free.Sort((a, b) =>
            {
                int byTimeHome = scouts[b].HomeFor.CompareTo(scouts[a].HomeFor);
                return byTimeHome != 0 ? byTimeHome : a.CompareTo(b);
            });

            for (int i = 0; i < free.Count && picked.Count < wanted; i++)
                picked.Add(free[i]);
            return picked;
        }

        /// <summary>
        /// <paramref name="points"/> waypoints evenly round a flat ring of <paramref name="radius"/>
        /// about <paramref name="centre"/>, at the centre's height, the first on the bearing
        /// <paramref name="startAngleDeg"/> (0 = +Z, clockwise seen from above).
        /// </summary>
        public static Vector3[] SweepPoints(Vector3 centre, float radius, int points, float startAngleDeg)
        {
            if (points <= 0) return System.Array.Empty<Vector3>();

            var ring = new Vector3[points];
            float step = 360f / points;
            for (int i = 0; i < points; i++)
            {
                float bearing = (startAngleDeg + i * step) * Mathf.Deg2Rad;
                ring[i] = centre + new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * radius;
            }
            return ring;
        }

        /// <summary>
        /// Waypoints a closed loop of <paramref name="points"/> reaches: each once, then the first
        /// again to close the ring (so the last chord is swept too).
        /// </summary>
        public static int LoopStops(int points) => points <= 0 ? 0 : points + 1;

        /// <summary>
        /// How far a scout rides on one sweep: out from the centre to the first waypoint, then round
        /// all <paramref name="points"/> chords of the closed loop back to it. What a sweep's timeout
        /// has to outlast.
        /// </summary>
        public static float LoopLength(float radius, int points)
        {
            if (points <= 0) return 0f;
            float chord = 2f * radius * Mathf.Sin(Mathf.PI / points);
            return radius + points * chord;
        }

        /// <summary>
        /// A sweep ends when the loop is ridden, or when it has run past <paramref name="timeout"/>:
        /// a waypoint off the NavMesh or behind a cliff must not keep a scout out for ever.
        /// </summary>
        public static bool SweepOver(float elapsed, float timeout, bool reachedLastPoint) =>
            reachedLastPoint || elapsed > timeout;

        /// <summary>
        /// A scout riding home from a sweep counts as away until it is within
        /// <paramref name="regroupDistance"/> of the leader -- where formation stops riding it straight
        /// back and puts it in its slot -- so the number away never exceeds the rota's.
        /// </summary>
        public static bool BackWithTheColumn(float distanceToLeader, float regroupDistance) =>
            distanceToLeader < regroupDistance;

        /// <summary>
        /// Where to ride for the waypoint at <paramref name="centre"/> + <paramref name="offset"/> while
        /// only streamed-in ground is safe to drive on: the waypoint itself when
        /// <paramref name="groundLoaded"/> says its ground is in, else the first point pulled in toward
        /// the centre by <paramref name="pullInStep"/> at a time whose ground is. False when nothing
        /// is, down to <paramref name="minRadius"/> from the centre. Flat: the offset's height is kept.
        /// </summary>
        public static bool TryPullIn(Vector3 centre, Vector3 offset, float pullInStep, float minRadius,
                                     System.Func<Vector3, bool> groundLoaded, out Vector3 point)
        {
            Vector3 flat = new Vector3(offset.x, 0f, offset.z);
            float radius = flat.magnitude;
            Vector3 outward = radius > Mathf.Epsilon ? flat / radius : Vector3.zero;
            Vector3 lift = Vector3.up * offset.y;

            if (pullInStep > 0f)
            {
                for (float along = radius; along >= minRadius; along -= pullInStep)
                {
                    point = centre + outward * along + lift;
                    if (groundLoaded(point)) return true;
                }
            }
            else if (groundLoaded(centre + offset))
            {
                point = centre + offset;
                return true;
            }

            point = centre;
            return false;
        }
    }
}
