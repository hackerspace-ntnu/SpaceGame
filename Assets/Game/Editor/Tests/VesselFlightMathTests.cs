// The kinematic flight rules a sky vessel is ticked with on the server: how high it cruises over
// terrain, how it closes on a point without overshooting, and how fast it can swing its nose round.
using NUnit.Framework;
using SpaceGame.Vehicles;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class VesselFlightMathTests
    {
        private const float MaxSpeed = 28f;
        private const float Accel = 6f;
        private const float Dt = 0.1f;
        private const float Clearance = 45f;
        private const float Floor = 30f;

        // ── Cruise altitude ────────────────────────────────────────────────────────

        [Test]
        public void CruisesAtClearanceAboveFlatGround()
        {
            Assert.AreEqual(55f, VesselFlightMath.CruiseAltitude(10f, 10f, Clearance, Floor), 1e-4f);
        }

        [Test]
        public void ClimbsForARidgeAheadBeforeReachingIt()
        {
            float altitude = VesselFlightMath.CruiseAltitude(10f, 200f, Clearance, Floor);

            Assert.GreaterOrEqual(altitude, 200f + Clearance, "the ridge ahead gets the full clearance");
        }

        [Test]
        public void ClearanceIsNeverTunedBelowTheFloor()
        {
            Assert.AreEqual(40f, VesselFlightMath.CruiseAltitude(10f, 0f, 5f, Floor), 1e-4f);
        }

        // ── Step ───────────────────────────────────────────────────────────────────

        [Test]
        public void StepNeverOvershootsAndDeceleratesOnArrival()
        {
            Vector3 target = new Vector3(100f, 0f, 0f);
            Vector3 position = Vector3.zero, velocity = Vector3.zero;
            float previousDistance = float.MaxValue, speedAt20 = -1f, speedAt5 = -1f;
            bool arrived = false;

            for (int i = 0; i < 2000 && !arrived; i++)
            {
                (position, velocity) = VesselFlightMath.Step(position, velocity, target, MaxSpeed, Accel, Dt);
                float distance = Vector3.Distance(position, target);

                Assert.LessOrEqual(position.x, target.x, $"overshot on step {i}");
                Assert.LessOrEqual(distance, previousDistance + 1e-4f, $"moved away on step {i}");
                Assert.LessOrEqual(velocity.magnitude, MaxSpeed + 1e-3f, "never faster than max speed");
                if (speedAt20 < 0f && distance < 20f) speedAt20 = velocity.magnitude;
                if (speedAt5 < 0f && distance < 5f) speedAt5 = velocity.magnitude;

                previousDistance = distance;
                arrived = position == target;
            }

            Assert.IsTrue(arrived, "reaches the target instead of creeping up on it forever");
            Assert.AreEqual(Vector3.zero, velocity, "and stops there");
            Assert.Less(speedAt20, MaxSpeed, "already braking 20 m out");
            Assert.Less(speedAt5, speedAt20, "and slower still at 5 m");
        }

        [Test]
        public void StepAtTheTargetStaysPut()
        {
            Vector3 target = new Vector3(3f, 4f, 5f);

            (Vector3 position, Vector3 velocity) = VesselFlightMath.Step(target, Vector3.zero, target, MaxSpeed, Accel, Dt);

            Assert.AreEqual(target, position);
            Assert.AreEqual(Vector3.zero, velocity);
        }

        [Test]
        public void AZeroTickMovesNothing()
        {
            Vector3 position = new Vector3(0f, 0f, 99.9f), velocity = new Vector3(0f, 0f, 20f);

            (Vector3 p, Vector3 v) = VesselFlightMath.Step(position, velocity, new Vector3(0f, 0f, 100f),
                                                            MaxSpeed, Accel, 0f);

            Assert.AreEqual(position, p);
            Assert.AreEqual(velocity, v);
        }

        [Test]
        public void AStepMovingAwayNeverSnapsOntoTheTarget()
        {
            Vector3 target = new Vector3(1f, 0f, 0f);

            (Vector3 p, Vector3 v) = VesselFlightMath.Step(Vector3.zero, new Vector3(-MaxSpeed, 0f, 0f), target,
                                                            MaxSpeed, Accel, Dt);

            Assert.Less(p.x, 0f, "still carried away by its speed, not teleported 1 m back");
            Assert.AreNotEqual(Vector3.zero, v);
        }

        // ── Turning ────────────────────────────────────────────────────────────────

        [Test]
        public void TurnsAtTheRateGivenAndOnlyInYaw()
        {
            Quaternion rotation = Quaternion.identity;
            Vector3 east = new Vector3(1f, -0.5f, 0f);

            Quaternion once = VesselFlightMath.TurnToward(rotation, east, 45f, 1f);
            Assert.AreEqual(45f, Quaternion.Angle(rotation, once), 1e-2f, "45 degrees in one second");

            for (int i = 0; i < 10; i++) once = VesselFlightMath.TurnToward(once, east, 45f, 1f);
            Assert.AreEqual(0f, Vector3.Angle(once * Vector3.forward, Vector3.right), 1e-2f,
                            "ends facing the flat direction, not pitched down it");
        }

        [Test]
        public void AVerticalDirectionLeavesTheHeadingAlone()
        {
            Quaternion rotation = Quaternion.Euler(0f, 30f, 0f);

            Assert.AreEqual(rotation, VesselFlightMath.TurnToward(rotation, Vector3.down, 45f, 1f));
        }
    }
}
