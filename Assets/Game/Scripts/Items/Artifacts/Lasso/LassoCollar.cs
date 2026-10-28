// The lasso's loop pulled shut on whatever it caught: one ring of rope on the skin, not a hoop in the air.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where a cinched lasso loop lies: one closed ring round the caught body's cross-section — a
    /// person's waist with their arms pinned to their sides, a creature's neck nearest to where the rope
    /// took hold — riding that bone so it follows the body as it fights.
    /// <see cref="LassoLoop.Ride(Vector3, Vector3, float, LassoCollar, Vector3)"/> closes the open loop onto it.
    ///
    /// <para>
    /// <b>Presentation on every machine, and holds nothing worth keeping.</b> Measured once, on each
    /// machine, from the catch every machine already hears (<c>LassoArtifact.Attach</c>); nothing in it
    /// is sent or saved, and a restored catch measures it again. The rope's physics still pull on the
    /// attach point, never on this ring.
    /// </para>
    /// <para>
    /// The measuring is the hogtie wrap's (<see cref="SkinCoil"/>), so the collar and the ropes round a
    /// tied body lie on the same skin the same way.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LassoCollar
    {
        [Tooltip("Metres of waist or neck the ring's cross-section is read over. Wide enough to find the " +
                 "outline, narrow enough not to take in the jaw, the shoulders or the hips.")]
        [SerializeField, Min(0.02f)] private float band = 0.12f;

        [Tooltip("How far one part of the cross-section may stand out past the rest, as a multiple of its middle " +
                 "radius, before the loop is let through it rather than pulled round it: a chin, an ear, a strap.")]
        [SerializeField, Range(1.1f, 3f)] private float looseLimit = 1.4f;

        [Tooltip("How far out the body lying against a creature's neck may reach, as a multiple of the neck's own " +
                 "reach that way, and still be pulled in under the loop.")]
        [SerializeField, Range(1f, 2f)] private float limbHug = 1.6f;

        [Tooltip("Where the loop sits on a creature's neck, from the bone it rides (0) to the next one along (1).")]
        [SerializeField, Range(0f, 1f)] private float alongBone = 0.5f;

        [Tooltip("Where the loop closes round a PERSON, from their hips (0) to their chest (1). About the elbows, " +
                 "so it goes round the waist and the arms hanging beside it.")]
        [SerializeField, Range(0f, 1f)] private float waistAlongTrunk = 0.45f;

        [Tooltip("How far out a person's arms may reach from their waist and still be pulled in under the loop, as " +
                 "a multiple of the waist's own reach that way. Arms at the sides are bound in; arms held up or out " +
                 "clear of the body are let out of it.")]
        [SerializeField, Range(1f, 3f)] private float armHug = 2f;

        private SkinCoil ring;

        /// <summary>Is there a measured ring to close onto? False before a catch, after a release, and on a body with nothing to measure.</summary>
        public bool IsFitted => ring != null;

        /// <summary>
        /// Measure the ring on <paramref name="body"/>, caught at <paramref name="caughtAt"/>, for a rope
        /// <paramref name="ropeWidth"/> thick. Once per catch; it rides the bone afterwards.
        /// </summary>
        public void Fit(Transform body, Vector3 caughtAt, float ropeWidth)
        {
            Clear();
            if (body == null) return;

            Transform[] bones = new List<Transform>(BodyFrame.BonesOf(body)).ToArray();
            if (bones.Length == 0) return;

            var surface = new BodySurface(body, bones);
            Animator person = BodyFrame.HumanoidOf(body);
            SkinCoil fitted = person != null ? Waist(surface, person, ropeWidth) : Neck(surface, caughtAt, ropeWidth);
            ring = fitted != null && fitted.Measured ? fitted : null;
        }

        /// <summary>The loop is off. Safe twice.</summary>
        public void Clear() => ring = null;

        /// <summary>The ring as it lies now, world space, first point repeated last. Nothing when not fitted.</summary>
        public void AddTo(List<Vector3> into) => ring?.AddTo(into);

        /// <summary>
        /// Round a person's waist and their arms with it: a lasso dropped over somebody pins their arms to
        /// their sides. The outline is the trunk at that height — what is weighted to the hips and spine,
        /// coat included — and the arms are taken in where they hang against it; an arm held out or up
        /// is not. Rides the spine, so it stays round the middle as they fight. Null on a rig missing a
        /// trunk bone.
        /// </summary>
        private SkinCoil Waist(BodySurface surface, Animator person, float ropeWidth)
        {
            Transform hips = person.GetBoneTransform(HumanBodyBones.Hips);
            Transform spine = person.GetBoneTransform(HumanBodyBones.Spine);
            Transform chest = person.GetBoneTransform(HumanBodyBones.Chest);
            Transform upperChest = person.GetBoneTransform(HumanBodyBones.UpperChest);
            Transform top = chest != null ? chest : upperChest;
            if (hips == null || spine == null || top == null) return null;

            Transform leftArm = person.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rightArm = person.GetBoneTransform(HumanBodyBones.RightUpperArm);

            System.Func<Transform, bool> trunk = owner => owner == hips || owner == spine || owner == chest || owner == upperChest;
            System.Func<Transform, bool> arms = owner => (leftArm != null && owner.IsChildOf(leftArm))
                                                        || (rightArm != null && owner.IsChildOf(rightArm));
            surface.Sample(new[] { trunk, arms });
            surface.Pose();

            return SkinCoil.Ring(surface, trunk, arms, spine, Vector3.Lerp(hips.position, top.position, waistAlongTrunk),
                                 top.position - hips.position, band, 0f, new SkinFit(ropeWidth, looseLimit, armHug));
        }

        /// <summary>
        /// Round a creature's neck, nearest to where the rope took hold. Round a NECK the outline is the
        /// neck and the bone above it, and the body below is pulled in only where it lies against them;
        /// nothing further away is read at all, because a catch is one frame and a big creature is hundreds
        /// of thousands of vertices. Round a creature with no neck bone, every bone at that height makes the
        /// outline — a flank or a belly is weighted to bones of its own, and two spine bones alone left the
        /// ring inside the barrel — and legs hanging clear of it are let through by the loose limit.
        /// </summary>
        private SkinCoil Neck(BodySurface surface, Vector3 caughtAt, float ropeWidth)
        {
            if (!NeckOf(surface.Skeleton, caughtAt, out Transform rides, out Transform toward, out bool neck)) return null;

            Transform below = HogtieWrap.NearestBoneAbove(rides, surface.Skeleton);
            System.Func<Transform, bool> own = neck ? owner => owner == rides || owner == toward : owner => true;
            System.Func<Transform, bool> lying = owner => neck && owner == below;
            surface.Sample(new[] { own, lying });
            surface.Pose();

            return SkinCoil.Ring(surface, own, lying, rides, Vector3.Lerp(rides.position, toward.position, alongBone),
                                 toward.position - rides.position, band, 0f, new SkinFit(ropeWidth, looseLimit, limbHug));
        }

        /// <summary>
        /// The bone the loop closes round on a creature, and the next one along it: its neck segment nearest
        /// to where the rope took hold, failing that the nearest segment.
        /// </summary>
        /// <param name="isNeck">Is it a neck, read as its own outline, rather than whatever bone was nearest?</param>
        private static bool NeckOf(Transform[] skeleton, Vector3 caughtAt, out Transform rides, out Transform toward,
                                   out bool isNeck)
        {
            // Every creature rig here names its neck (RigNeck1, neck2, Neck_07). The nearest bone alone put
            // an ostrich's loop round its shin and an Esko's round its barrel.
            isNeck = NearestSegment(skeleton, caughtAt, bone => bone.name.IndexOf("neck", System.StringComparison.OrdinalIgnoreCase) >= 0,
                                    out rides, out toward);
            return isNeck || NearestSegment(skeleton, caughtAt, bone => true, out rides, out toward);
        }

        /// <summary>The skeleton segment nearest <paramref name="point"/> whose upper bone passes <paramref name="upper"/>.</summary>
        private static bool NearestSegment(Transform[] skeleton, Vector3 point, System.Func<Transform, bool> upper,
                                           out Transform rides, out Transform toward)
        {
            rides = toward = null;
            float nearest = float.PositiveInfinity;
            foreach (Transform bone in skeleton)
            {
                Transform above = bone != null ? HogtieWrap.NearestBoneAbove(bone, skeleton) : null;
                if (above == null || !upper(above)) continue;

                Vector3 from = above.position, span = bone.position - from;
                float t = span.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - from, span) / span.sqrMagnitude) : 0f;
                float distance = (from + span * t - point).sqrMagnitude;
                if (distance >= nearest) continue;

                nearest = distance;
                rides = above;
                toward = bone;
            }

            return rides != null;
        }
    }
}
