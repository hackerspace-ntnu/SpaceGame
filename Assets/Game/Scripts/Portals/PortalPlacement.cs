// Deciding where an aperture can go, and where exactly it lands.
//
// A portal shot is not "put a quad at the hit point". The hit point is wherever
// the crosshair happened to be, which is usually near an edge, and an aperture
// hanging half off the end of a wall is worse than one that refuses to open —
// you can walk into the half with nothing behind it. So the aperture is FITTED:
// the surface is probed outward from the hit until its edges are found, and the
// opening is slid inward until it sits wholly on the wall, or the shot fizzles.
//
// This runs on the shooter's machine only. The result travels in the use
// message as a position and rotation, and every other machine applies it
// verbatim rather than re-deriving it — two machines probing slightly different
// physics states and disagreeing about where a portal is would be a far worse
// bug than one machine getting it slightly wrong.
using UnityEngine;

namespace SpaceGame.Portals
{
    /// <summary>Put this on anything a portal must not stick to — glass, grates, moving platforms.</summary>
    [AddComponentMenu("SpaceGame/Portals/Non-Portalable Surface")]
    public sealed class NonPortalable : MonoBehaviour { }

    public static class PortalPlacement
    {
        /// <summary>The outcome of a shot. <see cref="Valid"/> false means the aperture does not open.</summary>
        public readonly struct Result
        {
            public readonly bool Valid;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Collider Surface;
            public readonly string Reason;

            private Result(bool valid, Vector3 position, Quaternion rotation,
                           Collider surface, string reason)
            {
                Valid = valid;
                Position = position;
                Rotation = rotation;
                Surface = surface;
                Reason = reason;
            }

            public static Result Ok(Vector3 p, Quaternion r, Collider c) =>
                new Result(true, p, r, c, null);

            public static Result Fail(Vector3 p, string reason) =>
                new Result(false, p, Quaternion.identity, null, reason);
        }

        /// <summary>How far the aperture is floated off the wall, so it does not z-fight with it.</summary>
        private const float SurfaceOffset = 0.012f;

        /// <summary>How far above the surface the edge probes start, and twice how far they reach.</summary>
        private const float ProbeHeight = 0.25f;

        /// <summary>
        /// How coplanar a probe hit must be to count as the same wall.
        ///
        /// Generous on purpose. A tight tolerance means an aperture only opens
        /// on flat authored geometry, and refuses on terrain, on rock, on a
        /// slightly bowed hull plate — everything a real level is actually made
        /// of. Anything within a hand's width of the plane is close enough for
        /// the traversal maths, which only ever uses the plane itself.
        /// </summary>
        private const float CoplanarTolerance = 0.22f;

        /// <summary>
        /// How far off-normal a probe hit may face and still count.
        ///
        /// 0.86 was about 30 degrees, which rejected any curved or bumpy
        /// surface. 0.55 is about 57 degrees: enough to still reject the far
        /// side of a thin wall, which is what this test is really for.
        /// </summary>
        private const float NormalTolerance = 0.55f;

        /// <summary>Steps used to walk outward looking for an edge. More is finer, and linear in cost.</summary>
        private const int EdgeSteps = 14;

        /// <summary>
        /// Fit an aperture of <paramref name="size"/> onto whatever <paramref name="hit"/> struck.
        ///
        /// <paramref name="viewForward"/> only matters on floors and ceilings, where "up" on the
        /// wall is undefined: there the aperture is rolled to face the shooter, so stepping into a
        /// portal in the floor does not drop you out sideways.
        /// </summary>
        public static Result Fit(RaycastHit hit, Vector2 size, Vector3 viewForward,
                                 LayerMask surfaceMask, bool requireFullSupport = false,
                                 bool allowMovingSurfaces = true)
        {
            Collider surface = hit.collider;
            if (surface == null) return Result.Fail(hit.point, "nothing there");

            if ((surfaceMask.value & (1 << surface.gameObject.layer)) == 0)
                return Result.Fail(hit.point, "surface is not portalable");

            // The one hard refusal, and it is opt-in: a designer says no.
            if (surface.GetComponentInParent<NonPortalable>() != null)
                return Result.Fail(hit.point, "surface refuses portals");

            // A portal on something that moves drags its own transfer matrix
            // around under whoever is standing in it, so the view and the exit
            // disagree for as long as it is moving. Allowed by default anyway:
            // "it can be placed on anything" is worth more than the handful of
            // frames it looks wrong on a moving crate, and the alternative is a
            // shot that silently does nothing.
            if (!allowMovingSurfaces &&
                surface.attachedRigidbody != null && !surface.attachedRigidbody.isKinematic)
                return Result.Fail(hit.point, "surface is not fixed");

            Vector3 normal = hit.normal;
            Quaternion rotation = Orientation(normal, viewForward);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            Vector3 centre = hit.point;
            Vector3 planePoint = hit.point;

            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;

            // Two passes of sliding the aperture off any edge it hangs over.
            // The second pass catches the case where fixing one axis walked it
            // over an edge on the other — an inside corner.
            //
            // A failure here is NOT a refusal. It means the surface is smaller
            // than the aperture, or lumpier than the probes like, and the
            // aperture is placed where it was aimed instead of not at all.
            // Refusing was the old behaviour and it made the gun feel broken:
            // most of a real level is too small, too curved or too cluttered to
            // pass a strict fit, and every one of those shots simply fizzled
            // with nothing on screen to say why.
            bool fitted = true;

            for (int pass = 0; pass < 2; pass++)
            {
                fitted &= Slide(ref centre, right, halfWidth, normal, planePoint, surfaceMask);
                fitted &= Slide(ref centre, up, halfHeight, normal, planePoint, surfaceMask);
            }

            // The rim check works where the axis-by-axis slide cannot see a
            // diagonal notch. Also advisory unless the caller insists.
            if (fitted)
                fitted = RimIsSupported(centre, right, up, halfWidth, halfHeight,
                                        normal, planePoint, surfaceMask);

            if (!fitted)
            {
                if (requireFullSupport)
                    return Result.Fail(hit.point, "surface cannot hold the whole aperture");

                // Best effort: back at the point actually aimed at, since the
                // sliding may have walked the centre somewhere worse than where
                // it started while chasing an edge that was never there.
                centre = hit.point;
            }

            return Result.Ok(centre + normal * SurfaceOffset, rotation, surface);
        }

