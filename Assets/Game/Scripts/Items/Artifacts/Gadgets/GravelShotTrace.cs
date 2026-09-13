using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// Traces one gravel-blaster shot: seed in, one <see cref="Pellet"/> per piece of gravel out,
    /// each carrying where it went and what it met.
    ///
    /// <para>
    /// Extracted from the artifact because the AUTHORITY and every WATCHING machine must walk the
    /// same shot — the server to bill it, the peers to draw thirty streaks that end exactly where
    /// the damage landed. Two copies of this loop would drift, and the drift would be invisible:
    /// the spray would keep looking right while it stopped agreeing with the hits.
    /// </para>
    /// <para>
    /// Fills a caller-owned list rather than returning one, because a shot is fired from inside
    /// gameplay and this runs on every machine watching.
    /// </para>
    /// </summary>
    public static class GravelShotTrace
    {
        /// <summary>One piece of gravel: the line it flew, and the thing it stopped against.</summary>
        public readonly struct Pellet
        {
            /// <summary>Unit direction out of the muzzle.</summary>
            public readonly Vector3 Direction;

            /// <summary>Where it stopped — the surface it struck, or the end of its range.</summary>
            public readonly Vector3 Point;

            /// <summary>Surface normal where it struck; the reverse of <see cref="Direction"/> on a miss.</summary>
            public readonly Vector3 Normal;

            /// <summary>Metres travelled. Drives both the damage falloff and the tracer's lifetime.</summary>
            public readonly float Distance;

            /// <summary>Did it hit anything at all?</summary>
            public readonly bool Hit;

            /// <summary>The collider struck, or null. Needed by <see cref="BlastPush"/> for its Rigidbody.</summary>
            public readonly Collider Collider;

            /// <summary>
            /// What takes the damage: the <see cref="HealthComponent"/>'s object where there is one,
            /// otherwise the collider's. Null on a miss.
            /// </summary>
            public readonly GameObject Target;

            /// <summary>Whether <see cref="Target"/> is a living thing, which the impact FX read.</summary>
            public readonly bool IsFlesh;

            public Pellet(Vector3 direction, Vector3 point, Vector3 normal, float distance,
                          bool hit, Collider collider, GameObject target, bool isFlesh)
            {
                Direction = direction;
                Point = point;
                Normal = normal;
                Distance = distance;
                Hit = hit;
                Collider = collider;
                Target = target;
                IsFlesh = isFlesh;
            }
        }

        /// <summary>
        /// Walk the shot the seed describes.
        /// </summary>
        /// <param name="holder">
        /// Excluded from the trace. The barrels start outside the holder's body but the aim ray
        /// starts at their camera, so without this the first thing every shot meets is the person
        /// firing it.
        /// </param>
        public static void Trace(Vector3 origin, Quaternion aim, int seed, int count,
                                 float spreadDeg, float range, LayerMask mask, Transform holder,
                                 List<Pellet> into)
        {
            if (into == null) return;
            into.Clear();

            foreach (Vector3 dir in GravelBlastMath.PelletDirections(seed, aim, count, spreadDeg))
            {
                if (!Physics.Raycast(origin, dir, out RaycastHit hit, range, mask,
                                     QueryTriggerInteraction.Ignore)
                    || (holder != null && hit.collider.transform.IsChildOf(holder)))
                {
                    into.Add(new Pellet(dir, origin + dir * range, -dir, range, false, null, null, false));
                    continue;
                }

                HealthComponent health = hit.collider.GetComponentInParent<HealthComponent>();
                into.Add(new Pellet(dir, hit.point, hit.normal, hit.distance, true, hit.collider,
                                    health != null ? health.gameObject : hit.collider.gameObject,
                                    health != null));
            }
        }
    }
}
