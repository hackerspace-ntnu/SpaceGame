// Assets/Game/Editor/Tests/NpcAirPatrolTests.cs
// An air patrol's pure parts: the chevron, the loop, where members are made, and the folded record flying it.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class NpcAirPatrolTests
    {
        private const float Tolerance = 0.01f;

        private static NpcGroupAirPatrol Patrol() => new NpcGroupAirPatrol
        {
            route = new[] { new Vector3(0f, 0f, 0f), new Vector3(1000f, 0f, 0f), new Vector3(1000f, 0f, 1000f) },
            waypointDwell = new Vector2(10f, 20f),
        };

        [Test]
        public void TheChevron_AlternatesSides_EachPairOneRankBackAndUp()
        {
            NpcGroupAirPatrol patrol = Patrol();
            Vector3 first = NpcAirPatrol.WingOffset(0, patrol);
            Vector3 second = NpcAirPatrol.WingOffset(1, patrol);
            Vector3 third = NpcAirPatrol.WingOffset(2, patrol);

            Assert.Less(first.x, 0f, "the first wingman is not on the left");
            Assert.Greater(second.x, 0f, "the second wingman is not on the right");
            Assert.AreEqual(first.z, second.z, Tolerance, "a pair is not abreast");
            Assert.Less(third.z, first.z, "the second pair is not further back");
            Assert.Greater(third.y, first.y, "the second pair is not higher");
            Assert.Less(first.z, 0f, "a wingman ahead of its leader");
        }

        [Test]
        public void NoTwoStations_AreWithinSeparationOfEachOtherOrTheLeader()
        {
            NpcGroupAirPatrol patrol = Patrol();
            float separation = new EscortSettings().separationRadius;
            var stations = new List<Vector3> { Vector3.zero };
            for (int i = 0; i < 4; i++) stations.Add(NpcAirPatrol.WingOffset(i, patrol));

            for (int a = 0; a < stations.Count; a++)
            for (int b = a + 1; b < stations.Count; b++)
                Assert.Greater(Vector3.Distance(stations[a], stations[b]), separation,
                               $"stations {a} and {b} sit inside the separation radius and would push each other about");
        }

        [Test]
        public void TheNextLeg_IsTheWaypointAfterTheNearest_WrappingRoundTheLoop()
        {
            Vector3[] route = Patrol().route;
            Assert.AreEqual(1, NpcAirPatrol.NextLeg(route, new Vector3(30f, 50f, -20f)));
            Assert.AreEqual(2, NpcAirPatrol.NextLeg(route, new Vector3(990f, 0f, 10f)));
            Assert.AreEqual(0, NpcAirPatrol.NextLeg(route, new Vector3(1000f, 0f, 990f)), "the loop does not wrap");
        }

        [Test]
        public void TheLeader_IsMadeAtTheOrigin_AndAWingmanOnItsChevron_TurnedWithTheHeading()
        {
            NpcGroupAirPatrol patrol = Patrol();
            var origin = new Vector3(5f, 70f, 5f);

            Assert.AreEqual(origin, NpcAirPatrol.SpawnPoint(origin, Vector3.right, true, 0, patrol));

            Vector3 wing = NpcAirPatrol.SpawnPoint(origin, Vector3.right, false, 0, patrol) - origin;
            Vector3 expected = Quaternion.LookRotation(Vector3.right) * NpcAirPatrol.WingOffset(0, patrol);
            Assert.That(Vector3.Distance(wing, expected), Is.LessThan(Tolerance));
        }

        [Test]
        public void Folded_ThePatrolFliesTheLoop_CirclingEachWaypointForItsDwell()
        {
            NpcGroupAirPatrol patrol = Patrol();
            var group = new NpcGroup { Position = patrol.route[0] };

            NpcAirPatrol.TickVirtual(group, patrol, 17.5f, 1f);
            Assert.IsTrue(group.HasGoal);
            Assert.AreEqual(patrol.route[1], group.GoalPosition, "a patrol at the first waypoint did not head for the second");

            NpcAirPatrol.TickVirtual(group, patrol, 17.5f, 100f);
            Assert.IsFalse(group.HasGoal, "it did not arrive");
            Assert.That(group.DwellRemaining, Is.InRange(patrol.waypointDwell.x, patrol.waypointDwell.y));

            NpcAirPatrol.TickVirtual(group, patrol, 17.5f, 100f);
            NpcAirPatrol.TickVirtual(group, patrol, 17.5f, 1f);
            Assert.AreEqual(patrol.route[2], group.GoalPosition, "after its dwell it did not fly on to the next waypoint");
        }

        [Test]
        public void ARouteOfLessThanTwoPoints_IsNoPatrol()
        {
            Assert.IsFalse(new NpcGroupAirPatrol().IsSet);
            Assert.IsFalse(new NpcGroupAirPatrol { route = new[] { Vector3.zero } }.IsSet);
            Assert.IsTrue(Patrol().IsSet);
        }
    }
}
