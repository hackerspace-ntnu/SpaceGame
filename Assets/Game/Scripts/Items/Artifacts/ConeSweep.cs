// Which bodies a continuous artifact's cone is actually on.
//
// The flamethrower's jet and the cryo sprayer's plume ask the same question fifteen times a second:
// of everything standing in front of the muzzle, which bodies are inside the visible cone, in
// sight, and not the holder's own? They answered it in two copies until the sprayer stopped being
// a centre-line tool, and two copies of "what is the flame on" is exactly the pair that drifts —
// one gets an exclusion the other never hears about, and the two guns disagree about what a mount
// is. The geometry lives here; what a body then CATCHES stays with each gun.
//
// Everything in here is derived from the aim ray, which every machine already has off the hold
// stream, so a sweep run on the owner, on the server and on a peer reaches the same bodies without
// a message of its own.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>One body the cone is on, and how far off the centre line it stands.</summary>
    public readonly struct ConeBody
    {
        public ConeBody(StatusReceiver body, Collider collider, Vector3 point, float offAxisDegrees)
        {
            Body = body;
            Collider = collider;
            Point = point;
            OffAxisDegrees = offAxisDegrees;
        }

        /// <summary>The receiver the condition goes on — one per creature, not one per limb.</summary>
        public readonly StatusReceiver Body;

        /// <summary>The collider that put it in the cone: the one nearest the centre line.</summary>
        public readonly Collider Collider;

        /// <summary>Where the cone caught it, for the landing effects.</summary>
        public readonly Vector3 Point;

        /// <summary>Degrees off the aim. Zero is dead on the crosshair.</summary>
        public readonly float OffAxisDegrees;
    }

    /// <summary>
    /// The shared "what is standing in this cone" sweep for the continuous artifacts.
    ///
    /// <para>
    /// It resolves bodies through a caller-supplied rule rather than one of its own, because the
    /// two guns want different things put on what they touch — the flame adds a
    /// <c>BurningVisual</c>, the plume adds nothing — while agreeing completely about what counts
    /// as a body in the first place. Both rules end at <see cref="StatusReceiver.EnsureOnBody"/>.
    /// </para>
    /// </summary>
    public static class ConeSweep
    {
        /// <summary>
        /// The overlap buffer. Static because a sweep is synchronous and never nests: the results
        /// are copied out before <see cref="Bodies"/> returns, and no artifact sweeps from inside
        /// another artifact's sweep.
        /// </summary>
        private static readonly Collider[] Overlaps = new Collider[128];

        /// <summary>
        /// Fill <paramref name="results"/> with the bodies inside the cone — one entry per body,
        /// holder excluded, anything behind cover skipped.
        ///
        /// <para>
        /// The apex is the aim ray's own origin rather than a muzzle, for the reason the laser
        /// staff traces from there: the ray is the one thing every machine agrees on, while a
        /// muzzle is a bone on an animated arm each machine poses for itself. It also errs in the
        /// safe direction — a cone opening from the eye is narrower beside the player than one
        /// opening from the fist.
        /// </para>
        /// <para>
        /// One entry per body however many colliders it puts in the cone. A creature is a handful
        /// of them and the first one an overlap returns may be the one behind the rock, so the
        /// whole set is considered and the one closest to the centre line wins — which is also the
        /// one whose off-axis angle the caller should be scaling by.
        /// </para>
        /// </summary>
        /// <param name="ownerRoot">
        /// The holder's transform ROOT, and everything under it, is skipped. Mounting parents a
        /// rider under their mount, so the root IS the machine they are riding.
        /// </param>
        /// <param name="holder">The holder's own receiver, which never catches its own spray.</param>
        /// <param name="sightBlockers">
        /// What stops the cone reaching a body. Set to nothing to let it pass through walls, which
        /// is almost never wanted.
        /// </param>
        /// <param name="resolve">What counts as a body, and what is put on one — see the class note.</param>
        public static void Bodies(Vector3 origin, Vector3 direction, float range,
                                  float halfAngleDegrees, LayerMask mask, LayerMask sightBlockers,
                                  Transform ownerRoot, StatusReceiver holder,
                                  Func<GameObject, StatusReceiver> resolve, List<ConeBody> results)
        {
            if (results == null || resolve == null) return;

            results.Clear();

            int count = Physics.OverlapSphereNonAlloc(origin, range, Overlaps, mask,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (hit == null) continue;
                if (ownerRoot != null && hit.transform.IsChildOf(ownerRoot)) continue;

                Vector3 point = hit.bounds.center;
                if (!RepulsorBlast.InCone(origin, direction, point, range, halfAngleDegrees)) continue;

                StatusReceiver body = resolve(hit.gameObject);
                if (body == null || body == holder) continue;

                float offAxis = Vector3.Angle(direction, point - origin);

                int existing = IndexOf(results, body);
                if (existing >= 0 && results[existing].OffAxisDegrees <= offAxis) continue;

                // Asked only of a collider that is otherwise worth keeping: a line-of-sight raycast
                // per limb of every creature in an eighteen-metre sphere is the expensive half of
                // this loop.
                if (!Reaches(origin, point, body.transform, ownerRoot, sightBlockers)) continue;

                var found = new ConeBody(body, hit, point, offAxis);

                if (existing >= 0) results[existing] = found;
                else results.Add(found);
            }
        }

        /// <summary>
        /// Is there a clear line from the apex to <paramref name="point"/>?
        ///
        /// <para>
        /// The cone alone is a volume and knows nothing about what is standing in it, so without
        /// this it reaches whatever is on the far side of the rock the target is hiding behind. The
        /// body itself is what the ray meets whenever nothing else is in the way, and the holder's
        /// own body is what it meets first when the eye sits inside their head — neither of those
        /// is an obstruction.
        /// </para>
        /// </summary>
        private static bool Reaches(Vector3 origin, Vector3 point, Transform body,
                                    Transform ownerRoot, LayerMask sightBlockers)
        {
            if (sightBlockers.value == 0) return true;

            Vector3 to = point - origin;
            float distance = to.magnitude;
            if (distance < 1e-3f) return true;

            if (!Physics.Raycast(origin, to / distance, out RaycastHit blocker, distance,
                                 sightBlockers, QueryTriggerInteraction.Ignore))
                return true;

            Transform obstruction = blocker.collider.transform;
            return obstruction.IsChildOf(body) ||
                   (ownerRoot != null && obstruction.IsChildOf(ownerRoot));
        }

        /// <summary>
        /// Where <paramref name="body"/> already sits in the results, or -1. A linear scan rather
        /// than a set, because the list is the handful of bodies in one cone and the entry has to
        /// be REPLACED when a closer collider on the same body turns up.
        /// </summary>
        private static int IndexOf(List<ConeBody> results, StatusReceiver body)
        {
            for (int i = 0; i < results.Count; i++)
                if (results[i].Body == body) return i;

            return -1;
        }
    }
}
