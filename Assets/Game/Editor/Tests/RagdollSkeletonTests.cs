using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The rig-agnostic half of the ragdoll. These numbers decide whether a blast produces a body
    /// that folds or one that vibrates apart, and none of them needs a scene to check.
    /// </summary>
    public class RagdollSkeletonTests
    {
        [Test]
        public void SelectBones_KeepsHeavyBones_DropsFingers()
        {
            // A spine and two limb segments carrying most of the mesh; two finger bones carrying
            // about one percent each. 764 total, so the 2% floor sits at ~15.3.
            float[] weights = { 300f, 250f, 200f, 8f, 6f };
            bool[] keep = RagdollSkeleton.SelectBones(weights, 0.02f);

            Assert.IsTrue(keep[0]);
            Assert.IsTrue(keep[1]);
            Assert.IsTrue(keep[2]);
            Assert.IsFalse(keep[3], "a bone under the weight floor is not worth a joint");
            Assert.IsFalse(keep[4]);
        }

        [Test]
        public void SelectBones_EmptyOrZeroTotal_KeepsNothing()
        {
            Assert.AreEqual(0, RagdollSkeleton.SelectBones(null, 0.02f).Length);

            // A mesh bound to no bones is not a ragdoll. Keeping everything here would build a
            // joint chain through the rig's helpers and attach points.
            CollectionAssert.AreEqual(new[] { false, false },
                                      RagdollSkeleton.SelectBones(new[] { 0f, 0f }, 0.02f));
        }

        [Test]
        public void CapsuleSize_UsesBoneLength_AndClampsRadius()
        {
            // A long thin bone: height follows the bone, radius follows the spread.
            Vector2 forearm = RagdollSkeleton.CapsuleSize(0.30f, 0.05f, 0.03f);
            Assert.AreEqual(0.30f, forearm.y, 1e-4f);
            Assert.AreEqual(0.05f, forearm.x, 1e-4f);

            // Radius never exceeds half the height, or a short bone becomes a sphere that swallows
            // its neighbours and the body stops being able to fold.
            Vector2 stubby = RagdollSkeleton.CapsuleSize(0.10f, 0.40f, 0.03f);
            Assert.AreEqual(0.05f, stubby.x, 1e-4f);

            // And never collapses to a line, which tunnels through the ground.
            Vector2 tiny = RagdollSkeleton.CapsuleSize(0.20f, 0.001f, 0.03f);
            Assert.AreEqual(0.03f, tiny.x, 1e-4f);
        }

        [Test]
        public void MassFor_SplitsByWeight_AndHasAFloor()
        {
            Assert.AreEqual(30f, RagdollSkeleton.MassFor(300f, 1000f, 100f, 0.5f), 1e-3f);

            // A joint between bodies more than ~10:1 apart is the classic ragdoll explosion.
            Assert.AreEqual(0.5f, RagdollSkeleton.MassFor(1f, 1000f, 100f, 0.5f), 1e-3f,
                            "a near-weightless bone still needs enough mass to simulate stably");
        }

        [Test]
        public void MassFor_ZeroTotalWeight_FallsBackToTheFloor()
        {
            Assert.AreEqual(0.5f, RagdollSkeleton.MassFor(0f, 0f, 100f, 0.5f), 1e-3f);
        }

        [Test]
        public void IsSettled_NeedsBothSpeedsLow_ForLongEnough()
        {
            // Still travelling: not settled however long it has been slow by the other measure.
            Assert.IsFalse(RagdollSkeleton.IsSettled(2f, 0.1f, 5f, 0.25f, 1f, 0.4f));

            // Spinning on the spot — a corpse rolling down a dune. Almost no linear velocity, and
            // obviously not at rest, which is why the two speeds are tested separately.
            Assert.IsFalse(RagdollSkeleton.IsSettled(0.1f, 8f, 5f, 0.25f, 1f, 0.4f));

            // Slow, but only for an instant. A tumbling body passes through zero at the top of
            // every bounce; without the dwell time this fires mid-air.
            Assert.IsFalse(RagdollSkeleton.IsSettled(0.1f, 0.2f, 0.2f, 0.25f, 1f, 0.4f));

            Assert.IsTrue(RagdollSkeleton.IsSettled(0.1f, 0.2f, 0.5f, 0.25f, 1f, 0.4f));
        }

        // ── The rig, for a model drawn as separate rigid pieces ────────────────

        [Test]
        public void SelectRigNodes_PicksTheBones_NotTheMeshesHangingOffThem()
        {
            // The golem's shape, which is every hard-surface model in this project: an articulated
            // chain of empty bones with the rigid pieces parented onto them as leaves.
            //
            //   0 Arm_Golem ─ 1 Bone_Hips ─ 2 Bone_Spine ─ 3 Bone_Thigh_L ─ 4 Mesh_Shin_L
            //                      └ 5 Mesh_Pelvis        └ 6 Mesh_Torso
            int[] parents = { -1, 0, 1, 1, 3, 1, 2 };
            bool[] draws = { false, false, false, false, true, true, true };

            bool[] rig = RagdollSkeleton.SelectRigNodes(parents, draws);

            Assert.IsTrue(rig[1], "Bone_Hips articulates the model and must carry a body");
            Assert.IsTrue(rig[2], "Bone_Spine likewise");
            Assert.IsTrue(rig[3], "Bone_Thigh_L likewise");

            // The meshes are the SHAPE, not the skeleton. Bodies on them are what produced a
            // ragdoll with every limb jointed straight to the pelvis, because no mesh is ever
            // another mesh's ancestor and so nothing could find a parent to hang off.
            Assert.IsFalse(rig[4], "a leaf mesh is geometry, not a joint");
            Assert.IsFalse(rig[5]);
            Assert.IsFalse(rig[6]);

            Assert.IsTrue(rig[0], "the model root articulates everything below it");
        }

        [Test]
        public void SelectRigNodes_IgnoresBranchesThatDrawNothing()
        {
            // 0 root ─ 1 bone ─ 2 mesh
            //        └ 3 AttachPoint ─ 4 AimTarget      (helpers: no geometry anywhere below)
            int[] parents = { -1, 0, 1, 0, 3 };
            bool[] draws = { false, false, true, false, false };

            bool[] rig = RagdollSkeleton.SelectRigNodes(parents, draws);

            Assert.IsTrue(rig[1]);
            Assert.IsFalse(rig[3], "an attach point carries no part of the creature");
            Assert.IsFalse(rig[4]);
        }

        [Test]
        public void NearestRigNode_HandsEachPieceToTheBoneThatMovesIt()
        {
            // Same golem hierarchy. Bone_Chest (2) holds ten plates directly; the shin mesh (4)
            // belongs to the thigh bone (3) it is parented to.
            int[] parents = { -1, 0, 1, 1, 3, 1, 2 };
            bool[] rigNode = { true, true, true, true, false, false, false };

            int[] carrier = RagdollSkeleton.NearestRigNode(parents, rigNode);

            Assert.AreEqual(3, carrier[4], "the shin plate is part of the thigh's body");
            Assert.AreEqual(1, carrier[5], "the pelvis plate is part of the hips");
            Assert.AreEqual(2, carrier[6]);

            // A rig node carries itself: it is the body the geometry ends up on.
            Assert.AreEqual(1, carrier[1]);
        }

        [Test]
        public void CarriedVolume_RanksByBulk_SoADenseLittleMeshCannotOutweighTheBody()
        {
            // The ostrich. Its neck is eleven separate vertebra meshes, each densely tessellated
            // and each bound to one bone, so by raw vertex weight a single vertebra outscores a
            // whole torso — which is how a bird ended up with a ragdoll made of nothing but neck.
            //
            // Vertex weight measures how much SURFACE a bone carries, and that only stands in for
            // mass while the mesh has one density. A model built from several meshes does not.
            float vertebra = RagdollSkeleton.CarriedVolume(400f, 400f, 0.002f);
            float torso = RagdollSkeleton.CarriedVolume(100f, 2000f, 0.900f);

            Assert.Greater(torso, vertebra,
                           "the torso is the bulk of the bird however few vertices describe it");

            // And it is a volume, in the same units a rigid part's own bounds give — which is what
            // lets one measure rank a skinned neck against a bolted-on rigid plate at all.
            Assert.AreEqual(0.045f, torso, 1e-5f);
        }

        [Test]
        public void CarriedVolume_RendererWithNoWeightAtAll_ContributesNothing()
        {
            Assert.AreEqual(0f, RagdollSkeleton.CarriedVolume(0f, 0f, 1f), 1e-6f);
        }

        [Test]
        public void SubtreeBulk_WeighsTheWholeBranch_NotTheBoneAtItsHead()
        {
            // PatrolRobot 1. Its hips carry too little to be worth simulating, so the chest and both
            // legs come out as branch roots at the same depth — and by its own bulk alone a thigh
            // outweighs a chest, so the robot ended up rooted at its right leg with the left leg
            // jointed to it and the entire upper body hanging off the pair.
            //
            //   0 Hips ─ 1 Leg1.L ─ 2 Leg2.L
            //          ├ 3 Leg1.R ─ 4 Leg2.R
            //          └ 5 Chest  ─ 6 Head
            int[] parents = { -1, 0, 1, 0, 3, 0, 5 };
            float[] bulk = { 0f, 4f, 2f, 4f, 2f, 3f, 7f };

            float[] subtree = RagdollSkeleton.SubtreeBulk(parents, bulk);

            Assert.AreEqual(10f, subtree[5], 1e-4f, "the chest carries the head above it");
            Assert.AreEqual(6f, subtree[3], 1e-4f, "a leg carries only the rest of that leg");
            Assert.Greater(subtree[5], subtree[3],
                           "the body hangs from the chest, never from a thigh");

            // The whole hierarchy rolls up to its root, which is what makes the numbers comparable.
            Assert.AreEqual(22f, subtree[0], 1e-4f);
        }
    }
}
