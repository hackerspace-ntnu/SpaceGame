// Assets/Game/Editor/Tests/EscortSteeringTests.cs
// Keeping a station on something that moves: the station sits in the anchor's yaw frame (a banking leader
// does not swing its wingmen), an orbit turns it at its rate, the flier matches the station's speed and
// catches up or eases off by how far it lags, never aims into the ground, and pushes off a neighbour.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class EscortSteeringTests
    {
        private const float Tolerance = 0.01f;
        private const float TopSpeed = 25f;

        private static EscortSettings NoDrift() => new EscortSettings { driftAmplitude = 0f };

        [Test]
        public void TheStation_IsInTheAnchorsYawFrame_SoAPitchedOrBankedAnchorDoesNotMoveIt()
        {
            var offset = new Vector3(10f, 2f, -14f);
            Vector3 level = EscortSteering.StationPoint(Vector3.zero, Vector3.forward, offset, 0, NoDrift(), 0f);
            Vector3 pitched = EscortSteering.StationPoint(Vector3.zero, Quaternion.Euler(-30f, 0f, 0f) * Vector3.forward,
                                                          offset, 0, NoDrift(), 0f);

            Assert.That(Vector3.Distance(level, offset), Is.LessThan(Tolerance), "a level anchor heading +Z is not the plain offset");
            Assert.That(Vector3.Distance(level, pitched), Is.LessThan(Tolerance), "pitching the anchor moved its wingman's station");
        }

        [Test]
        public void TheStation_TurnsWithTheAnchor()
        {
            Vector3 point = EscortSteering.StationPoint(Vector3.zero, Vector3.right, new Vector3(10f, 0f, 0f), 0, NoDrift(), 0f);

            Assert.That(Vector3.Distance(point, new Vector3(0f, 0f, -10f)), Is.LessThan(Tolerance),
                        "an anchor heading +X should put a station on its right at -Z");
        }

        [Test]
        public void AnOrbit_StartsAtItsPhase_AndTurnsAtItsRate()
        {
            FlightStation orbit = FlightStation.Orbit(null, 100f, 20f, 10f, 90f, 0);

            Assert.That(Vector3.Distance(EscortSteering.OffsetAt(orbit, 0f), new Vector3(100f, 20f, 0f)), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(EscortSteering.OffsetAt(orbit, 9f), new Vector3(0f, 20f, -100f)), Is.LessThan(Tolerance),
                        "nine seconds at 10°/s did not carry the station a quarter turn on");
        }

        [Test]
        public void AFixedStation_NeverTurns()
        {
            FlightStation fixedStation = FlightStation.Fixed(null, new Vector3(5f, 0f, 5f), 0);
            Assert.IsFalse(fixedStation.Orbits);
            Assert.AreEqual(fixedStation.Offset, EscortSteering.OffsetAt(fixedStation, 1000f));
        }

        [Test]
        public void OnStation_TheFlierMatchesTheStationsSpeed()
        {
            Assert.AreEqual(17.5f, EscortSteering.MatchedSpeed(Vector3.zero, Vector3.forward * 17.5f, 0.15f), Tolerance);
        }

        [Test]
        public void Behind_ItSpeedsUp_AndAhead_ItEasesOff()
        {
            Vector3 velocity = Vector3.forward * 17.5f;
            float behind = EscortSteering.MatchedSpeed(Vector3.forward * 20f, velocity, 0.15f);
            float ahead = EscortSteering.MatchedSpeed(Vector3.back * 20f, velocity, 0.15f);

            Assert.Greater(behind, 17.5f);
            Assert.Less(ahead, 17.5f);
            Assert.GreaterOrEqual(EscortSteering.MatchedSpeed(Vector3.back * 1000f, velocity, 0.15f), 0f, "a speed below zero");
        }

        [Test]
        public void AStillStation_IsFlownAtByDistance()
        {
            Assert.AreEqual(3f, EscortSteering.MatchedSpeed(new Vector3(0f, 0f, 20f), Vector3.zero, 0.15f), Tolerance);
        }

        [Test]
        public void TheSpeedFraction_StaysWithinItsBounds()
        {
            EscortSettings settings = NoDrift();
            var anchor = new AnchorState(Vector3.zero, Vector3.forward, Vector3.forward * 17.5f);
            FlightStation station = FlightStation.Fixed(null, Vector3.zero, 0);

            EscortStep farBehind = EscortSteering.Step(Vector3.back * 500f, anchor, station, settings, TopSpeed, -1000f, 0f, null);
            EscortStep farAhead = EscortSteering.Step(Vector3.forward * 500f, anchor, station, settings, TopSpeed, -1000f, 0f, null);

            Assert.AreEqual(settings.maxSpeed, farBehind.Speed, Tolerance);
            Assert.AreEqual(settings.minSpeed, farAhead.Speed, Tolerance, "a flier far ahead of its station should ease off, never hover");
        }

        [Test]
        public void TheTarget_LeadsTheStationAlongItsTravel()
        {
            EscortSettings settings = NoDrift();
            var anchor = new AnchorState(Vector3.zero, Vector3.forward, Vector3.forward * 10f);
            EscortStep step = EscortSteering.Step(Vector3.zero, anchor, FlightStation.Fixed(null, Vector3.zero, 0), settings,
                                                  TopSpeed, -1000f, 0f, null);

            Assert.AreEqual(10f * settings.leadSeconds, step.Target.z, Tolerance);
        }

        [Test]
        public void TheTarget_NeverDipsBelowTheClearanceOverTheGround()
        {
            EscortSettings settings = NoDrift();
            var anchor = new AnchorState(Vector3.zero, Vector3.forward, Vector3.zero);
            EscortStep step = EscortSteering.Step(Vector3.zero, anchor, FlightStation.Fixed(null, Vector3.down * 40f, 0), settings,
                                                  TopSpeed, 0f, 0f, null);

            Assert.AreEqual(settings.minClearance, step.Target.y, Tolerance);
        }

        [Test]
        public void ANeighbour_Close_PushesTheFlierAway_AndOneFarOff_DoesNot()
        {
            var near = new List<Vector3> { new Vector3(3f, 0f, 0f) };
            var far = new List<Vector3> { new Vector3(30f, 0f, 0f) };

            Vector3 push = EscortSteering.Separation(Vector3.zero, near, 9f);
            Assert.Less(push.x, 0f, "a neighbour on the right did not push the flier left");
            Assert.AreEqual(6f, push.magnitude, Tolerance);
            Assert.AreEqual(Vector3.zero, EscortSteering.Separation(Vector3.zero, far, 9f));
            Assert.AreEqual(Vector3.zero, EscortSteering.Separation(Vector3.zero, new List<Vector3> { Vector3.zero }, 9f),
                            "the flier pushed itself");
        }

        [Test]
        public void OnAFixedStation_ItHoldsTheAnchorsHeading_ButAnOrbitFacesItsTravel()
        {
            EscortSettings settings = NoDrift();
            var anchor = new AnchorState(Vector3.zero, Vector3.right, Vector3.zero);

            EscortStep onStation = EscortSteering.Step(Vector3.zero, anchor, FlightStation.Fixed(null, Vector3.zero, 0), settings,
                                                       TopSpeed, -1000f, 0f, null);
            EscortStep orbiting = EscortSteering.Step(new Vector3(0f, 0f, 50f), anchor,
                                                      FlightStation.Orbit(null, 50f, 0f, 10f, 0f, 0), settings,
                                                      TopSpeed, -1000f, 0f, null);

            Assert.IsTrue(onStation.HoldsHeading);
            Assert.That(Vector3.Distance(onStation.Heading, Vector3.right), Is.LessThan(Tolerance));
            Assert.IsFalse(orbiting.HoldsHeading);
        }

        [Test]
        public void TheDrift_StaysWithinItsAmplitude()
        {
            const float Bound = 2.5f + Tolerance;
            var settings = new EscortSettings { driftAmplitude = 2.5f };
            for (int seed = 0; seed < 8; seed++)
            for (float t = 0f; t < 600f; t += 7.3f)
            {
                Vector3 drift = EscortSteering.Drift(seed, settings, t);
                Assert.LessOrEqual(Mathf.Abs(drift.x), Bound);
                Assert.LessOrEqual(Mathf.Abs(drift.y), Bound);
                Assert.LessOrEqual(Mathf.Abs(drift.z), Bound);
            }
        }

        [Test]
        public void StationKeeping_SharesOutTopSpeed_AndLeadsByTheAnchorsMotion()
        {
            Assert.AreEqual(0.5f, StationKeeping.SpeedFraction(10f, 1f, 20f, 0.01f, 1f), Tolerance);
            Assert.AreEqual(1f, StationKeeping.SpeedFraction(1000f, 1f, 20f, 0.01f, 1f), Tolerance);
            Assert.AreEqual(0.01f, StationKeeping.SpeedFraction(0f, 1f, 20f, 0.01f, 1f), Tolerance);
            Assert.AreEqual(0.8f, StationKeeping.SpeedFraction(5f, 1f, 0f, 0.01f, 0.8f), Tolerance, "no top speed should ask for max");
            Assert.AreEqual(new Vector3(0f, 0f, 8f), StationKeeping.Led(Vector3.zero, Vector3.forward * 2f, 4f));
        }
    }
}
