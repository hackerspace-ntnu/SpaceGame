using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    public class MatchWinEvaluatorTests
    {
        [Test]
        public void KillTarget_NoTeamReachedTarget_ReturnsNoWinner()
        {
            var kills = new System.Collections.Generic.Dictionary<int, int> { { 0, 3 }, { 1, 4 } };
            int? winner = MatchWinEvaluator.EvaluateKillTarget(kills, target: 5);
            Assert.IsNull(winner);
        }

        [Test]
        public void KillTarget_TeamReachedTarget_ReturnsThatTeam()
        {
            var kills = new System.Collections.Generic.Dictionary<int, int> { { 0, 3 }, { 1, 5 } };
            int? winner = MatchWinEvaluator.EvaluateKillTarget(kills, target: 5);
            Assert.AreEqual(1, winner);
        }

        [Test]
        public void KillTarget_BothTeamsReachedTarget_ReturnsHigherScoringTeam()
        {
            var kills = new System.Collections.Generic.Dictionary<int, int> { { 0, 6 }, { 1, 5 } };
            int? winner = MatchWinEvaluator.EvaluateKillTarget(kills, target: 5);
            Assert.AreEqual(0, winner);
        }

        [Test]
        public void LivesRemaining_OneTeamAtZero_ReturnsOtherTeam()
        {
            var lives = new System.Collections.Generic.Dictionary<int, int> { { 0, 0 }, { 1, 3 } };
            int? winner = MatchWinEvaluator.EvaluateLivesExhausted(lives);
            Assert.AreEqual(1, winner);
        }

        [Test]
        public void LivesRemaining_BothTeamsHaveLives_ReturnsNoWinner()
        {
            var lives = new System.Collections.Generic.Dictionary<int, int> { { 0, 1 }, { 1, 2 } };
            int? winner = MatchWinEvaluator.EvaluateLivesExhausted(lives);
            Assert.IsNull(winner);
        }

        [Test]
        public void LivesRemaining_AllTeamsAtZero_ReturnsNull_NoWinnerDraw()
        {
            var lives = new System.Collections.Generic.Dictionary<int, int> { { 0, 0 }, { 1, 0 } };
            int? winner = MatchWinEvaluator.EvaluateLivesExhausted(lives);
            Assert.IsNull(winner);
        }

        [Test]
        public void LastStanding_OneTeamHasLivingMembers_ReturnsThatTeam()
        {
            var livingCounts = new System.Collections.Generic.Dictionary<int, int> { { 0, 0 }, { 1, 2 } };
            int? winner = MatchWinEvaluator.EvaluateLastStanding(livingCounts);
            Assert.AreEqual(1, winner);
        }

        [Test]
        public void LastStanding_MultipleTeamsHaveLivingMembers_ReturnsNoWinner()
        {
            var livingCounts = new System.Collections.Generic.Dictionary<int, int> { { 0, 1 }, { 1, 2 } };
            int? winner = MatchWinEvaluator.EvaluateLastStanding(livingCounts);
            Assert.IsNull(winner);
        }
    }
}
