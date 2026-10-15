// Which day it is, and that no restatement of the hour ever loses it.
//
// The day counter is the integer part of the same cycles the hour is the fraction of, so it is as
// machine-independent as the hour — provided every way the anchor moves carries the day with it.
// The ways it moves are: a jump to an hour, a clock handover, a save loaded, and the server's
// anchor handed to a client in the one float SkyAnchor carries. Each is checked here against a
// fixed clock reading, with no frame ever having run.
using NUnit.Framework;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class DayNightDayCounterTests
    {
        private const float Cycle = 2400f;

        private GameObject fixture;

        [TearDown]
        public void TearDown()
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            fixture = null;
        }

        private DayNightCycle NewCycle(string name)
        {
            if (fixture == null) fixture = new GameObject("day-fixture");

            var go = new GameObject(name);
            go.transform.SetParent(fixture.transform, false);

            var cycle = go.AddComponent<DayNightCycle>();
            cycle.cycleDuration = Cycle;
            return cycle;
        }

        [Test]
        public void DayIsTheWholeCyclesSinceTheAnchor()
        {
            Assert.AreEqual(0, DayNightCycle.DayAt(0, 0.25, 0d, Cycle));
            Assert.AreEqual(0, DayNightCycle.DayAt(0, 0.25, 0.7 * Cycle, Cycle));
            Assert.AreEqual(1, DayNightCycle.DayAt(0, 0.25, 0.75 * Cycle, Cycle), "midnight turns the day");
            Assert.AreEqual(4, DayNightCycle.DayAt(3, 0.9, 0.2 * Cycle, Cycle));
        }

        [Test]
        public void RestatingTheHourKeepsTheDayItIsOn()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(0, 0.25f, 0d);

            // Two and a half cycles on: day 2, three quarters through it. Moving the hour back to
            // early morning is a statement about the hour, not a trip back to day 0.
            double later = 2.5 * Cycle;
            Assert.AreEqual(2, cycle.DayAt(later));

            cycle.AnchorTo(0.1f, later);

            Assert.AreEqual(2, cycle.DayAt(later));
            Assert.AreEqual(0.1f, cycle.PhaseAt(later), 1e-5f);
            Assert.AreEqual(3, cycle.DayAt(later + Cycle), "and the next midnight still turns it");
        }

        [Test]
        public void AJoinerTakesTheDayFromTheReplicatedAnchor()
        {
            DayNightCycle host = NewCycle("host");
            DayNightCycle joiner = NewCycle("joiner");
            host.AnchorTo(5, 0.4f, 100d);

            host.ReadAnchor(out int day, out float phase, out double clock);
            joiner.AdoptAnchor(day, phase, clock);

            double probe = clock + 0.7 * Cycle;
            Assert.AreEqual(6, host.DayAt(probe));
            Assert.AreEqual(host.DayAt(probe), joiner.DayAt(probe));
            Assert.AreEqual(host.PhaseAt(probe), joiner.PhaseAt(probe), 1e-5f);
        }

        [Test]
        public void ASaveFromBeforeTheDayCounterLoadsAsDayZero()
        {
            DayNightCycle cycle = NewCycle("sun");

            cycle.RestoreTimeOfDay(0.8f);
            Assert.AreEqual(0, cycle.Day);

            cycle.RestoreTimeOfDay(0.8f, 7);
            Assert.AreEqual(7, cycle.Day);
            Assert.AreEqual(0.8f, cycle.TimeOfDay, 1e-3f);
        }

        [Test]
        public void JumpingToAnHourStaysOnTheSameDay()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.RestoreTimeOfDay(0.9f, 3);

            cycle.JumpToHour(6f);
            Assert.AreEqual(3, cycle.Day, "back to this morning");
            Assert.AreEqual(6f, cycle.HourOfDay, 0.05f);

            cycle.JumpToHour(23f);
            Assert.AreEqual(3, cycle.Day, "on to tonight");
            Assert.AreEqual(23f, cycle.HourOfDay, 0.05f);
        }

        [Test]
        public void GameMinutesCountWholeDaysPlusTheHour()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.RestoreTimeOfDay(0.5f, 2);

            Assert.AreEqual((2 + 0.5) * DayNightCycle.MinutesPerDay, cycle.GameMinutesNow, 1.0);
        }
    }
}
