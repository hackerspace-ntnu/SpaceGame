using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything the net is not allowed to pass through.
    ///
    /// <para>
    /// Separated from <see cref="SnareLattice"/> because they fail differently and are tuned
    /// differently: the lattice is a solver whose bugs look like bad cloth, and this is a query
    /// budget whose bugs look like a stutter. Keeping the solver free of raycasts also keeps it
    /// testable without a scene, which is why every test in Tasks 1-4 runs with no GameObject at
    /// all.
    /// </para>
    /// <para>
    /// <b>Capsules, not colliders.</b> A captive is approximated by one capsule taken from its
    /// bounds rather than resolved against its real collider, because <c>Physics.ComputePenetration</c>
    /// against a skinned mesh 225 times a substep is not a budget that exists. A capsule is also
    /// simply more correct for the job: the net has to slide off shoulders and haunches, and the
    /// exact geometry of a horn is noise at mesh spacing.
    /// </para>
    /// <para>
    /// <b>Contacts, not clamps.</b> Both halves below hand the correction to
    /// <see cref="SnareLattice.PlaceOnSurface"/> rather than writing the position outright. A
    /// clamp that only moves the position gives the node the penetration depth as outward
    /// SPEED, so a net dropped on an animal takes an impulse off every node at once and jumps
    /// clear of it — and no amount of friction, weight or stiffness anywhere else can hold down
    /// a sheet that is being kicked. There is deliberately nothing here that gathers cord onto
    /// a body on purpose: a net wraps because its hem is weighted and the cord it lies on has
    /// friction, and a version of this that projected the nearby nodes onto the capsule instead
    /// drew a capsule.
    /// </para>
    /// </summary>
    public class SnareDrape
    {
        /// <summary>One captive's collision proxy, in world space.</summary>
        public struct Capsule
        {
            public Vector3 Bottom;
            public Vector3 Top;
            public float Radius;
        }

        /// <summary>
        /// Build a proxy from whatever collider a captive happens to have.
        ///
        /// Bounds rather than the collider's own type, because the creatures in this game are a
        /// mix of capsule, box and mesh colliders and several have none at all — the same reason
        /// <see cref="LassoTether.Mass"/> estimates from bounds rather than trusting a Rigidbody.
        /// </summary>
        public static Capsule ProxyFor(GameObject captive)
        {
            Bounds bounds;

            if (captive.TryGetComponent(out Collider collider))
            {
                bounds = collider.bounds;
            }
            else
            {
                Collider child = captive.GetComponentInChildren<Collider>();
                bounds = child != null
                    ? child.bounds
                    : new Bounds(captive.transform.position + Vector3.up, Vector3.one);
            }

            float radius = Mathf.Max(0.2f, Mathf.Min(bounds.extents.x, bounds.extents.z));

            return new Capsule
            {
                Bottom = new Vector3(bounds.center.x, bounds.min.y + radius, bounds.center.z),
                Top = new Vector3(bounds.center.x, bounds.max.y - radius, bounds.center.z),
                Radius = radius,
            };
        }

        /// <summary>
        /// Push every node out of every capsule, then off the ground.
        ///
        /// <para>
        /// <paramref name="groundHeight"/> is a single sampled height rather than a per-node
        /// raycast, because 225 raycasts per substep is not a budget that exists.
        /// <see cref="SnareCatch"/> re-samples it as the net drifts.
        /// </para>
        /// <para>
        /// What that costs, written down rather than discovered later: the floor is FLAT for the
        /// whole net. On level ground, and against the capsules, it is within a hand's width of the
        /// truth everywhere it matters. On a slope it is wrong by the rise across the net — a 6 m
        /// net on a 15 degree hillside is out by about 1.6 m corner to corner, so the uphill hem
        /// floats clear of the ground and the downhill hem sinks under it. Re-sampling at the
        /// centre keeps the error centred but cannot remove it; a net that has to lie convincingly
        /// on steep terrain needs a height per row, not one per net.
        /// </para>
        /// <para>
        /// This writes through <see cref="SnareLattice.Positions"/> in place, so anything
        /// non-finite reaching a node here propagates through the next constraint pass and erases
        /// the whole net within a frame. Every divide on the path below is guarded; what remains is
        /// a requirement on the CALLER, which must not hand in a capsule built from non-finite
        /// bounds. <see cref="ProxyFor"/> floors the radius for the same reason.
        /// </para>
        /// </summary>
        public void Resolve(SnareLattice lattice, IReadOnlyList<Capsule> captives, float groundHeight)
        {
            Vector3[] nodes = lattice.Positions;
            if (nodes == null) return;

            for (int i = 0; i < nodes.Length; i++)
            {
                for (int c = 0; c < captives.Count; c++)
                {
                    if (!Contact(nodes[i], captives[c], out Vector3 surface, out Vector3 normal))
                        continue;

                    // Through the lattice rather than by writing the position here, because a
                    // contact is not only a place — it is also what the node's velocity does next.
                    // See PlaceOnSurface: written straight into the array, the correction reads as
                    // outward speed and the net bounces off whatever it landed on.
                    lattice.PlaceOnSurface(i, surface, normal, lattice.BodyGrip);
                }

                if (nodes[i].y >= groundHeight) continue;

                // Friction zero here, and not because the sand is slippery: GripGround is the pass
                // that holds cord ALREADY lying on the floor, and it works on a contact band rather
                // than on penetration, so it catches resting nodes this one never sees. Applying it
                // in both places would take the same speed off twice.
                lattice.PlaceOnSurface(i, new Vector3(nodes[i].x, groundHeight, nodes[i].z),
                                       Vector3.up, friction: 0f);
            }
        }

        /// <summary>
        /// Where a node has ended up inside a capsule, and which way is out.
        ///
        /// Returns false when the node is clear, which is the common case by a wide margin — the
        /// early exit is what keeps this affordable at two hundred-odd nodes a substep.
        /// </summary>
        private static bool Contact(Vector3 node, Capsule capsule, out Vector3 surface, out Vector3 normal)
        {
            surface = node;
            normal = Vector3.up;

            Vector3 axis = capsule.Top - capsule.Bottom;
            float lengthSquared = axis.sqrMagnitude;

            float t = lengthSquared < 1e-6f
                ? 0f
                : Mathf.Clamp01(Vector3.Dot(node - capsule.Bottom, axis) / lengthSquared);

            Vector3 onAxis = capsule.Bottom + axis * t;
            Vector3 outward = node - onAxis;
            float distance = outward.magnitude;

            if (distance >= capsule.Radius) return false;

            // A node exactly on the axis has no direction of its own to be pushed along, so one is
            // chosen rather than dividing by zero and writing a NaN into the lattice.
            normal = distance < 1e-5f
                ? LateralTo(axis, lengthSquared)
                : outward / distance;

            surface = onAxis + normal * capsule.Radius;
            return true;
        }

        /// <summary>
        /// Some direction square across the capsule, for a node sitting exactly on its axis.
        ///
        /// <para>
        /// It has to be PERPENDICULAR to the axis, and that is the trap. Pushing such a node along
        /// <c>Vector3.up</c> — the obvious choice — slides it along an upright capsule's own axis
        /// and leaves it exactly as deep inside as it started: the one case this branch exists for
        /// becomes the one case it fails to fix, and the node stays swallowed until something else
        /// happens to nudge it off centre.
        /// </para>
        /// <para>
        /// Which perpendicular does not matter. A node dead on the spine has no preferred side, and
        /// the constraint passes settle which way the mesh actually falls within a substep. Only
        /// reached from the degenerate branch, so the square root is not on the common path.
        /// </para>
        /// </summary>
        private static Vector3 LateralTo(Vector3 axis, float lengthSquared)
        {
            // A capsule with coincident ends is a sphere, and every direction is already lateral.
            if (lengthSquared < 1e-6f) return Vector3.up;

            Vector3 axisDirection = axis / Mathf.Sqrt(lengthSquared);

            // The same guard Deploy uses on LookRotation: a reference parallel to the axis gives a
            // zero cross product, so pick the one that cannot be.
            Vector3 reference = Mathf.Abs(axisDirection.y) > 0.99f ? Vector3.forward : Vector3.up;

            return Vector3.Cross(axisDirection, reference).normalized;
        }
    }
}
