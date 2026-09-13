using NUnit.Framework;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Pins the staging rule <see cref="VersusRulesUI"/> is built on: teams and team size are not
    /// independent, their product is capped at <see cref="VersusRules.MaxSeats"/>, and whichever
    /// axis the host just moved has to land where asked — the OTHER axis is what gives way.
    /// </summary>
    public class VersusRulesUITests
    {
        [TearDown]
        public void TearDown() => VersusRulesUI.ResetToDefaults();

        [Test]
        public void ResetToDefaults_RestoresTheDefaultTeamsAndSize()
        {
            VersusRulesUI.StageTeams(VersusRules.MaxTeams);
            VersusRulesUI.StageTeamSize(VersusRules.MaxTeamSize);

            VersusRulesUI.ResetToDefaults();

            Assert.AreEqual(VersusRules.DefaultTeams, VersusRulesUI.StagedTeams);
            Assert.AreEqual(VersusRules.DefaultTeamSize, VersusRulesUI.StagedTeamSize);
        }

        [Test]
        public void StagingBothAxesToTheirMax_StaysWithinTheSeatCeiling()
        {
            VersusRulesUI.StageTeams(VersusRules.MaxTeams);
            VersusRulesUI.StageTeamSize(VersusRules.MaxTeamSize);

            int seats = VersusRules.Seats(VersusRulesUI.StagedTeams, VersusRulesUI.StagedTeamSize);
            Assert.LessOrEqual(seats, VersusRules.MaxSeats);
        }

        [Test]
        public void StageTeams_ClampsRatherThanRefusesAnOutOfRangeRequest()
        {
            VersusRulesUI.StageTeams(99);
            Assert.LessOrEqual(VersusRulesUI.StagedTeams, VersusRules.MaxTeams);

            VersusRulesUI.StageTeams(0);
            Assert.GreaterOrEqual(VersusRulesUI.StagedTeams, VersusRules.MinTeams);
        }

        [Test]
        public void StageTeamSize_ClampsRatherThanRefusesAnOutOfRangeRequest()
        {
            VersusRulesUI.StageTeamSize(99);
            Assert.LessOrEqual(VersusRulesUI.StagedTeamSize, VersusRules.MaxTeamSize);

            VersusRulesUI.StageTeamSize(0);
            Assert.GreaterOrEqual(VersusRulesUI.StagedTeamSize, VersusRules.MinTeamSize);
        }

        /// <summary>
        /// Raising teams while team size is already at its own maximum has to shrink team size, not
        /// refuse the team count the host just asked for — and the axis actually moved (teams) has
        /// to land exactly where asked.
        /// </summary>
        [Test]
        public void MovingTeamsAfterMaxingTeamSize_TheMovedAxisWinsAndTheOtherGivesWay()
        {
            VersusRulesUI.StageTeamSize(VersusRules.MaxTeamSize);
            VersusRulesUI.StageTeams(VersusRules.MaxTeams);

            Assert.AreEqual(VersusRules.MaxTeams, VersusRulesUI.StagedTeams);
            int seats = VersusRules.Seats(VersusRulesUI.StagedTeams, VersusRulesUI.StagedTeamSize);
            Assert.LessOrEqual(seats, VersusRules.MaxSeats);
        }

        [Test]
        public void DescribeSeats_ReportsTheProductAndTheCeiling()
        {
            string description = VersusRulesUI.DescribeSeats(3, 4);

            StringAssert.Contains("12", description);
            StringAssert.Contains(VersusRules.MaxSeats.ToString(), description);
        }
    }
}
