using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class MonowheelDriveTests
    {
        private static MonowheelDriveSettings S => new MonowheelDriveSettings
        {
            topSpeed = 20f, reverseSpeed = 4f, acceleration = 6f, braking = 12f, coastDrag = 2f,
            turnRate = 90f, turnRateAtTop = 25f, lateralGrip = 4f,
            maxLean = 25f, leanPerLateralAccel = 2.5f,
            cornerSlowAngle = 90f, cornerMinSpeedFraction = 0.3f, alignAngle = 90f,
        };

        [Test] public void Throttle_AcceleratesTowardTopSpeed_AndNoFurther()
        {
            Assert.AreEqual(6f, MonowheelDrive.NextSpeed(0f, 1f, 1f, S), 1e-4f);
            Assert.AreEqual(20f, MonowheelDrive.NextSpeed(19f, 1f, 1f, S), 1e-4f);
        }

        [Test] public void NoThrottle_Coasts_BrakeStopsFaster()
        {
            Assert.AreEqual(8f, MonowheelDrive.NextSpeed(10f, 0f, 1f, S), 1e-4f);
            Assert.AreEqual(0f, MonowheelDrive.NextSpeed(10f, -1f, 1f, S), 1e-4f);
        }

        [Test] public void Reverse_IsSlow() =>
            Assert.AreEqual(-4f, MonowheelDrive.NextSpeed(0f, -1f, 10f, S), 1e-4f);

        [Test] public void TurningIsSlowerAtSpeed()
        {
            Assert.AreEqual(90f, MonowheelDrive.TurnRate(0f, S), 1e-4f);
            Assert.AreEqual(25f, MonowheelDrive.TurnRate(20f, S), 1e-4f);
            Assert.Greater(MonowheelDrive.TurnRate(5f, S), MonowheelDrive.TurnRate(15f, S));
        }

        [Test] public void Heading_FollowsSteer_ReversedWhenRollingBackwards()
        {
            Assert.AreEqual(9f, MonowheelDrive.NextHeading(0f, 1f, 0f, 0.1f, S), 1e-3f);
            Assert.Less(MonowheelDrive.NextHeading(0f, 1f, -2f, 0.1f, S), 0f);
        }

        [Test] public void Grip_BleedsSideways_KeepsVerticalAndForward()
        {
            Vector3 v = MonowheelDrive.GripVelocity(new Vector3(5f, -3f, 10f), Vector3.forward, 10f, 1f, S);
            Assert.AreEqual(10f, v.z, 1e-3f);
            Assert.AreEqual(-3f, v.y, 1e-3f, "gravity is the Rigidbody's");
            Assert.Less(Mathf.Abs(v.x), 5f * 0.05f, "e^-4 of the slide is left after a second");
        }

        [Test] public void Lean_IsIntoTheTurn_AndCapped()
        {
            float right = MonowheelDrive.Lean(10f, 30f, S);
            Assert.Greater(right, 0f);
            Assert.AreEqual(-right, MonowheelDrive.Lean(10f, -30f, S), 1e-4f);
            Assert.AreEqual(25f, MonowheelDrive.Lean(20f, 180f, S), 1e-4f);
        }

        [Test] public void CornerAhead_LowersTheWantedSpeed()
        {
            float straight = MonowheelDrive.WantedSpeed(500f, 0f, 1f, S);
            float hairpin = MonowheelDrive.WantedSpeed(500f, 90f, 1f, S);
            Assert.AreEqual(20f, straight, 1e-4f);
            Assert.AreEqual(6f, hairpin, 1e-4f, "cornerMinSpeedFraction of top speed");
        }

        [Test] public void StoppingDistance_BrakesInTime()
        {
            // v^2 = 2 a d: with 12 m/s^2 braking, 6 m left allows 12 m/s.
            Assert.AreEqual(12f, MonowheelDrive.WantedSpeed(6f, 0f, 1f, S), 1e-3f);
            Assert.AreEqual(0f, MonowheelDrive.WantedSpeed(0f, 0f, 1f, S), 1e-4f);
        }

        [Test] public void NpcInput_SteersTowardTheTarget_ThrottlesToTheWantedSpeed()
        {
            var (throttle, steer) = MonowheelDrive.NpcInput(0f, 5f, Vector3.zero, new Vector3(10f, 0f, 10f), 15f, S);
            Assert.Greater(steer, 0f, "target is to the right of +Z");
            Assert.AreEqual(1f, throttle, 1e-4f);
            var (brake, _) = MonowheelDrive.NpcInput(0f, 15f, Vector3.zero, new Vector3(0f, 0f, 10f), 5f, S);
            Assert.Less(brake, 0f, "over the wanted speed: brake");
        }

        [Test] public void NpcInput_AtTheSteerPoint_KeepsHeadingAndDoesNotSteer()
        {
            Vector3 here = new Vector3(3f, 0f, 7f);
            var (_, steer) = MonowheelDrive.NpcInput(40f, 5f, here, here, 5f, S);
            Assert.AreEqual(0f, steer, 1e-4f, "no bearing to a point underneath: hold the heading");
        }

        [Test] public void NpcInput_TargetBehind_DoesNotThrottle()
        {
            var (throttle, steer) = MonowheelDrive.NpcInput(0f, 0f, Vector3.zero, new Vector3(0f, 0f, -10f), 15f, S);
            Assert.LessOrEqual(throttle, 0f, "turn first; throttling now would orbit the target");
            Assert.AreNotEqual(0f, steer, "and it does turn");
        }
    }
}
