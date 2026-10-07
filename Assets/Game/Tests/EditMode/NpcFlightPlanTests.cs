// Assets/Game/Tests/EditMode/NpcFlightPlanTests.cs
using System;
using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The NPC craft's flight plan, flown against a stand-in for FlyingRigidbodyMotor's AI path: velocity
    /// ramps toward (target - position).normalized * MaxSpeed * Speed at Acceleration. MaxSpeed and
    /// Acceleration are the values NpcOrnithopterBuilder writes onto the prefab (Task 4). Behaviour tests:
    /// what a watching player has to see. A retune of NpcFlightSettings must keep them passing.
    /// </summary>
    public class NpcFlightPlanTests
    {
        private const float Dt = 1f / 50f;
        private const float MaxSpeed = 25f;        // NpcOrnithopterBuilder.CruiseSpeed
        private const float Acceleration = 8f;     // NpcOrnithopterBuilder.Acceleration
        private const float Cruise = 60f;
        private const float LandingTolerance = 15f;   // spec: "land within ~15 m of the goal"
        private static readonly float SafeClosing = new OrnithopterCrashConfig().SafeClosingSpeed;

        private struct Flight
        {
            public bool Landed, HitTheGround;
            public Vector3 Touchdown;
            public float SinkAtTouchdown, Highest, LowestCruiseClearance, Seconds;
        }

        private static NpcFlightPlan Plan()
        {
            var plan = new NpcFlightPlan(new NpcFlightSettings());
            plan.Begin();
            return plan;
        }

        private static Flight Fly(NpcFlightPlan plan, Vector3 position, Vector3 velocity, Vector3 landing,
                                  Func<float, float, float> ground, float seconds)
        {
            var f = new Flight { Highest = position.y, LowestCruiseClearance = float.MaxValue };
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                float g = ground(position.x, position.z);
                NpcFlightStep step = plan.Step(position, g, landing, Cruise);
                if (step.Touchdown)
                {
                    f.Landed = true;
                    f.Touchdown = position;
                    f.SinkAtTouchdown = Mathf.Max(0f, -velocity.y);
                    f.Seconds = i * Dt;
                    return f;
                }

                Vector3 toTarget = step.Target - position;
                Vector3 desired = toTarget.sqrMagnitude > 0.04f ? toTarget.normalized * MaxSpeed * step.Speed : Vector3.zero;
                velocity = Vector3.MoveTowards(velocity, desired, Acceleration * Dt);
                position += velocity * Dt;

                f.Highest = Mathf.Max(f.Highest, position.y);
                if (plan.Phase == NpcFlightPhase.EnRoute && i * Dt > 20f)
                    f.LowestCruiseClearance = Mathf.Min(f.LowestCruiseClearance, position.y - g);
                if (position.y < g)
                {
                    f.HitTheGround = true;
                    f.Touchdown = position;
                    f.Seconds = i * Dt;
                    return f;
                }
            }
            return f;
        }

        private static float Flat0(float x, float z) => 0f;
        private static float Miss(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void TakeOff_ClimbsToCruise_ThenLandsNearTheGoal()
        {
            var goal = new Vector3(600f, 0f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, 3f, 0f), Vector3.zero, goal, Flat0, 300f);

            Assert.IsFalse(f.HitTheGround, "flew into the ground");
            Assert.Greater(f.Highest, Cruise - 6f, "never climbed to cruise height");
            Assert.IsTrue(f.Landed, "never touched down");
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m from the goal");
            Assert.Less(f.SinkAtTouchdown, SafeClosing, $"came down at {f.SinkAtTouchdown:F1} m/s");
        }

        [Test]
        public void OffTheSkyCity_LandsNearAGoalTwoKilometresAway()
        {
            var goal = new Vector3(0f, 0f, 2000f);
            Flight f = Fly(Plan(), new Vector3(0f, 280f, 0f), new Vector3(0f, 0f, 14f), goal, Flat0, 400f);

            Assert.IsFalse(f.HitTheGround);
            Assert.IsTrue(f.Landed, "never touched down");
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m away");
            Assert.Less(f.SinkAtTouchdown, SafeClosing);
        }

        [Test]
        public void ArrivingTooHigh_SpiralsDown_AndLandsGently()
        {
            var goal = new Vector3(40f, 0f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, 120f, 0f), Vector3.zero, goal, Flat0, 300f);

            Assert.IsFalse(f.HitTheGround, "dived into the ground instead of spiralling down");
            Assert.IsTrue(f.Landed);
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m away");
            Assert.Less(f.SinkAtTouchdown, SafeClosing);
        }

        [Test]
        public void TerrainRisingUnderTheRoute_IsClimbedOver()
        {
            float Ridge(float x, float z) => Mathf.Clamp((x - 200f) * 0.15f, 0f, 150f);
            var goal = new Vector3(1500f, 150f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, 3f, 0f), Vector3.zero, goal, Ridge, 400f);

            Assert.IsFalse(f.HitTheGround, "flew into the rising ground");
            Assert.Greater(f.LowestCruiseClearance, 20f, $"cruised {f.LowestCruiseClearance:F1} m over the ground");
            Assert.IsTrue(f.Landed);
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance);
        }

        [Test]
        public void AWreck_SpiralsDownNearWhereItWasHit()
        {
            NpcFlightPlan plan = Plan();
            var hit = new Vector3(0f, 60f, 0f);
            plan.Wreck(hit);
            Flight f = Fly(plan, hit, new Vector3(25f, 0f, 0f), new Vector3(3000f, 0f, 0f), Flat0, 60f);

            Assert.IsTrue(f.Landed || f.HitTheGround, "the wreck never came down");
            Vector3 end = f.Touchdown;
            Assert.Less(Miss(end, hit), 3f * new NpcFlightSettings().SpiralRadius,
                        "a wreck flew off instead of coming down where the pilot died");
            Assert.Less(f.Seconds, 20f, "a wreck took too long to come down");
        }

        [Test]
        public void StartingOnTheGround_TakesOff_AndLandsNearTheGoalNotAtTheStart()
        {
            float rest = new NpcFlightSettings().TouchdownHeight;
            var goal = new Vector3(30f, 0f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, rest, 0f), Vector3.zero, goal, Flat0, 200f);

            Assert.IsFalse(f.HitTheGround);
            Assert.IsTrue(f.Landed, "never touched down");
            Assert.Greater(f.Seconds, 2f, "landed on the spot it started from");
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m from the goal");
            Assert.Less(f.SinkAtTouchdown, SafeClosing);
        }

        [Test]
        public void LaunchingFromASkyCityPlatform_LeavesIt_AndLandsNearTheGoal()
        {
            float Platform(float x, float z) => new Vector2(x, z).magnitude < 40f ? 280f : 0f;
            float rest = 280f + new NpcFlightSettings().TouchdownHeight;
            var goal = new Vector3(0f, 0f, 1000f);
            Flight f = Fly(Plan(), new Vector3(0f, rest, 0f), Vector3.zero, goal, Platform, 400f);

            Assert.IsFalse(f.HitTheGround);
            Assert.IsTrue(f.Landed, "never touched down");
            Assert.Greater(f.Seconds, 5f, "landed on the platform it started from");
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m from the goal");
            Assert.Less(f.SinkAtTouchdown, SafeClosing);
        }

        [Test]
        public void Begin_RestartsALandedOrWreckedPlan()
        {
            NpcFlightPlan plan = Plan();
            plan.Wreck(Vector3.zero);
            plan.Begin();
            Assert.AreEqual(NpcFlightPhase.EnRoute, plan.Phase);
        }
    }
}
