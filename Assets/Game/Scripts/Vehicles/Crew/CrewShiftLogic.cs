// The decisions a walking city's house makes about its crew, with nothing in them but numbers:
// when to put them ashore, when to call them back, and when to stop waiting. CrewShift feeds it
// what it counts and does what it says; this file never touches a scene.
namespace SpaceGame.Vehicles
{
    public enum CrewState { Aboard, Disembarking, Ashore, Recalling }

    public readonly struct CrewCensus
    {
        public readonly int Living;
        public readonly int Aboard;
        public readonly int Fighting;

        public CrewCensus(int living, int aboard, int fighting)
        {
            Living = living;
            Aboard = aboard;
            Fighting = fighting;
        }

        public bool AllAboard => Aboard >= Living;
    }

    public static class CrewShiftLogic
    {
        /// <param name="atStop">The column's leader is dwelling with time left on its stay.</param>
        public static CrewState Next(CrewState state, bool atStop, CrewCensus crew)
        {
            switch (state)
            {
                case CrewState.Aboard:
                    return atStop && crew.Living > 0 ? CrewState.Disembarking : CrewState.Aboard;
                case CrewState.Disembarking:
                    if (!atStop) return CrewState.Recalling;
                    return crew.Aboard == 0 ? CrewState.Ashore : CrewState.Disembarking;
                case CrewState.Ashore:
                    return atStop ? CrewState.Ashore : CrewState.Recalling;
                default:
                    return crew.AllAboard ? CrewState.Aboard : CrewState.Recalling;
            }
        }

        /// <summary>The recall clock does not run while anyone is fighting: nobody is hurried aboard mid-fight.</summary>
        public static float AdvanceRecallClock(float elapsed, float deltaTime, CrewCensus crew) =>
            crew.Fighting > 0 ? elapsed : elapsed + deltaTime;

        /// <summary>Seat whoever is still ashore, rather than strand the city on a straggler.</summary>
        public static bool ShouldForceBoard(float recallElapsed, float recallTimeout, CrewCensus crew) =>
            crew.Fighting == 0 && recallElapsed >= recallTimeout;
    }
}
