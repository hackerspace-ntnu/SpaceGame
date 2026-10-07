// The rider's motion response, and above all its limits.
//
// The clamps are the point. These gains multiply a velocity measured by differencing the mount's
// transform, and a legged mount's transform can move a very long way in one frame — a leap landing,
// a chunk streaming in, a scene migration. Unclamped, any of those folds the rider's spine through
// their own pelvis for as long as the smoothing takes to forget it.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class RiderPoseMathTests
    {
        private const float Tolerance = 1e-4f;

        private static RiderPoseGains Gains => RiderPoseGains.Default;

        [Test]
        public void StandingStill_LeavesTheAuthoredPoseAlone()
        {
            Vector3 offset = RiderPoseMath.SpineOffset(0f, 0f, 0f, Gains);
            Assert.AreEqual(Vector3.zero, offset);
        }

        [Test]
        public void Bounce_LeansOppositeWaysForRisingAndFalling()
        {
            float rising = RiderPoseMath.SpineOffset(1f, 0f, 0f, Gains).x;
            float falling = RiderPoseMath.SpineOffset(-1f, 0f, 0f, Gains).x;

            Assert.Greater(rising, 0f);
            Assert.Less(falling, 0f);
            Assert.AreEqual(rising, -falling, Tolerance);
        }

        [Test]
        public void Bounce_ClampsAtItsLimitInBothDirections()
        {
            RiderPoseGains gains = Gains;

            Assert.AreEqual(gains.bounceMax,
                RiderPoseMath.SpineOffset(1000f, 0f, 0f, gains).x, Tolerance);
            Assert.AreEqual(-gains.bounceMax,
                RiderPoseMath.SpineOffset(-1000f, 0f, 0f, gains).x, Tolerance);
        }

        [Test]
        public void ForwardSpeed_LeansTheRiderForward()
        {
            Assert.Greater(RiderPoseMath.SpineOffset(0f, 5f, 0f, Gains).x, 0f);
        }

        [Test]
        public void ForwardSpeed_ClampsAtItsLimit()
        {
            Assert.AreEqual(Gains.speedMax,
                RiderPoseMath.SpineOffset(0f, 1000f, 0f, Gains).x, Tolerance);
        }

        [Test]
        public void ReversingDoesNotLeanTheRiderBackwards()
        {
            // Backing a mount up is slow and deliberate. Leaning out of it reads as the rider being
            // shoved rather than as them riding, so the speed channel is one-sided by design.
            Assert.AreEqual(0f, RiderPoseMath.SpineOffset(0f, -8f, 0f, Gains).x, Tolerance);
        }

        [Test]
        public void Turning_RollsOppositeWaysForLeftAndRight()
        {
            float right = RiderPoseMath.SpineOffset(0f, 0f, 90f, Gains).z;
            float left = RiderPoseMath.SpineOffset(0f, 0f, -90f, Gains).z;

            Assert.Greater(right, 0f);
            Assert.Less(left, 0f);
            Assert.AreEqual(right, -left, Tolerance);
        }

        [Test]
        public void Turning_ClampsAtItsLimitInBothDirections()
        {
            RiderPoseGains gains = Gains;

            Assert.AreEqual(gains.turnMax,
                RiderPoseMath.SpineOffset(0f, 0f, 100000f, gains).z, Tolerance);
            Assert.AreEqual(-gains.turnMax,
                RiderPoseMath.SpineOffset(0f, 0f, -100000f, gains).z, Tolerance);
        }

        [Test]
        public void TurningNeverPitchesAndBouncingNeverRolls()
        {
            Assert.AreEqual(0f, RiderPoseMath.SpineOffset(0f, 0f, 120f, Gains).x, Tolerance);
            Assert.AreEqual(0f, RiderPoseMath.SpineOffset(4f, 6f, 0f, Gains).z, Tolerance);
        }

        [Test]
        public void YawIsNeverDriven()
        {
            // The rider faces where the mount faces. Nothing in the motion response may twist them
            // off that heading.
            Assert.AreEqual(0f, RiderPoseMath.SpineOffset(9f, 30f, 500f, Gains).y, Tolerance);
        }

        [Test]
        public void EveryChannelAtOnceStaysWithinTheSumOfItsLimits()
        {
            RiderPoseGains gains = Gains;
            Vector3 offset = RiderPoseMath.SpineOffset(9999f, 9999f, 9999f, gains);

            Assert.LessOrEqual(Mathf.Abs(offset.x), gains.bounceMax + gains.speedMax + Tolerance);
            Assert.LessOrEqual(Mathf.Abs(offset.z), gains.turnMax + Tolerance);
        }

        [Test]
        public void NegativeLimitsAreTreatedAsMagnitudes()
        {
            // A limit typed in as -9 in the inspector must still bound the value rather than
            // inverting the clamp and pinning the rider at the limit permanently.
            RiderPoseGains gains = Gains;
            gains.bounceMax = -9f;

            Assert.AreEqual(9f, RiderPoseMath.SpineOffset(1000f, 0f, 0f, gains).x, Tolerance);
        }
    }
}
