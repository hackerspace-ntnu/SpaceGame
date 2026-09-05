// The shrinking ring a net closes around what it caught.
//
// Pure static geometry, deliberately: this is the one part of the wrap whose behaviour can be
// wrong in a way that looks like a tuning problem. A cinch that widens for one substep, or that
// pushes outward on a node already inside the ring, pumps energy into a cloth solver and reads as
// "the net is jittery" rather than as an ordering mistake — exactly the failure mode the Laplacian
// bend pass produced before it became a constraint. Kept out of SnareLattice so it can be proven
// with arithmetic and no scene.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where the net's cord is being pulled to while it closes around a body, and how hard.
    ///
    /// <para>
    /// <b>This is a target field, never a shape.</b> Nothing here moves a node onto a cylinder.
    /// <see cref="Correction"/> returns a pull toward a radius, which <see cref="SnareLattice"/>
    /// relaxes inside its own constraint loop alongside the strands, the shear and the bend. The
    /// strands are inextensible, so the cord's length has nowhere to go as the ring closes except
    /// into folds — the buckling, the slack between limbs and the hanging hem are the solver's
    /// answer, not an authored pose.
    /// </para>
    /// <para>
    /// This distinction is the whole reason the feature works. The version that projected cord
    /// straight onto a capsule was rejected on sight, and the note explaining why is one sentence
    /// long: projecting cord onto a capsule draws a capsule.
    /// </para>
    /// </summary>
    public static class SnareCinch
    {
        /// <summary>
        /// The line the net closes around: a point on the victim, and which way is up.
        ///
        /// <para>
        /// Unbounded. This models an infinite line, not a segment standing off the ground between
        /// two heights — a hem node lying on the sand three metres below the creature is pulled in
        /// exactly as hard as one at the torso, which is the purse-seine behaviour the design
        /// wants. Any height limit on where the pull applies is the caller's business, not this
        /// struct's.
        /// </para>
        /// </summary>
        public readonly struct Axis
        {
            public readonly Vector3 Origin;

            /// <summary>Unit. Normalised on construction so callers may hand over any up vector.</summary>
            public readonly Vector3 Direction;

            public Axis(Vector3 origin, Vector3 up)
            {
                Origin = origin;
                Direction = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
            }
        }

        /// <summary>
        /// The target radius this far into the cinch. A per-SUBSTEP quantity — call it once per
        /// substep to get the ring for that substep, never once per node; every node in a pass
        /// shares the same ring.
        ///
        /// <para>
        /// SmoothStep rather than a straight ramp, for the reason
        /// <see cref="SnareLattice.AdvanceUnfurl"/> gives about the unfurl: the ends are what read.
        /// A linear close starts and stops with a visible corner; eased ends look like a net
        /// gathering and then holding.
        /// </para>
        /// <para>
        /// Monotonic by construction, and that is load-bearing rather than tidy. A target that
        /// widened even for one substep would hand the cloth outward corrections it then has to
        /// take back, which is energy the solver did not have — and a net that visibly breathes.
        /// The invariant is enforced by clamping the target to never exceed the start, rather than
        /// by trusting callers to only ever ask for a smaller ring: a net that landed already
        /// tighter than the authored radius holds where it is instead of being handed a target
        /// that would open it back up.
        /// </para>
        /// </summary>
        public static float RadiusAt(float startRadius, float targetRadius, float elapsed, float duration)
        {
            float target = Mathf.Min(targetRadius, startRadius);

            if (duration <= 0f) return target;

            float t = Mathf.Clamp01(elapsed / duration);
            return Mathf.Lerp(startRadius, target, Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>
        /// How far one node has to move to reach the ring, scaled by this pass's stiffness.
        ///
        /// <para>
        /// <b>One-sided.</b> A node already inside the radius gets a zero correction. Pushing it
        /// back out would make this an inflation as well as a contraction, and the net would settle
        /// into an even tube standing off the body — which is a drawn capsule by another route.
        /// What the design wants is cord gathered in and then left to fold wherever the body and
        /// its own constraints put it.
        /// </para>
        /// <para>
        /// <b>Radial only.</b> The component along the axis is removed before the pull is measured,
        /// so a cinch never slides cord up or down the body. Without that a net closing on a
        /// standing figure walks its own hem up to the waist.
        /// </para>
        /// <para>
        /// Takes the axis by <c>in</c>: this is the one method here written for a tight loop — one
        /// call per node per substep, roughly 162k calls a second for a single net at full
        /// resolution — and <see cref="Axis"/> being a readonly struct means the reference costs
        /// nothing extra at every call site.
        /// </para>
        /// </summary>
        public static Vector3 Correction(Vector3 node, in Axis axis, float radius, float stiffness)
        {
            if (stiffness <= 0f) return Vector3.zero;

            Vector3 offset = node - axis.Origin;

            // Deliberately not Vector3.ProjectOnPlane: that normalises its plane normal before
            // using it, and axis.Direction is already unit by construction. Reusing ProjectOnPlane
            // here would buy back a normalisation this type worked to avoid, once per node per
            // constraint pass.
            Vector3 radial = offset - axis.Direction * Vector3.Dot(offset, axis.Direction);

            float distance = radial.magnitude;

            // A node sitting exactly on the axis has no radial direction to be pulled along.
            // Normalising it is a NaN, and one NaN reaches every node in the lattice within a
            // single constraint pass.
            if (distance <= 1e-5f || distance <= radius) return Vector3.zero;

            return radial * (-(distance - radius) / distance * stiffness);
        }
    }
}
