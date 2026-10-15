using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Walks the crew through their <see cref="ObjectiveChain"/>s: one place in each chain, shared by
    /// everyone in the session.
    ///
    /// <para>
    /// <b>One main chain, any number of side chains.</b> The main chain is under way from the landing.
    /// A side chain stands still until somebody accepts it (<see cref="TryStart"/>), and then runs
    /// alongside the main one with its own progress and its own <see cref="ObjectiveWorld"/>. Each is
    /// an <see cref="ObjectiveRun"/>; the main chain is run 0.
    /// </para>
    /// <para>
    /// <b>The server decides, every machine presents.</b> Only where <see cref="Network.Decides"/>
    /// does this start chains, begin steps and judge them met; everywhere else the progress arrives
    /// through <see cref="ObjectiveNetwork"/> into <see cref="Adopt"/>. The briefing, the visor and
    /// the beacon all read the runs on their own machine and add nothing to the wire. The one thing a
    /// client sends is "my player has done their part" for a step every player must do themselves
    /// (<see cref="LocalPlayerFinished"/>).
    /// </para>
    /// <para>
    /// <b>Story worlds only.</b> A versus match has one hull per team and no story, so every chain
    /// stands still there, and nothing is saved for it.
    /// </para>
    /// <para>
    /// Lives in <c>persistentScene</c>. No chain advances until the crew have landed — the bodies are
    /// spawned at the hull and seated a frame later, and nothing may be judged while the ship is still
    /// in the air.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]   // awake before the presenters that bind to it in their OnEnable
    public class ObjectiveDirector : MonoBehaviour
    {
        public static ObjectiveDirector Instance { get; private set; }

        [Tooltip("The main chain: under way from the landing.")]
        [SerializeField] private ObjectiveChain chain;

        [Tooltip("Chains that wait until somebody accepts them — a settlement's quest, a villager's " +
                 "favour. Append only: a run's position here is how peers name it on the wire.")]
        [SerializeField] private ObjectiveChain[] sideChains = Array.Empty<ObjectiveChain>();

        private ObjectiveRun[] runs = Array.Empty<ObjectiveRun>();

        /// <summary>The run whose progress last changed on this machine; what the visor points at.</summary>
        private ObjectiveRun lastChanged;

        /// <summary>
        /// A run's progress changed on this machine. <c>live</c> is true when it happened in play — a
        /// chain accepted, a step reached, a step begun — and false when it was restored from a save
        /// or read off the wire on joining, which a briefing must not replay.
        /// </summary>
        public event Action<ObjectiveRun, bool> Changed;

        /// <summary>
        /// This machine's own player has done their part of the given run's step at this index.
        /// Raised once per step; <see cref="ObjectiveNetwork"/> carries it to the server.
        /// </summary>
        public event Action<ObjectiveRun, int> LocalPlayerFinished;

        /// <summary>Every run, main chain first. The same order on every machine.</summary>
        public IReadOnlyList<ObjectiveRun> Runs => runs;

        /// <summary>A story world with chains to run.</summary>
        public bool IsRunning => chain != null && !VersusSession.IsActive;

        /// <summary>
        /// The run the crew's attention is on: the one that last changed, while it still has a step,
        /// else the main chain, else any side chain under way. Null when nothing has a step.
        /// </summary>
        public ObjectiveRun Focused
        {
            get
            {
                if (!IsRunning) return null;
                if (lastChanged != null && lastChanged.IsActive) return lastChanged;

                foreach (ObjectiveRun run in runs)
                    if (run.IsActive) return run;

                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Objectives] A second ObjectiveDirector was destroyed; the crew have " +
                               "one place in each chain.", this);
                Destroy(this);
                return;
            }

            Instance = this;

            if (chain == null)
            {
                Debug.LogError("[Objectives] No main chain assigned, so the crew have no objectives.", this);
                return;
            }

            runs = new ObjectiveRun[1 + sideChains.Length];
            runs[0] = new ObjectiveRun(chain, 0, startsOnLanding: true);
            for (int i = 0; i < sideChains.Length; i++)
            {
                if (sideChains[i] == null)
                    Debug.LogError($"[Objectives] Side chain slot {i} is empty.", this);
                runs[i + 1] = new ObjectiveRun(sideChains[i], i + 1, startsOnLanding: false);
            }
        }

        private void OnEnable()
        {
            // A duplicate being destroyed still gets OnEnable this frame; only the survivor records.
            if (Instance != this) return;

            UseChannel.UsedOnServer += OnItemUsed;
            QuestMarks.RaisedOnServer += OnQuestMark;
        }

        private void OnDisable()
        {
            UseChannel.UsedOnServer -= OnItemUsed;
            QuestMarks.RaisedOnServer -= OnQuestMark;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnItemUsed(InventoryItem item, GameObject holder)
        {
            foreach (ObjectiveRun run in runs)
                if (run.IsActive) run.World.RecordUse(item);
        }

        private void OnQuestMark(QuestMark mark, string id)
        {
            foreach (ObjectiveRun run in runs)
                if (run.IsActive) run.World.RecordMark(mark, id);
        }

        private void Update()
        {
            if (!IsRunning) return;

            bool decides = Network.Decides && CrewHasLanded;
            foreach (ObjectiveRun run in runs)
            {
                if (run.Chain == null || !run.IsActive) continue;

                ObjectiveStep step = run.Current;
                if (step != null && run.Progress.Begun) TrackLocalPlayer(run, step);
                if (decides) Advance(run);
            }
        }

        /// <summary>EVERY machine: tells the server, once per step, when this machine's player has done their part.</summary>
        private void TrackLocalPlayer(ObjectiveRun run, ObjectiveStep step)
        {
            if (run.ReportedStep == run.Progress.Step || !step.TrackLocalPlayer(run.World)) return;

            run.ReportedStep = run.Progress.Step;
            LocalPlayerFinished?.Invoke(run, run.Progress.Step);
        }

        /// <summary>SERVER: begins the run's current step if it has not begun, and moves on once it is met.</summary>
        private void Advance(ObjectiveRun run)
        {
            bool changed = TryBeginCurrent(run);

            // Read after TryBeginCurrent, which may have skipped an empty slot.
            ObjectiveStep step = run.Current;
            if (step != null && run.Progress.Begun && step.IsMet(run.World))
            {
                SetProgress(run, new ObjectiveProgress { Step = run.Progress.Step + 1, Started = run.Progress.Started });

                // Straight on into the next step's setup, so a peer usually hears the step and its
                // begun flag in one change rather than two.
                if (!run.IsComplete) TryBeginCurrent(run);
                changed = true;
            }

            if (changed) Announce(run, live: true);
        }

        /// <summary>
        /// Runs the current step's setup if it has not run. True when it ran now. An empty slot in
        /// the chain is skipped rather than blocking it, loudly.
        /// </summary>
        private bool TryBeginCurrent(ObjectiveRun run)
        {
            if (run.Progress.Begun) return false;

            ObjectiveStep step = run.Chain.Steps[run.Progress.Step];
            if (step == null)
            {
                Debug.LogError($"[Objectives] Slot {run.Progress.Step} of '{run.Chain.name}' is empty. Skipping it.", run.Chain);
                SetProgress(run, new ObjectiveProgress { Step = run.Progress.Step + 1, Started = run.Progress.Started });
                return true;
            }

            if (!step.TryBegin(run.World)) return false;

            ObjectiveProgress begun = run.Progress;
            begun.Begun = true;
            run.Progress = begun;
            return true;
        }

        private static bool CrewHasLanded =>
            ArrivalDirector.Instance == null || ArrivalDirector.Instance.HasArrived;

        /// <summary>
        /// SERVER: somebody accepted the side chain <paramref name="chainId"/>. False when there is no
        /// such side chain, or it has already been started.
        /// </summary>
        public bool TryStart(string chainId)
        {
            if (!IsRunning || !Network.Decides) return false;

            ObjectiveRun run = Find(chainId);
            if (run == null)
            {
                Debug.LogWarning($"[Objectives] Asked to start chain '{chainId}', which is not a side chain here.", this);
                return false;
            }

            if (run.IsStarted) return false;

            SetProgress(run, new ObjectiveProgress { Started = true });
            Announce(run, live: true);
            return true;
        }

        /// <summary>The run of the chain with this id, or null.</summary>
        public ObjectiveRun Find(string chainId)
        {
            if (string.IsNullOrEmpty(chainId)) return null;

            foreach (ObjectiveRun run in runs)
                if (run.Chain != null && run.Chain.Id == chainId) return run;

            return null;
        }

        /// <summary>SERVER: the player on <paramref name="clientId"/> has done their part of run <paramref name="runIndex"/>'s step <paramref name="step"/>.</summary>
        public void RecordFinished(ulong clientId, int runIndex, int step)
        {
            if (runIndex < 0 || runIndex >= runs.Length) return;

            // A report that crossed a step change on the wire is about a step the crew have left.
            ObjectiveRun run = runs[runIndex];
            if (step == run.Progress.Step) run.World.RecordFinished(clientId);
        }

        /// <summary>Everywhere but the server: run <paramref name="runIndex"/>'s progress, as replicated.</summary>
        public void Adopt(int runIndex, ObjectiveProgress replicated, bool live)
        {
            if (runIndex < 0 || runIndex >= runs.Length) return;

            ObjectiveRun run = runs[runIndex];
            if (replicated.Equals(run.Progress)) return;

            // Joining: whatever this machine remembered belongs to no session it is in now.
            if (!live) ForgetStep(run);

            SetProgress(run, replicated);
            Announce(run, live);
        }

        // ── Save ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Restore-only. Called by <c>ObjectiveSaveable</c>; do not call from gameplay. Puts the crew
        /// back where the records say, without replaying anything that happened to get there. A run
        /// with no record goes back to the start — a side chain nobody has accepted.
        /// </summary>
        public void Restore(IReadOnlyDictionary<string, ObjectiveProgress> records)
        {
            foreach (ObjectiveRun run in runs)
            {
                if (run.Chain == null) continue;

                ForgetStep(run);
                SetProgress(run, records.TryGetValue(run.Chain.Id ?? string.Empty, out ObjectiveProgress saved)
                    ? saved
                    : default);
                Announce(run, live: false);
            }

            foreach (string id in records.Keys)
                if (Find(id) == null)
                    Debug.LogWarning($"[Objectives] The save names chain '{id}', which this world no longer has. Dropping it.", this);
        }

        /// <summary>
        /// A saved record as progress through <paramref name="chain"/>. By id, never by position, so
        /// steps can be inserted and reordered under existing saves. An id the chain no longer has
        /// restarts the chain — noisily, because it means a step was renamed or deleted.
        /// </summary>
        public static ObjectiveProgress Resolve(ObjectiveChain chain, string stepId, bool complete, bool begun, bool started)
        {
            if (complete) return new ObjectiveProgress { Step = chain.Steps.Count, Started = started };

            int step = chain.IndexOf(stepId);
            if (step < 0)
            {
                Debug.LogWarning($"[Objectives] The save names step '{stepId}', which '{chain.name}' " +
                                 "no longer has. Starting the chain again.");
                return new ObjectiveProgress { Started = started };
            }

            return new ObjectiveProgress { Step = step, Begun = begun, Started = started };
        }

        private void SetProgress(ObjectiveRun run, ObjectiveProgress value)
        {
            // Accepting a side chain lands on step 0 without moving the index, and is a step change all the same.
            if (value.Step != run.Progress.Step || value.Started != run.Progress.Started) ForgetStep(run);
            run.Progress = value;
        }

        private void Announce(ObjectiveRun run, bool live)
        {
            if (live) lastChanged = run;
            Changed?.Invoke(run, live);
        }

        /// <summary>Drops everything remembered about the run's current step, on this machine.</summary>
        private static void ForgetStep(ObjectiveRun run)
        {
            run.World.ForgetStep();
            run.ReportedStep = -1;
        }
    }
}