        /// <summary>
        /// Which way is up on this wall.
        ///
        /// Vertical surfaces take world up, so apertures on walls are never
        /// tilted. Floors and ceilings have no meaningful up, so they take the
        /// shooter's own facing — which is what makes a floor portal put you
        /// down the way you were already looking.
        /// </summary>
        private static Quaternion Orientation(Vector3 normal, Vector3 viewForward)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f
                ? -viewForward
                : Vector3.up;

            Vector3 up = Vector3.ProjectOnPlane(reference, normal);
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.ProjectOnPlane(Vector3.right, normal);

            return Quaternion.LookRotation(normal, up.normalized);
        }

        /// <summary>
        /// Move <paramref name="centre"/> along one axis until the aperture's half-extent fits on
        /// both sides, or report that it never will.
        /// </summary>
        private static bool Slide(ref Vector3 centre, Vector3 axis, float halfExtent,
                                  Vector3 normal, Vector3 planePoint, LayerMask mask)
        {
            float positive = EdgeDistance(centre, axis, halfExtent, normal, planePoint, mask);
            float negative = EdgeDistance(centre, -axis, halfExtent, normal, planePoint, mask);

            // Not enough wall in total, wherever it is put.
            if (positive + negative < halfExtent * 2f) return false;

            if (positive < halfExtent) centre -= axis * (halfExtent - positive);
            else if (negative < halfExtent) centre += axis * (halfExtent - negative);

            return true;
        }

        /// <summary>
        /// How far the surface continues from <paramref name="from"/> along <paramref name="axis"/>,
        /// stopping at <paramref name="limit"/> since nothing beyond it changes the answer.
        /// </summary>
        private static float EdgeDistance(Vector3 from, Vector3 axis, float limit,
                                          Vector3 normal, Vector3 planePoint, LayerMask mask)
        {
            float step = limit / EdgeSteps;
            float reached = 0f;

            for (int i = 1; i <= EdgeSteps; i++)
            {
                float distance = step * i;
                if (!Supported(from + axis * distance, normal, planePoint, mask))
                    return reached;

                reached = distance;
            }

            return limit;
        }

        private static bool RimIsSupported(Vector3 centre, Vector3 right, Vector3 up,
                                           float halfWidth, float halfHeight,
                                           Vector3 normal, Vector3 planePoint, LayerMask mask)
        {
            const int samples = 12;

            for (int i = 0; i < samples; i++)
            {
                float angle = Mathf.PI * 2f * i / samples;
                Vector3 point = centre
                              + right * (Mathf.Cos(angle) * halfWidth * 0.98f)
                              + up * (Mathf.Sin(angle) * halfHeight * 0.98f);

                if (!Supported(point, normal, planePoint, mask)) return false;
            }

            return true;
        }

        /// <summary>
        /// Is there wall directly behind <paramref name="point"/>, in the same plane?
        ///
        /// Deliberately not "did we hit the same collider". A real wall is
        /// usually several colliders — a modular kit, a mesh split by chunk, a
        /// prop flush against it — and requiring one collider makes apertures
        /// refuse to open across perfectly ordinary seams. Coplanarity is what
        /// actually matters to the traversal maths.
        /// </summary>
        private static bool Supported(Vector3 point, Vector3 normal, Vector3 planePoint,
                                      LayerMask mask)
        {
            Vector3 origin = point + normal * ProbeHeight;

            if (!Physics.Raycast(origin, -normal, out RaycastHit hit, ProbeHeight * 2f,
                                 mask, QueryTriggerInteraction.Ignore))
                return false;

            if (hit.collider.GetComponentInParent<NonPortalable>() != null) return false;

            // Same plane, and facing the same way. The dot check rejects the far
            // side of a thin wall, which is coplanar-ish but points backwards.
            if (Vector3.Dot(hit.normal, normal) < NormalTolerance) return false;

            float offPlane = Mathf.Abs(Vector3.Dot(hit.point - planePoint, normal));
            return offPlane <= CoplanarTolerance;
        }
    }
}
