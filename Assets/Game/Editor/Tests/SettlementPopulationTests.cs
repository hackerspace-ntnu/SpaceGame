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
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, alive: 0, maxPopulation: 12, perWave: 2, hold: false, now: 100f, Interval));
            Assert.IsTrue(state.Armed);
            Assert.AreEqual(160f, state.NextWaveAt, 1e-3f);
        }

        [Test]
        public void AWaveSpawnsTheShortfallCappedToTheWaveSize()
        {
            var state = new SettlementPopulationLogic.State();
            SettlementPopulationLogic.Step(ref state, 5, 12, 2, false, 0f, Interval);

            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 5, 12, 2, false, 30f, Interval), "not yet");
            Assert.AreEqual(2, SettlementPopulationLogic.Step(ref state, 5, 12, 2, false, 60f, Interval), "seven short, two per wave");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 7, 12, 2, false, 61f, Interval), "the clock restarted");
            Assert.AreEqual(1, SettlementPopulationLogic.Step(ref state, 11, 12, 2, false, 121f, Interval), "one short, one spawned");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 12, 12, 2, false, 181f, Interval), "full");
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 20, 12, 2, false, 241f, Interval), "over-full spawns nothing, never a negative");
        }

        [Test]
        public void AnAlarmHoldsTheWaveAndRestartsTheClockWhenItLowers()
        {
            var state = new SettlementPopulationLogic.State();
            SettlementPopulationLogic.Step(ref state, 2, 12, 2, false, 0f, Interval);

            // Raised from t=50 to t=90: the wave due at 60 does not happen, and the next one is a
            // full interval after the alarm lowered, not at 120.
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 2, 12, 2, true, 50f, Interval));
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 2, 12, 2, true, 90f, Interval));
            Assert.AreEqual(0, SettlementPopulationLogic.Step(ref state, 2, 12, 2, false, 120f, Interval), "the quiet after a fight is a whole interval");
            Assert.AreEqual(2, SettlementPopulationLogic.Step(ref state, 2, 12, 2, false, 150f, Interval));
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
