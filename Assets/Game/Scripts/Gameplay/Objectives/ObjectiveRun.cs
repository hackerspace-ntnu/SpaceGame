namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// One chain being worked through: the chain, the crew's place in it, and what its current step
    /// remembers. The director holds one per chain — the main chain the crew start on landing, and
    /// any side chains, each started when somebody accepts it.
    ///
    /// <para>
    /// Every run has its own <see cref="ObjectiveWorld"/>, so a step change in one chain never wipes
    /// what a step in another has recorded.
    /// </para>
    /// </summary>
    public sealed class ObjectiveRun
    {
        internal ObjectiveRun(ObjectiveChain chain, int index, bool startsOnLanding)
        {
            Chain = chain;
            Index = index;
            StartsOnLanding = startsOnLanding;
        }

        public ObjectiveChain Chain { get; }

        /// <summary>Position among the director's runs; the main chain is 0. The same on every machine.</summary>
        public int Index { get; }

        /// <summary>The main chain: under way from the landing, never accepted.</summary>
        public bool StartsOnLanding { get; }

        public ObjectiveProgress Progress { get; internal set; }

        public ObjectiveWorld World { get; } = new();

        /// <summary>The step this machine last reported its own player finished, so it is sent once.</summary>
        internal int ReportedStep { get; set; } = -1;

        public bool IsStarted => StartsOnLanding || Progress.Started;

        public bool IsComplete => Progress.Step >= Chain.Steps.Count;

        /// <summary>Started and not finished: there is a step to show.</summary>
        public bool IsActive => IsStarted && !IsComplete;

        /// <summary>The step the crew are on in this chain, or null when it has not started or is finished.</summary>
        public ObjectiveStep Current => IsActive ? Chain.Steps[Progress.Step] : null;

        /// <summary>The current step's id, or null once the chain is finished.</summary>
        public string CurrentStepId => IsComplete || Chain.Steps[Progress.Step] == null
            ? null
            : Chain.Steps[Progress.Step].Id;
    }
}
