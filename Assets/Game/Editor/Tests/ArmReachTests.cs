// An arm reaching for a point keeps its bones' lengths, puts the hand on the point when it is near enough, stops short and straight
// when it is not, and bends its elbow toward the side it is told.
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ArmReachTests
    {
        private const float Upper = 0.6f, Lower = 0.5f, Close = 0.005f;

        private static readonly Vector3 Shoulder = new Vector3(0f, 2f, 0f);

        [Test]
        public void AReachablePoint_GetsTheHand_AndTheBonesKeepTheirLengths()
        {
            foreach (Vector3 target in new[] { new Vector3(0.5f, 1.6f, 0.6f), new Vector3(-0.3f, 1.2f, 0.4f), new Vector3(0f, 1.7f, 0.9f) })
            {
                ArmReach.Solve(Shoulder, Upper, Lower, target, Shoulder + Vector3.down, out Vector3 elbow, out Vector3 hand);

                Assert.Less(Vector3.Distance(hand, target), Close, target.ToString());
                Assert.AreEqual(Upper, Vector3.Distance(Shoulder, elbow), Close);
                Assert.AreEqual(Lower, Vector3.Distance(elbow, hand), Close);
            }
        }

        [Test]
        public void APointBeyondReach_StopsShortOnTheLineToIt_WithTheArmStraight()
        {
            var target = new Vector3(0f, 2f, 5f);

            ArmReach.Solve(Shoulder, Upper, Lower, target, Shoulder + Vector3.down, out Vector3 elbow, out Vector3 hand);

            Assert.AreEqual(Upper + Lower, Vector3.Distance(Shoulder, hand), 0.01f);
            Assert.Less(Vector3.Angle(hand - Shoulder, target - Shoulder), 0.5f);
            Assert.Less(Vector3.Distance(elbow, Shoulder + (hand - Shoulder).normalized * Upper), 0.08f, "straight: the elbow is on the line");
        }

        [Test]
        public void TheElbowBendsTowardThePole()
        {
            var target = new Vector3(0f, 1.8f, 0.8f);

            ArmReach.Solve(Shoulder, Upper, Lower, target, Shoulder + Vector3.right, out Vector3 outward, out _);
            ArmReach.Solve(Shoulder, Upper, Lower, target, Shoulder + Vector3.left, out Vector3 inward, out _);

            Assert.Greater(outward.x, 0.05f);
            Assert.Less(inward.x, -0.05f);
        }

        [Test]
        public void ATargetOnTheShoulder_DoesNotBreakTheSolve()
        {
            ArmReach.Solve(Shoulder, Upper, Lower, Shoulder, Shoulder + Vector3.down, out Vector3 elbow, out Vector3 hand);

            Assert.IsFalse(float.IsNaN(elbow.x + elbow.y + elbow.z + hand.x + hand.y + hand.z));
        }

        [Test]
        public void ABonesChain_ReachesThePoint_AndKeepsItsBones()
        {
            var shoulder = new GameObject("shoulder").transform;
            var elbow = new GameObject("elbow").transform;
            var wrist = new GameObject("wrist").transform;
            try
            {
                shoulder.position = Shoulder;
                elbow.SetParent(shoulder, false);
                elbow.localPosition = Vector3.down * Upper;
                wrist.SetParent(elbow, false);
                wrist.localPosition = Vector3.down * Lower;
                var arm = new ReachingArm(shoulder, elbow, wrist);
                var target = new Vector3(0.4f, 1.5f, 0.7f);

                arm.Reach(target, Shoulder + Vector3.right, 1f);

                Assert.Less(Vector3.Distance(wrist.position, target), 0.01f);
                Assert.AreEqual(Upper, Vector3.Distance(shoulder.position, elbow.position), Close);
                Assert.AreEqual(Lower, Vector3.Distance(elbow.position, wrist.position), Close);
            }
            finally
            {
                Object.DestroyImmediate(shoulder.gameObject);
            }
        }

        [Test]
        public void AWeightOfZeroLeavesTheAnimationAlone()
        {
            var shoulder = new GameObject("shoulder").transform;
            var elbow = new GameObject("elbow").transform;
            var wrist = new GameObject("wrist").transform;
            try
            {
                shoulder.position = Shoulder;
                elbow.SetParent(shoulder, false);
                elbow.localPosition = Vector3.down * Upper;
                wrist.SetParent(elbow, false);
                wrist.localPosition = Vector3.down * Lower;
                Vector3 before = wrist.position;

                new ReachingArm(shoulder, elbow, wrist).Reach(new Vector3(2f, 2f, 2f), Shoulder + Vector3.right, 0f);

                Assert.AreEqual(before, wrist.position);
            }
            finally
            {
                Object.DestroyImmediate(shoulder.gameObject);
            }
        }
    }
}
