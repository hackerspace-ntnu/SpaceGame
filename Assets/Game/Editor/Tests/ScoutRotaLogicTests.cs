using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class ScoutRotaLogicTests
    {
        private static ScoutRecord Home(float homeFor) => new ScoutRecord { Alive = true, HomeFor = homeFor };

        [Test] public void Sends_TheTwoHomeLongest()
        {
            var scouts = new List<ScoutRecord> { Home(30f), Home(5f), Home(50f), Home(10f) };
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void KeepsExactlyTwoOut()
        {
            var scouts = new List<ScoutRecord> { new ScoutRecord { Alive = true, Out = true }, Home(5f), Home(50f) };
            CollectionAssert.AreEqual(new[] { 2 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void EnoughOut_SendsNobody()
        {
            var scouts = new List<ScoutRecord>
            {
                new ScoutRecord { Alive = true, Out = true }, new ScoutRecord { Alive = true, Out = true }, Home(99f),
            };
            CollectionAssert.IsEmpty(ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void SkipsTheDead_AndThoseFighting()
        {
            var scouts = new List<ScoutRecord>
            {
                new ScoutRecord { Alive = false, HomeFor = 99f },
                new ScoutRecord { Alive = true, Fighting = true, HomeFor = 98f },
                Home(1f), Home(2f),
            };
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void FewerThanTwoLiving_SendsWhatThereIs()
        {
            CollectionAssert.AreEqual(new[] { 0 }, ScoutRotaLogic.PickNext(new List<ScoutRecord> { Home(1f) }, 2));
            CollectionAssert.IsEmpty(ScoutRotaLogic.PickNext(new List<ScoutRecord>(), 2));
            CollectionAssert.IsEmpty(ScoutRotaLogic.PickNext(null, 2));
        }

        [Test] public void AFreshCity_SendsTheFirstListed()
        {
            // Every scout is first seen on the same tick, so nobody has been home longer.
            var scouts = new List<ScoutRecord> { Home(0f), Home(0f), Home(0f) };
            CollectionAssert.AreEqual(new[] { 0, 1 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void SweepPoints_AreOnTheRing()
        {
            Vector3[] pts = ScoutRotaLogic.SweepPoints(new Vector3(100f, 0f, 100f), 600f, 6, 0f);
            Assert.AreEqual(6, pts.Length);
            foreach (Vector3 p in pts)
                Assert.AreEqual(600f, Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(100f, 0f, 100f)), 0.01f);
        }

        [Test] public void SweepPoints_StartOnTheGivenBearing_AndGoRoundEvenly()
        {
            Vector3[] pts = ScoutRotaLogic.SweepPoints(Vector3.zero, 10f, 4, 90f);
            Assert.AreEqual(10f, pts[0].x, 0.001f);
            Assert.AreEqual(0f, pts[0].z, 0.001f);
            Assert.AreEqual(-10f, pts[1].z, 0.001f);
            Assert.AreEqual(-10f, pts[2].x, 0.001f);
            CollectionAssert.IsEmpty(ScoutRotaLogic.SweepPoints(Vector3.zero, 10f, 0, 0f));
        }

        [Test] public void LoopLength_IsTheRideOutPlusTheClosedRing()
        {
            // A hexagon's side equals its radius: 600 out + 6 x 600 round, the sixth chord back to the start.
            Assert.AreEqual(4200f, ScoutRotaLogic.LoopLength(600f, 6), 0.01f);
            Assert.AreEqual(0f, ScoutRotaLogic.LoopLength(600f, 0));
        }

        [Test] public void TheLoop_ClosesOnItsFirstWaypoint()
        {
            // 6 waypoints, then the first again: 6 chords, matching LoopLength.
            Assert.AreEqual(7, ScoutRotaLogic.LoopStops(6));
            Assert.AreEqual(0, ScoutRotaLogic.LoopStops(0));
        }

        [Test] public void AReturningScout_IsBackOnlyInsideRegroupDistance()
        {
            Assert.IsFalse(ScoutRotaLogic.BackWithTheColumn(600f, 150f));
            Assert.IsFalse(ScoutRotaLogic.BackWithTheColumn(150f, 150f));
            Assert.IsTrue(ScoutRotaLogic.BackWithTheColumn(149f, 150f));
        }

        [Test] public void ASweepThatRunsTooLong_EndsAndReturnsHome()
        {
            Assert.IsFalse(ScoutRotaLogic.SweepOver(100f, 300f, false));
            Assert.IsTrue(ScoutRotaLogic.SweepOver(301f, 300f, false));
            Assert.IsTrue(ScoutRotaLogic.SweepOver(10f, 300f, true));
        }

        [Test] public void AWaypointOnLoadedGround_IsRiddenAsItIs()
        {
            Assert.IsTrue(ScoutRotaLogic.TryPullIn(new Vector3(10f, 5f, 0f), new Vector3(600f, 0f, 0f), 100f, 25f,
                                                   _ => true, out Vector3 point));
            Assert.AreEqual(new Vector3(610f, 5f, 0f), point);
        }

        [Test] public void AWaypointBeyondTheStreamedGround_IsPulledInUntilItsChunkIsLoaded()
        {
            // Ground is in within 250 m of the city: the 600 m waypoint comes in to 200 m.
            Assert.IsTrue(ScoutRotaLogic.TryPullIn(Vector3.zero, new Vector3(0f, 0f, 600f), 100f, 25f,
                                                   p => p.magnitude <= 250f, out Vector3 point));
            Assert.AreEqual(200f, point.z, 0.001f);
        }

        [Test] public void NoLoadedGroundOutToTheWaypoint_CannotBeReached()
        {
            Assert.IsFalse(ScoutRotaLogic.TryPullIn(Vector3.zero, new Vector3(0f, 0f, 600f), 100f, 25f,
                                                    _ => false, out _));
        }
    }
}
