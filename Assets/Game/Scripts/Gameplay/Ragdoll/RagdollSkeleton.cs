using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// Pure math for turning an arbitrary skinned rig into a ragdoll — kept free of scene and
    /// component state so it is unit-testable, and so one implementation provably answers the same
    /// way for a Mixamo humanoid, an ostrich and a six-legged hexapod.
    ///
    /// <para>
    /// This exists because the project has ten rigs and no authored ragdolls: <c>CharacterJoint</c>
    /// appears nowhere on disk. Hand-building a ragdoll per creature is ten assets to author and
    /// re-author every time a rig is re-exported; deriving one from the skinning data is a
    /// decision made once. What the derivation needs to get right is which bones deserve a body at
    /// all — a rig has fifty-odd bones and a ragdoll wants a dozen, and the difference between
    /// them is not their names (which vary per rig and per exporter) but how much of the mesh they
    /// actually carry.
    /// </para>
    ///
    /// <para>
    /// Every threshold arrives as a parameter and none has a default, following the rule
    /// <see cref="SpaceGame.Items.RepulsorBlast.FlingVelocity"/> sets: the serialized field on the
    /// component is the only source of truth, and a default here would be a second value to tune
    /// that silently wins whenever somebody forgets to pass the field.
    /// </para>
    /// </summary>
    public static class RagdollSkeleton
    {
        /// <summary>
        /// Which bones are worth a rigidbody and a joint, by the share of the mesh each one holds.
        ///
        /// <para>
        /// Weight, not name and not depth. A finger bone and a forearm bone sit at similar depths
        /// and are named nothing alike across a Mixamo rig, a Blender armature and an FBX from a
        /// third exporter — but the forearm carries a few percent of the mesh's vertex weight and
        /// the finger carries a fraction of one. That difference is the same on every rig in the
        /// project, which is what makes it the thing to select on.
        /// </para>
        ///
        /// <para>
        /// Dropping the light bones is not an optimisation. A <c>CharacterJoint</c> chain through
        /// three finger phalanges is three more solver iterations to spend, but worse, each one is
        /// a centimetre-long capsule in a stack of them — the geometry PhysX is least stable on,
        /// and the reason under-tuned ragdolls vibrate themselves apart at the extremities.
        /// </para>
        /// </summary>
        /// <param name="boneWeights">Total vertex weight per bone, positionally.</param>
        /// <param name="minWeightFraction">
        /// Share of the whole mesh a bone must carry to be kept, 0..1.
        /// </param>
        /// <returns>
        /// A keep-flag per bone. Empty for a null input, and all-false when nothing is weighted at
        /// all — a mesh bound to no bones is not a ragdoll, and answering "keep everything" would
        /// build a joint chain out of the whole hierarchy including its helpers and attach points.
        /// </returns>
        public static bool[] SelectBones(float[] boneWeights, float minWeightFraction)
        {
            if (boneWeights == null) return System.Array.Empty<bool>();

            var keep = new bool[boneWeights.Length];

            float total = 0f;
            for (int i = 0; i < boneWeights.Length; i++)
                if (boneWeights[i] > 0f) total += boneWeights[i];

            if (total <= 0f) return keep;

            float floor = total * Mathf.Max(minWeightFraction, 0f);
            for (int i = 0; i < boneWeights.Length; i++)
                keep[i] = boneWeights[i] >= floor && boneWeights[i] > 0f;

            return keep;
        }

        /// <summary>
        /// Which nodes of a hierarchy are the RIG — the articulated skeleton, as opposed to the
        /// pieces of geometry hanging off it.
        ///
        /// <para>
        /// The counterpart of <see cref="SelectBones"/> for a model that is not skinned at all: a
        /// chain of empty bones with rigid meshes parented on as leaves, which is how every
        /// hard-surface creature in this project is built. The rule is that a rig node draws
        /// nothing itself and has something below it that does — an empty that leads to geometry
        /// is articulation, an empty that leads nowhere is an attach point or an aim target.
        /// </para>
        ///
        /// <para>
        /// Which nodes get the bodies is the whole difference between a ragdoll and a bag of parts,
        /// and it is not a matter of taste. Meshes are LEAVES: no mesh is ever another mesh's
        /// ancestor, so a skeleton built on them can find no parent for anything and degenerates
        /// into a star — every limb jointed straight to one hub across the width of the body, with
        /// the real chain (hip, knee, ankle) sitting unused beside it. Worse, a mesh that is not
        /// kept is left parented to a bone nothing drives any more, so it hangs in the air while
        /// the rest of the body falls. Bodies on the BONES have neither problem: the joints follow
        /// the articulation the model was rigged with, and every piece of geometry — simulated or
        /// dropped — is still a child of a bone that moves.
        /// </para>
        /// </summary>
        /// <param name="parents">Parent index per node, <c>-1</c> for a root. Parents precede children.</param>
        /// <param name="drawsGeometry">Whether each node draws a mesh of its own.</param>
        public static bool[] SelectRigNodes(int[] parents, bool[] drawsGeometry)
        {
            if (parents == null || drawsGeometry == null) return System.Array.Empty<bool>();

            var leadsToGeometry = new bool[parents.Length];

            // Backwards, so a node is visited after every one of its descendants and the flag has
            // already reached it from below.
            for (int i = parents.Length - 1; i >= 0; i--)
            {
                if (!drawsGeometry[i] && !leadsToGeometry[i]) continue;

                int parent = parents[i];
                if (parent >= 0 && parent < parents.Length) leadsToGeometry[parent] = true;
            }

            var rig = new bool[parents.Length];
            for (int i = 0; i < parents.Length; i++)
                rig[i] = !drawsGeometry[i] && leadsToGeometry[i];

            return rig;
        }

        /// <summary>
        /// Which rig node each node belongs to: itself if it is one, otherwise the nearest above it.
        ///
        /// <para>
        /// This is what turns "a bone" into "a body with a shape". A rig node draws nothing, so the
        /// only thing that can tell you how big it is, or where its surface lies, is the geometry
        /// parented under it — and that geometry belongs to exactly one bone, the nearest one at or
        /// above it. Everything else follows: a bone's importance is the bulk it carries, and its
        /// collider is the box around it.
        /// </para>
        /// </summary>
        /// <returns><c>-1</c> for a node with no rig node at or above it.</returns>
        public static int[] NearestRigNode(int[] parents, bool[] isRigNode)
        {
            if (parents == null || isRigNode == null) return System.Array.Empty<int>();

            var carrier = new int[parents.Length];

            // Forwards: parents precede children, so each node's parent already has its answer.
            for (int i = 0; i < parents.Length; i++)
            {
                if (isRigNode[i]) { carrier[i] = i; continue; }

                int parent = parents[i];
                carrier[i] = parent >= 0 && parent < i ? carrier[parent] : -1;
            }

            return carrier;
        }

        /// <summary>
        /// How much of the creature hangs from each node, itself included.
        ///
        /// <para>
        /// Which branch is the BODY, in one number. The ragdoll has to be rooted somewhere, and when
        /// a rig's own root carries too little to be worth simulating its children become branch
        /// roots at equal depth with nothing above them — for PatrolRobot 1 that is a chest and two
        /// legs. By its own bulk a thigh outscores a chest, so the robot came out rooted at its
        /// right leg, with the left leg jointed to it and the entire upper body hanging off the
        /// pair. By the bulk of what hangs BELOW it the chest wins easily, because it carries the
        /// head, both arms and every finger.
        /// </para>
        /// </summary>
        /// <param name="parents">Parent index per node, <c>-1</c> for a root. Parents precede children.</param>
        /// <param name="bulk">What each node carries on its own.</param>
        public static float[] SubtreeBulk(int[] parents, float[] bulk)
        {
            if (parents == null || bulk == null) return System.Array.Empty<float>();

            var total = (float[])bulk.Clone();

            // Backwards, so a node is added to its parent only once its own children have been
            // added to it and the total below it is complete.
            for (int i = parents.Length - 1; i >= 0; i--)
            {
                int parent = parents[i];
                if (parent >= 0 && parent < total.Length) total[parent] += total[i];
            }

            return total;
        }

        /// <summary>
        /// How much of the creature one bone is, as a volume — the measure that ranks a skinned
        /// bone and a bolted-on rigid part on the same scale.
        ///
        /// <para>
        /// Vertex weight measures how much of the SURFACE a bone carries, and that stands in for
        /// mass only while the mesh has one density. A model built from several meshes does not:
        /// the ostrich's neck is eleven separate vertebra meshes, each densely tessellated and each
        /// bound to a single bone, so by raw weight one vertebra outscores the whole torso. The
        /// bird came out of that with a ragdoll made of nothing but neck — every body bone under
        /// the weight floor, and the one surviving branch taken for the whole animal.
        /// </para>
        ///
        /// <para>
        /// Scaling the share by the renderer's own bounds fixes the units. A bone that carries a
        /// tenth of a big mesh outranks one that carries all of a small one, which is what the word
        /// "importance" was always supposed to mean — and the answer comes out in cubic metres, the
        /// same thing a rigid part's bounds give directly, so one number ranks both kinds of rig.
        /// </para>
        /// </summary>
        /// <param name="boneWeight">This bone's vertex weight within the renderer.</param>
        /// <param name="rendererWeight">The renderer's total vertex weight across all its bones.</param>
        /// <param name="rendererVolume">The renderer's own bounds volume, cubic metres.</param>
        public static float CarriedVolume(float boneWeight, float rendererWeight, float rendererVolume)
        {
            if (rendererWeight <= 0f) return 0f;

            return boneWeight / rendererWeight * rendererVolume;
        }

        /// <summary>
        /// The capsule for one bone: <c>x</c> is the radius, <c>y</c> the height along the bone.
        ///
        /// <para>
        /// The height is the bone's own length, because the bone IS the segment — a capsule that
        /// does not span from joint to joint leaves either a gap the neighbouring limb falls
        /// through or an overlap the solver spends every frame pushing apart.
        /// </para>
        ///
        /// <para>
        /// The radius is clamped at both ends and both clamps are load-bearing. The ceiling of half
        /// the height stops a short bone — a neck vertebra, a pelvis — from becoming a sphere wide
        /// enough to swallow its own neighbours, which reads as a body that cannot fold and instead
        /// jitters. The floor stops a long thin bone from becoming a zero-radius line, which PhysX
        /// will happily tunnel straight through the ground.
        /// </para>
        /// </summary>
        /// <param name="boneLength">Distance to the bone's next physical joint, in metres.</param>
        /// <param name="radialSpread">How far the vertices this bone carries sit from its axis.</param>
        /// <param name="minRadius">The thinnest a limb may be, in metres.</param>
        public static Vector2 CapsuleSize(float boneLength, float radialSpread, float minRadius)
        {
            float height = Mathf.Max(boneLength, 0.02f);
            float radius = Mathf.Min(Mathf.Max(radialSpread, minRadius), height * 0.5f);

            return new Vector2(radius, height);
        }

        /// <summary>
        /// One bone's share of the body's mass, split by how much of the mesh it carries.
        ///
        /// <para>
        /// Splitting by weight rather than by volume is the cheap approximation that happens to be
        /// right: the vertices bound to a bone are the flesh hanging off it, so a torso comes out
        /// heavy and a forearm light without measuring anything. What matters for stability is less
        /// the accuracy than the RATIO — a joint between two bodies whose masses differ by more
        /// than about ten to one is the classic ragdoll explosion, so the floor exists to keep the
        /// extremities from being weightless rather than to make them correct.
        /// </para>
        /// </summary>
        public static float MassFor(float boneWeight, float totalWeight, float totalMass, float minMass)
        {
            if (totalWeight <= 0f) return minMass;

            return Mathf.Max(boneWeight / totalWeight * totalMass, minMass);
        }

        /// <summary>
        /// Has the body come to rest? Both speeds must be low, and they must have STAYED low.
        ///
        /// <para>
        /// The dwell time is the whole point. A tumbling body passes through zero velocity at the
        /// top of every bounce and at the instant it reverses against a slope, so an instantaneous
        /// speed test fires mid-tumble and stands the creature up in the air. Requiring the
        /// slowness to persist is what distinguishes "at rest" from "momentarily between motions".
        /// </para>
        ///
        /// <para>
        /// Angular speed is tested separately rather than folded into the linear one because a body
        /// spinning on the spot has almost no linear velocity and is obviously not settled — a
        /// corpse rolling down a dune ends up doing exactly this.
        /// </para>
        /// </summary>
        /// <param name="slowSeconds">How long both speeds have been under their thresholds.</param>
        public static bool IsSettled(float linearSpeed, float angularSpeed, float slowSeconds,
                                     float linearThreshold, float angularThreshold, float settleSeconds)
            => linearSpeed <= linearThreshold
               && angularSpeed <= angularThreshold
               && slowSeconds >= settleSeconds;
    }
}
