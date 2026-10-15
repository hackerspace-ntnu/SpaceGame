using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>The NPC craft's wings, derived from how it moves: what the wing animator and audio are fed.</summary>
    public class NpcWingsTests
    {
        private const float Dt = 1f / 60f;
        private static readonly NpcWingSettings Cfg = new NpcWingSettings();

        private static NpcWingState Run(Vector3 velocity, float seconds, float bank = 0f, float turnRate = 0f)
        {
            var s = new NpcWingState();
            for (float t = 0f; t < seconds; t += Dt) s = NpcWings.Step(s, velocity, bank, turnRate, Dt, Cfg);
            return s;
        }

        [Test]
        public void AParkedCraft_KeepsItsWingsShut()
        {
            Assert.AreEqual(0f, Run(Vector3.zero, 2f).WingSpread);
        }

        [Test]
        public void ACruisingCraft_OpensItsWingsAndBeatsThem()
        {
            NpcWingState s = Run(new Vector3(0f, 0f, 25f), 2f);
            Assert.AreEqual(1f, s.WingSpread, 1e-3f);
            Assert.Greater(s.FlapEffort, 0f);
            Assert.AreEqual(25f, s.Airspeed, 1e-3f);
        }

        [Test]
        public void Climbing_BeatsHarderThanLevelFlight()
        {
            Assert.Greater(Run(new Vector3(0f, 5f, 24f), 1f).FlapEffort, Run(new Vector3(0f, 0f, 25f), 1f).FlapEffort);
        }

        [Test]
        public void AWreckFallingFast_HalfFoldsItsWings_AndStopsBeating()
        {
            NpcWingState s = Run(new Vector3(10f, -20f, 0f), 3f);
            Assert.AreEqual(Cfg.WreckSpread, s.WingSpread, 1e-3f);
            Assert.AreEqual(0f, s.FlapEffort);
        }

        [Test]
        public void TurningRight_ReadsAsARightTurn_AndPassesTheBankThrough()
        {
            NpcWingState s = Run(new Vector3(0f, 0f, 25f), 0.5f, bank: 20f, turnRate: 30f);
            Assert.Greater(s.Turn, 0f);
            Assert.AreEqual(20f, s.Bank, 1e-3f);
        }
    }
}
