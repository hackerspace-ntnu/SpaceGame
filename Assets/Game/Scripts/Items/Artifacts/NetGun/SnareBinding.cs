// What a net's cord is nailed to once it has stopped solving.
//
// The saving this buys is the whole point of the wrap: a bound net costs one matrix multiply per
// node per frame against a solver that no longer runs at all, where the draped net it replaces ran
// ninety substeps a second for its full thirty-second life, three of them at a time per gun.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Frozen net cord, pinned to the bones of the body it closed around.
    ///
    /// <para>
    /// <b>One bone per node, not a weighted skin.</b> A ragdoll here has on the order of fifteen
    /// bones and the cord is already folded against the body by the time this captures, so a
    /// blended skin would spend its budget smoothing a seam nobody can see through a net. Nearest
    /// bone at freeze time, offset stored in that bone's local space, and the seam between two
    /// limbs is held together by the cord's own drawn geometry.
    /// </para>
    /// <para>
    /// Bones are held as plain transforms rather than as the rig's own records, so nothing here can
    /// push a ragdoll it does not own. Every one is null-checked on resolve: a netted creature can
    /// be despawned by world streaming while a peer is still drawing the net that caught it, and
    /// that must not throw once per node per frame.
    /// </para>
    /// <para>
    /// <b>The offset round-trips through the bone's full local-to-world matrix, scale included</b>
    /// (<see cref="Transform.InverseTransformPoint"/> / <see cref="Transform.TransformPoint"/>), not
    /// a hand-rolled position-plus-rotation. That is safe only because a ragdoll bone's scale never
    /// changes once <c>RagdollRig</c> is simulating it: physics drives a <c>Rigidbody</c> by writing
    /// position and rotation, never <c>localScale</c>. So whatever scale a bone imported with is
    /// fixed for the life of the ragdoll, the round trip is exact, and using the full transform costs
    /// nothing extra while absorbing any baked-in bone scale for free.
    /// </para>
    /// </summary>
    public class SnareBinding
    {
        // Cloned in Capture, not the caller's own array: RagdollRig.BoneTransforms() returns a fresh
        // array today, but nothing guarantees every future caller will — a caller that pooled and
        // reused its array would silently re-point every already-captured index at a different rig's
        // bones the next time it filled that pool slot. One allocation per net, at freeze time, is
        // cheap insurance against a bug that would otherwise show up as cord bound to the wrong body
        // with no null and no error.
        private Transform[] bones;
        private int[] boneOf;
        private Vector3[] localOffset;

        // The last position Resolve actually computed for each node — a WORLD coordinate, unlike
        // localOffset. If a bone disappears out from under a node, the node freezes here instead of
        // jumping to whatever number localOffset happens to hold: localOffset is expressed in the
        // dead bone's local space, and writing it straight into a world-position array would snap
        // the node to an arbitrary point near the origin the instant its bone is destroyed.
        //
        // This is a field on the binding rather than a gap left in the caller's own array (skip the
        // write, let last frame's value sit there). That would work too, but only for a caller that
        // reuses one buffer forever — a future caller handing Resolve a freshly allocated array every
        // frame would silently get Vector3.zero for every node past a dead bone instead of a freeze.
        // Owning the last-known value here means Resolve is correct regardless of what the caller
        // does with its array.
        private Vector3[] lastResolved;

        // Logged once, the first time Resolve is handed an array of the wrong length, and never
        // again for this binding: a mismatch is a programming error worth surfacing, but a per-frame
        // Debug.LogError on a net with two hundred nodes is its own defect.
        private bool warnedOnMismatch;

        /// <summary>
        /// Did the capture find anything to bind to? False for a rig with no skeleton.
        ///
        /// <para>
        /// Also false before the first <see cref="Capture"/>, and false again after a
        /// <see cref="Capture"/> whose rig had no bones — the fields are nulled out rather than left
        /// holding the previous rig's data, so a binding cannot look bound to a body it was never
        /// actually given.
        /// </para>
        /// <para>
        /// True even when every captured bone has since been destroyed: that is not a failure to
        /// bind, it is a bound net whose body is now gone. <see cref="Resolve"/> freezes such a net
        /// at the pose it was captured in, which is the correct behaviour, not a case this flag needs
        /// to distinguish.
        /// </para>
        /// </summary>
        public bool IsBound => bones != null && bones.Length > 0 && boneOf != null;

        /// <summary>
        /// Nail each node to whichever bone is closest to it right now.
        ///
        /// <para>
        /// Called once, at the end of the cinch. Refuses a rig with no bones rather than binding to
        /// nothing: <c>RagdollRig.Build()</c> can measure a real skeleton and still keep none of it —
        /// the weight floor and bone cap (<c>Select</c>) can trim every candidate away, and the
        /// `if (kept.Count == 0) return;` guard that follows leaves <c>HasSkeleton</c> false and logs
        /// NOTHING. The prefab looks correctly wired and the net simply hangs in mid-air.
        /// <see cref="IsBound"/> is how the caller finds out in time to fall back.
        /// </para>
        /// </summary>
        public void Capture(Vector3[] nodes, Transform[] rigBones)
        {
            if (nodes == null || nodes.Length == 0 || rigBones == null || rigBones.Length == 0)
            {
                bones = null;
                boneOf = null;
                localOffset = null;
                lastResolved = null;
                return;
            }

            bones = (Transform[])rigBones.Clone();
            boneOf = new int[nodes.Length];
            localOffset = new Vector3[nodes.Length];
            lastResolved = new Vector3[nodes.Length];

            for (int i = 0; i < nodes.Length; i++)
            {
                int nearest = -1;
                float best = float.MaxValue;

                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] == null) continue;

                    float distance = (bones[b].position - nodes[i]).sqrMagnitude;
                    if (distance >= best) continue;

                    best = distance;
                    nearest = b;
                }

                boneOf[i] = nearest;

                // A local-space value with no bone to be local TO. Never read back: Resolve's
                // fallback for a live-at-capture, dead-by-resolve bone reads lastResolved instead of
                // this. Zero rather than the world-space nodes[i], so a stray future read fails
                // obviously (everything at the origin) rather than looking plausible.
                localOffset[i] = nearest < 0 ? Vector3.zero : bones[nearest].InverseTransformPoint(nodes[i]);

                // Before Resolve has ever run, the capture position IS the last known good world
                // position — the natural fallback if the bone is destroyed before a single frame
                // gets to track it.
                lastResolved[i] = nodes[i];
            }
        }

        /// <summary>Where every node is this frame. Fills the caller's array rather than allocating.</summary>
        public void Resolve(Vector3[] into)
        {
            if (into == null || !IsBound) return;

            if (into.Length != boneOf.Length && !warnedOnMismatch)
            {
                warnedOnMismatch = true;
                Debug.LogError($"SnareBinding.Resolve: caller passed {into.Length} slots for " +
                                $"{boneOf.Length} bound nodes. Clamping to the smaller count for " +
                                "this and every future frame — the arrays should never disagree.");
            }

            int count = Mathf.Min(into.Length, boneOf.Length);

            for (int i = 0; i < count; i++)
            {
                int bone = boneOf[i];

                // Unity-null, not C#-null: a destroyed transform compares equal to null through the
                // engine's own operator and would otherwise throw a MissingReferenceException here.
                if (bone < 0 || bones[bone] == null)
                {
                    into[i] = lastResolved[i];
                    continue;
                }

                into[i] = bones[bone].TransformPoint(localOffset[i]);
                lastResolved[i] = into[i];
            }
        }
    }
}
