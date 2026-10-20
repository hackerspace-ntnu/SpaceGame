using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// One quest the crew have taken on: its <see cref="ObjectiveChain"/>, their shared place in it, and what its
    /// steps remember (<see cref="World"/>). Every started quest advances on its own whether or not anyone is
    /// following it on the visor; <see cref="QuestLog"/> only chooses which one to show.
    ///
    /// <para>
    /// Owned by <see cref="ObjectiveDirector"/>, which decides when each member runs: the SERVER members
    /// (<see cref="Advance"/>, <see cref="RecordFinished"/>) only where <c>Network.Decides</c>, the rest everywhere.
    /// </para>
    /// </summary>
    public sealed class Quest : IQuestEntry
    {
        private ObjectiveProgress progress;

        /// <summary>The step this machine last reported its own player finished, so it is sent once.</summary>
        private int reportedStep = -1;

        private TrackedStep tracked;

        public Quest(ObjectiveChain chain, int order)
        {
            Chain = chain;
            Order = order;
        }

        public ObjectiveChain Chain { get; }

        /// <summary>When the crew took it on, relative to the other quests: 0 for the story quest, then 1, 2, …</summary>
        public int Order { get; }

        /// <summary>What this quest's steps may ask about the world, and remember during the current step.</summary>
        public ObjectiveWorld World { get; } = new();

        public ObjectiveProgress Progress => progress;

        public string Id => Chain.Id;
        public string Name => Chain.Title;
        public string Summary => Chain.Summary;

        public bool IsComplete => progress.Step >= Chain.Steps.Count;

        /// <summary>The step the crew are on in this quest, or null once it is finished.</summary>
        public ObjectiveStep Current => IsComplete ? null : Chain.Steps[progress.Step];

        /// <summary>The current step's id, or null once the quest is finished.</summary>
        public string CurrentStepId => Current != null ? Current.Id : null;

        public ITrackedObjective Objective
        {
            get
            {
                ObjectiveStep step = Current;
                if (step == null) return null;
                if (tracked == null || tracked.Step != step) tracked = new TrackedStep(step, World);
                return tracked;
            }
        }

        // ── EVERY machine ───────────────────────────────────────────────────────

        /// <summary>
        /// Watches this machine's own player for a step every player must do themselves. True once per step, the
        /// frame they finish their part; the director carries it to the server.
        /// </summary>
        public bool TrackLocalPlayer()
        {
            ObjectiveStep step = Current;
            if (step == null || !progress.Begun || reportedStep == progress.Step) return false;
            if (!step.TrackLocalPlayer(World)) return false;

            reportedStep = progress.Step;
            return true;
        }

        /// <summary>Everywhere but the server: the server's progress, as replicated. True when it changed anything.</summary>
        public bool Adopt(ObjectiveProgress replicated, bool live)
        {
            if (replicated.Equals(progress)) return false;

            // Joining: whatever this machine remembered belongs to no session it is in now.
            if (!live) ForgetStep();

            SetProgress(replicated);
            return true;
        }

        /// <summary>Restore-only: puts the crew back where a save says, without replaying anything.</summary>
        public void Restore(ObjectiveProgress restored)
        {
            ForgetStep();
            SetProgress(restored);
        }

        public SavedQuest ToSaved() => new()
        {
            Id = Id,
            Order = Order,
            Step = CurrentStepId,
            Complete = IsComplete,
            Begun = progress.Begun,
            Remarks = progress.Remarks,
        };

        // ── SERVER ──────────────────────────────────────────────────────────────

        /// <summary>SERVER: begins the current step if it has not begun, and moves on once it is met. True when anything changed.</summary>
        public bool Advance()
        {
            if (IsComplete) return false;

            bool changed = TryBeginCurrent();

            // Read after TryBeginCurrent, which may have skipped an empty slot.
            ObjectiveStep step = Current;
            if (step != null && progress.Begun && SayDueRemarks(step)) changed = true;

            if (step != null && progress.Begun && step.IsMet(World))
            {
                SetProgress(new ObjectiveProgress { Step = progress.Step + 1 });

                // Straight on into the next step's setup, so a peer usually hears the step and its
                // begun flag in one change rather than two.
                if (!IsComplete) TryBeginCurrent();
                changed = true;
            }

            return changed;
        }

        /// <summary>SERVER: the player on <paramref name="clientId"/> has done their part of step <paramref name="step"/>.</summary>
        public void RecordFinished(ulong clientId, int step)
        {
            // A report that crossed a step change on the wire is about a step the crew have left.
            if (step == progress.Step) World.RecordFinished(clientId);
        }

        /// <summary>
        /// Runs the current step's setup if it has not run. True when it ran now. An empty slot in
        /// the chain is skipped rather than blocking it, loudly.
        /// </summary>
        private bool TryBeginCurrent()
        {
            if (progress.Begun) return false;

            ObjectiveStep step = Chain.Steps[progress.Step];
            if (step == null)
            {
                Debug.LogError($"[Objectives] Slot {progress.Step} of '{Chain.name}' is empty. Skipping it.", Chain);
                SetProgress(new ObjectiveProgress { Step = progress.Step + 1 });
                return true;
            }

            if (!step.TryBegin(World)) return false;

            progress.Begun = true;
            return true;
        }

        /// <summary>SERVER: marks every remark of <paramref name="step"/> that has come due as said. True when any did.</summary>
        private bool SayDueRemarks(ObjectiveStep step)
        {
            int count = Mathf.Min(step.RemarkCount, ObjectiveStep.MaxRemarks);
            int said = progress.Remarks;

            for (int i = 0; i < count; i++)
                if ((said & (1 << i)) == 0 && step.IsRemarkDue(World, i))
                    said |= 1 << i;

            if (said == progress.Remarks) return false;

            progress.Remarks = said;
            return true;
        }

        private void SetProgress(ObjectiveProgress value)
        {
            if (value.Step != progress.Step) ForgetStep();
            progress = value;
        }

        /// <summary>Drops everything remembered about the current step, on this machine.</summary>
        private void ForgetStep()
        {
            World.ForgetStep();
            reportedStep = -1;
        }

        private sealed class TrackedStep : ITrackedObjective
        {
            public readonly ObjectiveStep Step;
            private readonly ObjectiveWorld world;

            public TrackedStep(ObjectiveStep step, ObjectiveWorld world)
            {
                Step = step;
                this.world = world;
            }

            public string Title => Step.Title;
            public string Status => Step.Status(world);
            public bool TryGetWaypoint(out Vector3 position) => Step.TryGetWaypoint(world, out position);
        }
    }

    /// <summary>
    /// A quest as a save file holds it: by ids, never by position, so quests and steps can be added, removed and
    /// reordered under existing saves.
    /// </summary>
    public struct SavedQuest
    {
        /// <summary><see cref="ObjectiveChain.Id"/>.</summary>
        public string Id;

        /// <summary><see cref="Quest.Order"/>.</summary>
        public int Order;

        /// <summary>The current step's id. Null once the quest is finished.</summary>
        public string Step;

        public bool Complete;

        /// <summary>The current step's one-time setup has already run.</summary>
        public bool Begun;

        /// <summary>Which of the current step's remarks have been said, by bit.</summary>
        public int Remarks;
    }
}
