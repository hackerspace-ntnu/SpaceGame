using NUnit.Framework;
using SpaceGame.Gameplay.Arena;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Arena rounds and arena damage. Rounds: last side standing takes it, both down together is a
    /// replay. Damage: nothing outside a live round, nothing from a teammate, and no hit worth more
    /// than the cap — the cap is the promise that nobody is ever one-shot.
    /// </summary>
    public class ArenaRulesTests
    {
        private const float Scale = 0.6f;
        private const int Cap = 34;

        [TestCase(2, 1, ArenaRules.Undecided)]
        [TestCase(1, 0, 0)]
        [TestCase(0, 3, 1)]
        [TestCase(0, 0, ArenaRules.Draw)]
        public void RoundGoesToTheLastSideStanding(int alive0, int alive1, int expected)
        {
            Assert.AreEqual(expected, ArenaRules.EvaluateRound(alive0, alive1));
        }

        [TestCase(3, 1, 3, 0)]
        [TestCase(2, 3, 3, 1)]
        [TestCase(2, 2, 3, -1)]
        [TestCase(1, 0, 0, 0)]
        public void MatchGoesToTheFirstTeamAtTheTarget(int score0, int score1, int target, int expected)
        {
            Assert.AreEqual(expected, ArenaRules.MatchWinner(score0, score1, target));
        }

        [Test]
        public void RoundNeedsBothSidesPopulated()
        {
            Assert.IsTrue(ArenaRules.CanStartRound(1, 1));
            Assert.IsFalse(ArenaRules.CanStartRound(2, 0));
            Assert.IsFalse(ArenaRules.CanStartRound(0, 0));
        }

        [Test]
        public void NothingLandsOutsideALiveRound()
        {
            Assert.AreEqual(0, ArenaDamageRules.Resolve(50, 0, 1, false, fighting: false, Scale, Cap));
        }

        [Test]
        public void TeammatesCannotHurtEachOther()
        {
            Assert.AreEqual(0, ArenaDamageRules.Resolve(50, 1, 1, isSelf: false, true, Scale, Cap));
        }

        [Test]
        public void YourOwnBackfireStillLands()
        {
            Assert.AreEqual(15, ArenaDamageRules.Resolve(25, 1, 1, isSelf: true, true, Scale, Cap));
        }

        [Test]
        public void EnemyHitsAreScaled()
        {
            Assert.AreEqual(11, ArenaDamageRules.Resolve(18, 0, 1, false, true, Scale, Cap));
        }

        [Test]
        public void NoHitIsWorthMoreThanTheCap()
        {
            Assert.AreEqual(Cap, ArenaDamageRules.Resolve(150, 0, 1, false, true, Scale, Cap));
        }

        [Test]
        public void ThreeCappedHitsAreNeededToKill()
        {
            Assert.Less(2 * ArenaDamageRules.Resolve(1000, 0, 1, false, true, Scale, Cap), 100);
        }

        [Test]
        public void HitsWithNoPlayerBehindThemStillLand()
        {
            Assert.AreEqual(6, ArenaDamageRules.Resolve(10, ArenaDamageRules.NoTeam, 1, false, true, Scale, Cap));
        }

        [Test]
        public void AWeakHitStillChips()
        {
            Assert.AreEqual(1, ArenaDamageRules.Resolve(1, 0, 1, false, true, Scale, Cap));
        }
    }
}
