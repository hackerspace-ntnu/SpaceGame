// The settlement's population clock, on the pure logic: a town that does not spawn the frame it
// loads, refills only its shortfall, never more than a wave, and waits out an alarm.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SettlementPopulationTests
    {
        private const float Interval = 60f;

        [Test]
        public void TheFirstTickArmsTheClockAndSpawnsNothing()
        {
            var state = new SettlementPopulationLogic.State();
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, alive: 0, maxPopulation: 12, perWave: 2, hold: false, now: 100f, Interval, 0, 0f));
            Assert.IsTrue(state.Armed);
            Assert.AreEqual(160f, state.NextWaveAt, 1e-3f);
        }

        [Test]
        public void AWaveSpawnsTheShortfallCappedToTheWaveSize()
        {
            var state = new SettlementPopulationLogic.State();
            SettlementPopulationLogic.Step(ref state, 5, 12, 2, false, 0f, Interval, 0, 0f);

            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 5, 12, 2, false, 30f, Interval, 0, 0f), "not yet");
            Assert.AreEqual(2, SettlementPopulationLogic.Step(ref state, 5, 12, 2, false, 60f, Interval, 0, 0f), "seven short, two per wave");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 7, 12, 2, false, 61f, Interval, 0, 0f), "the clock restarted");
            Assert.AreEqual(1, SettlementPopulationLogic.Step(ref state, 11, 12, 2, false, 121f, Interval, 0, 0f), "one short, one spawned");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 12, 12, 2, false, 181f, Interval, 0, 0f), "full");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 20, 12, 2, false, 241f, Interval, 0, 0f), "over-full spawns nothing, never a negative");
        }

        [Test]
        public void AnAlarmHoldsTheWaveAndRestartsTheClockWhenItLowers()
        {
            var state = new SettlementPopulationLogic.State();
            SettlementPopulationLogic.Step(ref state, 2, 12, 2, false, 0f, Interval, 0, 0f);

            // Raised from t=50 to t=90: the wave due at 60 does not happen, and the next one is a
            // full interval after the alarm lowered, not at 120.
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 2, 12, 2, true, 50f, Interval, 0, 0f));
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 2, 12, 2, true, 90f, Interval, 0, 0f));
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 2, 12, 2, false, 120f, Interval, 0, 0f), "the quiet after a fight is a whole interval");
            Assert.AreEqual(2, SettlementPopulationLogic.Step(ref state, 2, 12, 2, false, 150f, Interval, 0, 0f));
        }

        [Test]
        public void InitialWavesFillTheTownQuicklyThenTheNormalPaceResumes()
        {
            const float Quick = 1f;
            var state = new SettlementPopulationLogic.State();
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 0, 16, 4, false, 0f, Interval, 4, Quick), "arming still spawns nothing");
            Assert.AreEqual(4, SettlementPopulationLogic.Step(ref state, 0, 16, 4, false, 1f, Interval, 4, Quick));
            Assert.AreEqual(4, SettlementPopulationLogic.Step(ref state, 4, 16, 4, false, 2f, Interval, 4, Quick));
            Assert.AreEqual(4, SettlementPopulationLogic.Step(ref state, 8, 16, 4, false, 3f, Interval, 4, Quick));
            Assert.AreEqual(4, SettlementPopulationLogic.Step(ref state, 12, 16, 4, false, 4f, Interval, 4, Quick), "full after four quick waves");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 15, 16, 4, false, 5f, Interval, 4, Quick), "then a whole interval");
            Assert.AreEqual(1, SettlementPopulationLogic.Step(ref state, 15, 16, 4, false, 4f + Interval, Interval, 4, Quick));
        }

        [Test]
        public void NoInitialWavesIsTheOldPace()
        {
            // initialWaves = 0 must give the old pace no matter what initialInterval says -- it is
            // never consulted once there are no initial waves to space out. Two very different
            // interval values must therefore trace identically.
            var withZeroInterval = new SettlementPopulationLogic.State();
            var withNonZeroInterval = new SettlementPopulationLogic.State();
            foreach (float now in new[] { 0f, 1f, 30f, 59f, 60f, 61f, 120f })
                Assert.AreEqual(SettlementPopulationLogic.Step(ref withZeroInterval, 0, 12, 2, false, now, Interval, 0, 0f),
                                SettlementPopulationLogic.Step(ref withNonZeroInterval, 0, 12, 2, false, now, Interval, 0, 1f),
                                $"t={now}");
        }

        [Test]
        public void PickFollowsTheWeights()
        {
            var mix = new[]
            {
                new SettlementPopulation.Inhabitant { prefab = null, weight = 3 },
                new SettlementPopulation.Inhabitant { prefab = null, weight = 1 },
                new SettlementPopulation.Inhabitant { prefab = null, weight = 0 },
            };
            Assert.AreEqual(0, SettlementPopulationLogic.Pick(mix, 0f));
            Assert.AreEqual(0, SettlementPopulationLogic.Pick(mix, 0.74f));
            Assert.AreEqual(1, SettlementPopulationLogic.Pick(mix, 0.76f));
            Assert.AreEqual(1, SettlementPopulationLogic.Pick(mix, 1f), "the top of the range is the last weighted entry, never the zero one");
            Assert.AreEqual(-1, SettlementPopulationLogic.Pick(new[] { mix[2] }, 0.5f), "nothing weighted, nothing picked");
        }
    }
}
