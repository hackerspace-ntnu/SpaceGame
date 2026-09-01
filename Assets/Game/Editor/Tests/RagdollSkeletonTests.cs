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
    }
}
