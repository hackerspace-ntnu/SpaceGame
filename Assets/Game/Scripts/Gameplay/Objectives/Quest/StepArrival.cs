namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// "The crew just reached this step", exactly once per arrival, for presentation that belongs to
    /// the moment — a bolt of lightning, a cutscene. Feed it every <see cref="ObjectiveDirector.Changed"/>.
    ///
    /// <para>
    /// A client can hear a step and its <c>Begun</c> flag as two changes, so the second must not count
    /// again; and a reload or a late join that lands on the step counts as already arrived, because
    /// what plays belongs to the moment and they missed it.
    /// </para>
    /// </summary>
    public struct StepArrival
    {
        private bool arrived;

        /// <summary>True the first time a live change puts <paramref name="chain"/> on <paramref name="stepId"/>.</summary>
        public bool Notice(ObjectiveRun run, bool live, ObjectiveChain chain, string stepId)
        {
            if (run.Chain != chain) return false;

            if (run.CurrentStepId != stepId)
            {
                arrived = false;
                return false;
            }

            if (arrived) return false;
            arrived = true;
            return live;
        }
    }
}
