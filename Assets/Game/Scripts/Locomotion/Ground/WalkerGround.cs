// Finding the ground under a walking machine.
//
// Three questions, and every walker asks all of them: what is directly below this point, what
// surface will a whole SOLE come to rest on there, and what is the tallest thing a step has to
// clear. None of the answers depend on how many legs the machine has, so none of them belong in a
// locomotion component -- they were duplicated almost verbatim between the ostrich and the walking
// station, which is two places for the same bug to hide, and the bug duly hid in both.
//
// That bug: the old sole query paired the centre ray's x/z with the HIGHEST neighbour's y and
// called the result a contact point. It lies on no surface at all, which is why feet were seen
// hovering on one side of a slope and buried on the other. The fit now lives in WalkerSurface,
// where it can be tested without a physics scene, and this class does nothing but cast the rays.
//
// The other subtlety is that a walker is wrapped in its own colliders, so a plain masked raycast
// plants its feet on itself. Every ray here rejects hits on the machine casting it.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// Built once from the machine's own settings, then queried per frame. Owns its buffers so the
    /// per-frame rays allocate nothing.
    public class WalkerGround
    {
        private readonly Transform self;
        private readonly LayerMask mask;
        private readonly float startAbove;
        private readonly float length;

        // A walking station carries better than thirty colliders of its own. RaycastNonAlloc does
        // not sort, so a buffer that fills up discards hits arbitrarily -- including, sometimes,
        // the ground. Sized to clear the machine with room to spare, and grown if that is ever
        // not enough, because the alternative is a leg silently concluding there is no floor.
        private RaycastHit[] buffer = new RaycastHit[32];

        /// Ceiling on the growth, so a pathological scene cannot make this allocate without bound.
        private const int MaxBuffer = 512;

        private readonly WalkerFootprintSample[] footprint = new WalkerFootprintSample[5];

        /// `startAbove` is how far above a query point a ray starts, so a foot already slightly
        /// below the surface still finds it; `length` is how far down it looks from there.
        public WalkerGround(Transform self, LayerMask mask, float startAbove, float length,
                            HashSet<Collider> excluded = null)
        {
            this.self = self;
            this.mask = mask;
            this.startAbove = startAbove;
            this.length = length;
            this.excluded = excluded;
        }

        /// Surfaces these probes must pretend are not there. Held by REFERENCE, not copied, so the
        /// owner can open and close a hole without rebuilding the sampler.
        ///
        /// It exists for one case and it is worth naming: a portal cuts an aperture into a wall and
        /// switches off the machine's COLLISION with it, which is all a physics body needs to walk
        /// through. A legged machine needs more, because it does not push against the world -- it
        /// asks the world where it is allowed to put a foot, by casting rays, and
        /// `Physics.IgnoreCollision` has no effect whatsoever on a raycast. So the wall stayed
        /// solid to every probe: the climb gate read the far side of the aperture as a cliff and
        /// refused to travel into it, and the machine stopped dead at the rim of a hole it was
        /// entitled to walk through.
        private readonly HashSet<Collider> excluded;

        /// Nearest ground below `origin`, never the machine itself.
        public bool Ray(Vector3 origin, float distance, out RaycastHit best)
        {
            best = default;
            if (!(distance > 0f)) return false;         // also rejects NaN

            int n = Physics.RaycastNonAlloc(origin, Vector3.down, buffer, distance,
                                            mask, QueryTriggerInteraction.Ignore);

            // A full buffer does not mean "that was all of them". RaycastNonAlloc stops filling
            // once it is out of room and does not sort, so whichever hits it dropped are arbitrary
            // -- and a ray fired down through a machine wearing thirty of its own colliders drops
            // the ground about as often as anything else. Grow and re-cast rather than let a leg
            // conclude there is no floor here.
            while (n >= buffer.Length && buffer.Length < MaxBuffer)
            {
                buffer = new RaycastHit[buffer.Length * 2];
                n = Physics.RaycastNonAlloc(origin, Vector3.down, buffer, distance,
                                            mask, QueryTriggerInteraction.Ignore);
            }

            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Collider col = buffer[i].collider;
                if (col == null || col.transform.IsChildOf(self)) continue;
                if (IsLooseBody(col)) continue;
                if (excluded != null && excluded.Count > 0 && excluded.Contains(col)) continue;
                if (buffer[i].distance >= nearest) continue;
                nearest = buffer[i].distance;
                best = buffer[i];
                found = true;
            }
            return found;
        }

        /// Whether a hit belongs to something standing on the world rather than being the world.
        ///
        /// Rejecting only the machine's OWN colliders is not enough, because the machine also
        /// CARRIES things. A player standing on the deck is not parented to the walker -- it is
        /// moved along by WalkerPlatformCarrier -- so the body's height probe found the rider's
        /// capsule, called it the ground, and set the body to "rider + ride height". The deck went
        /// up, the carrier took the rider up with it, and the probe found them higher still: the
        /// machine climbed into the sky at a steady rate for as long as somebody stood under the
        /// probe. Only under the probe, which is one central ray, which is why it happened when
        /// the player stood in the MIDDLE of the deck and nowhere else.
        ///
        /// The rule is the general one rather than a test for the player specifically: ground is
        /// terrain and buildings, which are static colliders. Anything under its own physics is
        /// cargo, and a walking machine does not stand on its cargo. A mounted rider is made
        /// kinematic AND parented to the mount, so this does not reject the rider of the machine
        /// casting the ray -- that case is already covered by the IsChildOf test above.
        private static bool IsLooseBody(Collider col)
        {
            Rigidbody rb = col.attachedRigidbody;
            return rb != null && !rb.isKinematic;
        }

        /// Ground below `at`, using the configured start height and length.
        public bool Below(Vector3 at, out RaycastHit hit)
            => Ray(at + Vector3.up * startAbove, length, out hit);

        /// Ground below `at`, from a ray started clear of both `at` itself and `clearAbove`.
        ///
        /// Callers pass their leg's hip. This used to CAP the origin at that height, on the
        /// reasoning that starting below the deck keeps the hull out of the ray's path -- and it
        /// was exactly backwards in the one case that matters. A machine stepping UP probes ground
        /// that is higher than the point it is standing at, and often higher than its own hips; the
        /// cap then put the ray's origin inside the terrain, a downward ray from inside a mesh
        /// reports nothing, and the leg concluded there was no ground there at all. Every candidate
        /// on the rise was rejected, the leg was stranded, and the walker refused to advance -- it
        /// went blind precisely when it was trying to climb.
        ///
        /// So the hip is a FLOOR on the origin now, never a ceiling, and the hull is kept out of
        /// the answer the only way that is actually sound: `Ray` discards hits on this machine.
        public bool BelowUnder(Vector3 at, float clearAbove, out RaycastHit hit)
        {
            // A caller with no opinion passes something not usefully above `at` -- negative
            // infinity, a NaN, or simply a hip below the ground being probed. All of them fall
            // through to `at` itself, which is the floor that actually matters.
            bool useHint = clearAbove > at.y && clearAbove < float.MaxValue;
            float originY = (useHint ? clearAbove : at.y) + startAbove;
            var origin = new Vector3(at.x, originY, at.z);

            // Always reach at least `length` BELOW the probe point, whatever the origin was lifted
            // to, so raising the start never shortens how deep the query can see.
            return Ray(origin, length + (originY - at.y), out hit);
        }

        /// The surface a whole sole will come to rest on, sampled across the footprint rather than
        /// at one point. A single ray makes the foot pivot on whatever one spot it happened to hit,
        /// so a sole this size sinks a corner whenever that spot is not representative, and the
        /// normal snaps as the ray crosses an edge.
        public bool TrySurface(Vector3 at, float footprintRadius, float clearAbove,
                               out WalkerSurface surface)
        {
            surface = default;
            if (!BelowUnder(at, clearAbove, out RaycastHit centre)) return false;

            footprint[0] = new WalkerFootprintSample { Point = centre.point, Normal = centre.normal };
            int found = 1;

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * footprintRadius;
                if (!BelowUnder(at + offset, clearAbove, out RaycastHit hit)) continue;
                footprint[found++] = new WalkerFootprintSample { Point = hit.point, Normal = hit.normal };
            }

            return WalkerSurface.TryFit(at, footprint, found, out surface);
        }

        /// Older two-out form, kept for the ostrich. Inherits the supporting-plane fix.
        public bool Sample(Vector3 at, float footprintRadius, out Vector3 point, out Vector3 normal)
        {
            point = at;
            normal = Vector3.up;
            if (!TrySurface(at, footprintRadius, float.NegativeInfinity,
                            out WalkerSurface surface)) return false;

            point = surface.Point;
            normal = surface.Normal;
            return true;
        }

        /// Ground heights along a run out from `from`, for asking how steep the way ahead is.
        ///
        /// Distinct from `HighestAlong` because the two want different answers from the same rays:
        /// a swing wants the single tallest thing between two footholds, and a climb test wants the
        /// PROFILE -- where the rise sits along the run is the whole difference between a hillside
        /// and a wall.
        ///
        /// `lift` is how far above `from` the rays start, and it has to clear everything the machine
        /// might legitimately walk up: a ray started below the ground it is probing is fired from
        /// inside the mesh and reports nothing, which is the failure BelowUnder's comment already
        /// documents from the other end. Ground standing higher than the lift comes back as not
        /// found, and not found is not a refusal -- so the caller sizes this generously.
        ///
        /// Fills exactly `into.Length` samples, evenly spaced, the last one at `run`.
        public void SampleAlong(Vector3 from, Vector3 direction, float run, float lift,
                                WalkerClimb.Sample[] into)
        {
            if (into == null || into.Length == 0) return;

            float originY = from.y + Mathf.Max(lift, 0f);

            for (int i = 0; i < into.Length; i++)
            {
                float distance = run * (i + 1) / into.Length;
                Vector3 at = from + direction * distance;

                // Reaches at least `length` below the probe point however far the origin was
                // lifted, so a tall lift never shortens how deep the query can see.
                var origin = new Vector3(at.x, originY, at.z);
                bool found = Ray(origin, length + (originY - at.y), out RaycastHit hit);

                into[i] = new WalkerClimb.Sample
                {
                    Distance = distance,
                    Height = found ? hit.point.y : 0f,
                    Found = found,
                };
            }
        }

        /// Highest ground anywhere along a step, so a swing can be lifted over what is actually
        /// under it rather than following a fixed arc through a rock. The ends are included, since
        /// they are where the foot has to leave from and arrive at.
        public float HighestAlong(Vector3 from, Vector3 to, int probes)
        {
            float highest = Mathf.Max(from.y, to.y);
            for (int i = 1; i < probes; i++)
            {
                Vector3 along = Vector3.Lerp(from, to, i / (float)probes);
                if (Below(along, out RaycastHit hit)) highest = Mathf.Max(highest, hit.point.y);
            }
            return highest;
        }
    }
}
