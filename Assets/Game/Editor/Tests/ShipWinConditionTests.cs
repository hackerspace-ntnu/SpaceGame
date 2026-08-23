// Winning the run, from the ship's side.
//
// The half of this that broke multiplayer — GameManager.WinGame loading the win scene through
// UnityEngine's SceneManager, which moves only the host and leaves everyone else in a world with no
// server — cannot be reached from here: it ends in a scene load either way, and its authority guard
// needs a NetworkManager that is genuinely listening, which an EditMode test has no way to produce.
// That half is MultiplayerAutotest's, and the routed load is asserted by reading the code.
//
// What IS worth pinning is the count in front of it. Ship.AddScrap is the only caller of WinGame in
// the project, so anything that lets it fire early — or throw on the way — is a scene change nobody
// asked for, for everybody in the session at once.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class ShipWinConditionTests
    {
        private GameObject host;
        private Ship ship;

        [SetUp]
        public void SetUp()
        {
            // These deposits deliberately reach the threshold, so a GameManager left behind by
            // something else would turn them into a real scene load out of the test runner.
            Assume.That(GameManager.Instance, Is.Null,
                "A GameManager is alive in the editor; this test would try to win an actual game.");

            host = new GameObject("Ship");
            ship = host.AddComponent<Ship>();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void ScrapIsCountedUpToTheThreshold()
        {
            Assert.AreEqual(0, ship.ScrapCollected);

            for (int i = 1; i < ship.ScrapToWin; i++)
            {
                ship.AddScrap();
                Assert.AreEqual(i, ship.ScrapCollected,
                    "Every deposit counts once, on the machine allowed to count it.");
            }
        }

        [Test]
        public void ReachingTheThresholdWithNoGameManagerIsNotAnError()
        {
            // The state a test scene is in, and the state the world is in for the first frames after
            // a load. CheckWin's null guard is what keeps "there is nothing to win yet" from being a
            // NullReferenceException thrown out of an interaction.
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < ship.ScrapToWin; i++) ship.AddScrap();
            });

            Assert.AreEqual(ship.ScrapToWin, ship.ScrapCollected);
        }

        [Test]
        public void PastTheThresholdItKeepsCounting()
        {
            // Not a rule anybody needs, but it pins that the threshold is a comparison rather than
            // an equality: a deposit that lands after the win must not silently reset the run.
            for (int i = 0; i < ship.ScrapToWin + 2; i++) ship.AddScrap();

            Assert.AreEqual(ship.ScrapToWin + 2, ship.ScrapCollected);
        }
    }
}
