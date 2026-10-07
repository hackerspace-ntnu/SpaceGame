using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// Forward kinematics from a solved pose to the world points of every joint.
    ///
    /// Nothing could ask where a leg's knee actually ended up before this, which is the reason the
    /// shin could be driven through a hillside without anything noticing.
    public class WalkerLimbPoseTests
    {
        private static WalkerLimbSolver.Result Solved(Vector3 target)
        {
            return WalkerLimbSolver.Solve(WalkerTestRig.Frame(), WalkerTestRig.Geometry(),
                                         WalkerLimbSolver.Limits.Default, target, Vector3.up);
        }

        /// The chain walked here has to end up where the IK was asked to put the foot. Asserting it
        /// against the solver's own SoleFromResult would prove nothing — that now delegates here — so
        /// it is asserted against the target itself, which is the property anyone actually cares about.
        [Test]
        public void WalkingTheChain_EndsOnTheFootholdTheSolveWasGiven()
        {
            WalkerLimbGeometry g = WalkerTestRig.Geometry();
            WalkerLimbSolver.Frame f = WalkerTestRig.Frame();

            foreach (Vector3 target in new[]
            {
                WalkerTestRig.RestFoot(),
                WalkerTestRig.RestFoot() + new Vector3(1.5f, 0.5f, 2f),
                WalkerTestRig.RestFoot() + new Vector3(-2f, 1.2f, -1f),
            })
            {
                WalkerLimbPose.Joints j = WalkerLimbPose.Resolve(f, g, Solved(target));

                Assert.Less(Vector3.Distance(j.Sole, target), 1e-3f, $"target {target}");
            }
        }

        [Test]
        public void SegmentLengths_MatchTheMeasuredRig()
        {
            WalkerLimbGeometry g = WalkerTestRig.Geometry();
            WalkerLimbSolver.Frame f = WalkerTestRig.Frame();

            foreach (Vector3 target in new[]
            {
                WalkerTestRig.RestFoot(),
                WalkerTestRig.RestFoot() + new Vector3(2f, 1f, 1.5f),
                WalkerTestRig.RestFoot() + new Vector3(-1f, -0.5f, -2f),
            })
            {
                WalkerLimbPose.Joints j = WalkerLimbPose.Resolve(f, g, Solved(target));

                Assert.AreEqual(g.UpperLength, Vector3.Distance(j.Hip, j.Knee), 1e-3f, "thigh");
                Assert.AreEqual(g.LowerLength, Vector3.Distance(j.Knee, j.Ankle), 1e-3f, "shin");
                Assert.AreEqual(g.FootLength, Vector3.Distance(j.Ankle, j.Sole), 1e-3f, "foot");
            }
        }

        [Test]
        public void Hip_IsTheFrameHip_WhateverTheLegIsDoing()
        {
            // The hip sits ON the yaw axis, which is the property that lets the IK decompose at all.
            // If this ever fails the two solve stages are no longer independent.
            WalkerLimbSolver.Frame f = WalkerTestRig.Frame();
            WalkerLimbPose.Joints j = WalkerLimbPose.Resolve(
                f, WalkerTestRig.Geometry(), Solved(WalkerTestRig.RestFoot() + new Vector3(0f, 0f, 4f)));

            Assert.Less(Vector3.Distance(j.Hip, f.Hip), 1e-4f);
        }

        [Test]
        public void Yaw_SwingsTheWholeLegOutOfItsRestPlane()
        {
            WalkerLimbSolver.Frame f = WalkerTestRig.Frame();
            WalkerLimbGeometry g = WalkerTestRig.Geometry();

            WalkerLimbPose.Joints straight = WalkerLimbPose.Resolve(f, g, Solved(WalkerTestRig.RestFoot()));
            WalkerLimbPose.Joints yawed = WalkerLimbPose.Resolve(
                f, g, Solved(WalkerTestRig.RestFoot() + new Vector3(0f, 0f, 4f)));

            Assert.Greater(Mathf.Abs(yawed.Knee.z - straight.Knee.z), 0.5f,
                "the knee must travel with the leg's plane, not stay behind in it");
        }
    }
}
