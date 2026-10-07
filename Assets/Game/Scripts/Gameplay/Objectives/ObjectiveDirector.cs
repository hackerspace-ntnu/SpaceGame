using System;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Walks the crew through the <see cref="ObjectiveChain"/>: one place in it, shared by everyone
    /// in the session.
    ///
    /// <para>
    /// <b>The server decides, every machine presents.</b> Only where <see cref="Network.Decides"/>
    /// does this begin steps and judge them met; everywhere else the progress arrives through
    /// <see cref="ObjectiveNetwork"/> into <see cref="Adopt"/>. The briefing, the visor and the
    /// beacon all read <see cref="Progress"/> and <see cref="World"/> on their own machine and add
    /// nothing to the wire. The one thing a client sends is "my player has done their part" for a
    /// step every player must do themselves (<see cref="LocalPlayerFinished"/>).
    /// </para>
    /// <para>
    /// <b>Story worlds only.</b> A versus match has one hull per team and no story, so the chain
    /// stands still there, and nothing is saved for it.
    /// </para>
    /// <para>
    /// Lives in <c>persistentScene</c>. The chain does not start until the crew have landed — the
    /// bodies are spawned at the hull and seated a frame later, and nothing may be judged while
    /// the ship is still in the air.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]   // awake before the presenters that bind to it in their OnEnable
    public class ObjectiveDirector : MonoBehaviour
    {
        public static ObjectiveDirector Instance { get; private set; }

        [SerializeField] private ObjectiveChain chain;

        private ObjectiveProgress progress;

        /// <summary>The step this machine last reported its own player finished, so it is sent once.</summary>
        private int reportedStep = -1;

        /// <summary>
        /// The progress changed on this machine. True when it happened in play — a step reached, a
        /// step begun — and false when it was restored from a save or read off the wire on joining,
        /// which a briefing must not replay.
        /// </summary>
        public event Action<bool> Changed;

        /// <summary>
        /// This machine's own player has done their part of the step at this index. Raised once
        /// per step; <see cref="ObjectiveNetwork"/> carries it to the server.
        /// </summary>
        public event Action<int> LocalPlayerFinished;

        public ObjectiveWorld World { get; } = new();

        public ObjectiveProgress Progress => progress;

        /// <summary>A story world with a chain to run.</summary>
        public bool IsRunning => chain != null && !VersusSession.IsActive;

        public bool IsComplete => chain == null || progress.Step >= chain.Steps.Count;

        /// <summary>The step the crew are on, or null when there is none — finished, or not a story world.</summary>
        public ObjectiveStep Current => IsRunning && !IsComplete ? chain.Steps[progress.Step] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Objectives] A second ObjectiveDirector was destroyed; the crew have " +
                               "one place in one chain.", this);
                Destroy(this);
                return;
            }

            Instance = this;

            if (chain == null)
                Debug.LogError("[Objectives] No chain assigned, so the crew have no objectives.", this);
        }

        private void OnEnable()
        {
            // A duplicate being destroyed still gets OnEnable this frame; only the survivor records.
            if (Instance == this) UseChannel.UsedOnServer += OnItemUsed;
        }

        private void OnDisable() => UseChannel.UsedOnServer -= OnItemUsed;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnItemUsed(InventoryItem item, GameObject holder) => World.RecordUse(item);

        private void Update()
        {
            if (!IsRunning || IsComplete) return;

            ObjectiveStep step = Current;
            if (step != null && progress.Begun) TrackLocalPlayer(step);
            if (Network.Decides && CrewHasLanded) Advance();
        }

        /// <summary>EVERY machine: tells the server, once per step, when this machine's player has done their part.</summary>
        private void TrackLocalPlayer(ObjectiveStep step)
        {
            if (reportedStep == progress.Step || !step.TrackLocalPlayer(World)) return;

            reportedStep = progress.Step;
            LocalPlayerFinished?.Invoke(progress.Step);
        }

        /// <summary>SERVER: begins the current step if it has not begun, and moves on once it is met.</summary>
        private void Advance()
        {
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

            if (changed) Changed?.Invoke(true);
        }

        /// <summary>
        /// Runs the current step's setup if it has not run. True when it ran now. An empty slot in
        /// the chain is skipped rather than blocking it, loudly.
        /// </summary>
        private bool TryBeginCurrent()
        {
            if (progress.Begun) return false;

            ObjectiveStep step = chain.Steps[progress.Step];
            if (step == null)
            {
                Debug.LogError($"[Objectives] Slot {progress.Step} of '{chain.name}' is empty. Skipping it.", chain);
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

        private static bool CrewHasLanded => ArrivalDirector.CrewHasLanded;

        /// <summary>SERVER: the player on <paramref name="clientId"/> has done their part of step <paramref name="step"/>.</summary>
        public void RecordFinished(ulong clientId, int step)
        {
            // A report that crossed a step change on the wire is about a step the crew have left.
            if (step == progress.Step) World.RecordFinished(clientId);
        }

        /// <summary>Everywhere but the server: the server's progress, as replicated.</summary>
        public void Adopt(ObjectiveProgress replicated, bool live)
        {
            if (replicated.Equals(progress)) return;

            // Joining: whatever this machine remembered belongs to no session it is in now.
            if (!live) ForgetStep();

            SetProgress(replicated);
            Changed?.Invoke(live);
        }

        // ── Save ────────────────────────────────────────────────────────────────

        /// <summary>The current step's id, or null once the chain is finished.</summary>
        public string CurrentStepId => IsComplete || chain.Steps[progress.Step] == null
            ? null
            : chain.Steps[progress.Step].Id;

        /// <summary>
        /// Restore-only. Called by <c>ObjectiveSaveable</c>; do not call from gameplay. Puts the
        /// crew back where the record says, without replaying anything that happened to get there.
        /// </summary>
        public void Restore(string stepId, bool complete, bool begun, int remarks = 0)
        {
            if (chain == null) return;

            ForgetStep();
            SetProgress(Resolve(chain, stepId, complete, begun, remarks));
            Changed?.Invoke(false);
        }

        /// <summary>
        /// A saved record as progress through <paramref name="chain"/>. By id, never by position, so
        /// steps can be inserted and reordered under existing saves. An id the chain no longer has
        /// restarts the chain — noisily, because it means a step was renamed or deleted.
        /// </summary>
        public static ObjectiveProgress Resolve(ObjectiveChain chain, string stepId, bool complete, bool begun,
                                                int remarks = 0)
        {
            if (complete) return new ObjectiveProgress { Step = chain.Steps.Count };

            int step = chain.IndexOf(stepId);
            if (step < 0)
            {
                Debug.LogWarning($"[Objectives] The save names step '{stepId}', which '{chain.name}' " +
                                 "no longer has. Starting the chain again.");
                return default;
            }

            return new ObjectiveProgress { Step = step, Begun = begun, Remarks = remarks };
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
    }
}
