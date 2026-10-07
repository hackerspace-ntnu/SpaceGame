using NUnit.Framework;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class CrewShiftLogicTests
    {
        private static CrewCensus Crew(int living, int aboard, int fighting = 0) => new CrewCensus(living, aboard, fighting);

        [Test]
        public void Marching_StaysAboard() =>
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Aboard, atStop: false, Crew(6, 6)));

        [Test]
        public void AStop_StartsTheDisembark() =>
            Assert.AreEqual(CrewState.Disembarking, CrewShiftLogic.Next(CrewState.Aboard, atStop: true, Crew(6, 6)));

        [Test]
        public void Disembark_EndsAshore_WhenNobodyIsLeftAboard()
        {
            Assert.AreEqual(CrewState.Disembarking, CrewShiftLogic.Next(CrewState.Disembarking, true, Crew(6, 2)));
            Assert.AreEqual(CrewState.Ashore, CrewShiftLogic.Next(CrewState.Disembarking, true, Crew(6, 0)));
        }

        [Test]
        public void TheStayEnding_RecallsFromAshore_AndFromAHalfFinishedDisembark()
        {
            Assert.AreEqual(CrewState.Recalling, CrewShiftLogic.Next(CrewState.Ashore, atStop: false, Crew(6, 0)));
            Assert.AreEqual(CrewState.Recalling, CrewShiftLogic.Next(CrewState.Disembarking, atStop: false, Crew(6, 3)));
        }

        [Test]
        public void Recall_EndsAboard_OnlyWhenEveryLivingMemberIs()
        {
            Assert.AreEqual(CrewState.Recalling, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(6, 5)));
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(6, 6)));
        }

        [Test]
        public void DeadCrewDoNotBlockTheRecall() =>
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(living: 4, aboard: 4)));

        [Test]
        public void AnEmptyHouse_IsAboard() =>
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(0, 0)));

        [Test]
        public void AFightPausesTheRecallClock()
        {
            Assert.AreEqual(10f, CrewShiftLogic.AdvanceRecallClock(10f, 1f, Crew(6, 2, fighting: 1)));
            Assert.AreEqual(11f, CrewShiftLogic.AdvanceRecallClock(10f, 1f, Crew(6, 2)));
        }

        [Test]
        public void TimeoutSeatsStragglers_ButNeverMidFight()
        {
            Assert.IsFalse(CrewShiftLogic.ShouldForceBoard(59f, 60f, Crew(6, 5)));
            Assert.IsTrue(CrewShiftLogic.ShouldForceBoard(60f, 60f, Crew(6, 5)));
            Assert.IsFalse(CrewShiftLogic.ShouldForceBoard(90f, 60f, Crew(6, 5, fighting: 1)));
        }
    }
}
