// Gives an NPC something to be doing, by writing a destination into AgentGoal and nothing else.
//
// ClaimsMovement is false, which is not a detail — it is the contract. A side-effect module
// structurally cannot return a MoveIntent, so this cannot become a second locomotion authority no
// matter what anyone adds to it later. Travel is GoalTravelModule's job, dwelling is WanderModule's
// job by default, and both preempt cleanly to combat because they sit where they sit on the ladder.
//
// The loop is three states and a timer:
//   CHOOSING  — pick a task, resolve a destination, write the goal.
//   TRAVELLING— wait for the goal to report arrival. Nothing here moves the NPC.
//   DWELLING  — clear the goal so wander takes the frame, and count down. Yield on finish.
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    public class NpcTaskModule : BehaviourModuleBase
    {
        public enum Phase { Choosing, Travelling, Dwelling }

        [Header("Tasks")]
        [Tooltip("What this NPC does with its time. Picked by weight, one at a time. Leave empty " +
                 "and the module does nothing at all, leaving wander/patrol in charge.")]
        [SerializeField] private NpcTask[] tasks;

        [Header("Home")]
        [Tooltip("The kind of site this NPC treats as its base. Tasks with searchFromHome measure " +
                 "their radius from it, which is what makes a group roam AROUND somewhere rather " +
                 "than drift away from it forever.")]
        [SerializeField] private SiteKind homeSiteKind = SiteKind.Home;

        [Tooltip("How far to look for a home site at startup. Finding none is fine — the NPC's own " +
                 "spawn position becomes its home instead.")]
        [SerializeField] private float homeSearchRadius = 400f;

        [Header("Timing")]
        [Tooltip("Seconds to wait before retrying after a task fails to find anywhere to go. " +
                 "Prevents a world with no sites from re-rolling every frame.")]
        [SerializeField] private float retryDelay = 5f;

        [Tooltip("Give up on a journey after this long and pick something else. Without it an NPC " +
                 "whose destination turns out to be unreachable — across a ravine, inside geometry " +
                 "— walks at a wall for the rest of the session.")]
        [SerializeField] private float travelTimeout = 240f;

        [Header("Debug")]
        [SerializeField] private bool logTransitions = false;

        [Tooltip("Optional. Drives NpcTask.dwellFlag while the NPC is working a site — how a " +
                 "grazing animal gets its head down. Found on this object or a child when unset.")]
        [SerializeField] private AgentAnimatorDriver animatorDriver;

        // The flag currently held true, so it can be cleared on the way out even if the task has
        // already been swapped for the next one. Leaving it set is how an animal ends up grazing
        // its way across the desert.
        private string heldDwellFlag;

        // Side-effect only. See the file header — this is the contract, not an optimisation.
        public override bool ClaimsMovement => false;

        // ── Published state, for chatter, dialog and the save system ──────────────
        public Phase CurrentPhase { get; private set; } = Phase.Choosing;
        public int CurrentTaskIndex { get; private set; } = -1;
        public NpcTask CurrentTask =>
            tasks != null && CurrentTaskIndex >= 0 && CurrentTaskIndex < tasks.Length
                ? tasks[CurrentTaskIndex]
                : null;

        /// <summary>What this NPC would say it is doing. Empty when it has no task.</summary>
        public string CurrentLabel => CurrentTask?.label ?? string.Empty;

        /// <summary>The name of the place it is headed for, if that place has one. Empty otherwise.</summary>
        public string CurrentDestinationName { get; private set; } = string.Empty;

        public Vector3 HomePosition { get; private set; }
        public bool HasTasks => tasks != null && tasks.Length > 0;

        // ── Internals ────────────────────────────────────────────────────────────
        private AgentGoal goal;
        private EntityInventoryComponent inventory;
        private string lastSiteId = string.Empty;
        private float phaseTimer;
        private float travelElapsed;
        private bool homeResolved;
        private int forcedTaskIndex = -1;

        private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

        private void Awake()
        {
            goal = AgentGoal.GetOrAdd(gameObject);
            inventory = GetComponent<EntityInventoryComponent>();
        }

        /// <summary>Set by a restore, consumed by the next <see cref="OnEnable"/>.</summary>
        private bool restoredThisEnable;

        private void OnEnable()
        {
            if (restoredThisEnable)
            {
                restoredThisEnable = false;
                return;
            }

            CurrentPhase = Phase.Choosing;
            phaseTimer = 0f;
            travelElapsed = 0f;
        }

        // ─────────── For the save system ───────────
        // A live standalone NPC's errand is state nothing else holds. NpcWorldSaveable covers the
        // VIRTUAL groups; an NPC that is currently a GameObject has its job only here, and without
        // this it reloads mid-journey as "Choosing" and re-rolls a different errand on the spot.

        /// <summary>The site this NPC last headed for, which the planner avoids picking twice running.</summary>
        public string LastSiteId => lastSiteId;

        /// <summary>Seconds left of the current retry delay or dwell, depending on the phase.</summary>
        public float PhaseTimer => phaseTimer;

        /// <summary>How long this journey has been going, against <c>travelTimeout</c>.</summary>
        public float TravelElapsed => travelElapsed;

        /// <summary>
        /// Whether <see cref="HomePosition"/> has been decided.
        ///
        /// Load-bearing for persistence: <c>EnsureHome</c> resolves the home to whichever site is
        /// nearest *the current position*, so an NPC restored 2 km from camp and left to resolve its
        /// own home permanently adopts a new one. Restoring this flag alongside the position is what
        /// stops that.
        /// </summary>
        public bool HomeResolved => homeResolved;

        /// <summary>-1 unless a <see cref="ForceTask"/> is queued and not yet consumed.</summary>
        public int ForcedTaskIndex => forcedTaskIndex;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <paramref name="homeWasResolved"/> false leaves the home alone rather than clearing it:
        /// "no home was recorded" must not become "adopt whatever site you are nearest to now".
        /// </summary>
        public void RestoreTaskState(Phase phase, int taskIndex, string destinationName, string siteId,
                                     float timer, float travelTime, int forcedIndex,
                                     bool homeWasResolved, Vector3 home)
        {
            CurrentPhase = phase;

            CurrentTaskIndex = tasks == null || tasks.Length == 0
                ? -1
                : Mathf.Clamp(taskIndex, -1, tasks.Length - 1);

            CurrentDestinationName = destinationName ?? string.Empty;
            lastSiteId = siteId ?? string.Empty;
            phaseTimer = Mathf.Max(0f, timer);
            travelElapsed = Mathf.Max(0f, travelTime);

            forcedTaskIndex = tasks != null && forcedIndex >= 0 && forcedIndex < tasks.Length
                ? forcedIndex
                : -1;

            if (homeWasResolved)
            {
                HomePosition = home;
                homeResolved = true;
            }

            restoredThisEnable = true;
        }

        public override string ModuleDescription =>
            "Gives the NPC somewhere to be. Writes AgentGoal and never moves the agent itself — " +
            "GoalTravelModule walks there, WanderModule takes over on arrival.\n\n" +
            "• tasks — weighted list of what this NPC does. Each names a SiteKind, not a place.\n" +
            "• homeSiteKind — the base tasks measure their search radius from\n" +
            "• Add ChatterModule to have the NPC talk about the current task\n" +
            "• Add a {task} token to a DialogInteraction line to have it answer when asked\n\n" +
            "A world with no registered sites is handled: the NPC roams to a point at search range " +
            "instead of standing still.";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!HasTasks || goal == null)
                return null;

            EnsureHome();

            switch (CurrentPhase)
            {
                case Phase.Choosing:   TickChoosing(deltaTime);   break;
                case Phase.Travelling: TickTravelling(deltaTime); break;
                case Phase.Dwelling:   TickDwelling(deltaTime);   break;
            }

            // Always. A side-effect module that returned an intent would be arbitrated as a
            // movement module and silently starve everything below it.
            return null;
        }

        // ── Phases ───────────────────────────────────────────────────────────────

        private void TickChoosing(float deltaTime)
        {
            if (phaseTimer > 0f)
            {
                phaseTimer -= deltaTime;
                return;
            }

            int next = forcedTaskIndex >= 0 && forcedTaskIndex < tasks.Length
                ? forcedTaskIndex
                : NpcTaskPlanner.PickTask(tasks, CurrentTaskIndex);

            forcedTaskIndex = -1;

            if (next < 0)
            {
                phaseTimer = retryDelay;
                return;
            }

            CurrentTaskIndex = next;
            NpcTask task = tasks[next];

            Vector3 origin = task.searchFromHome ? HomePosition : transform.position;

            if (!NpcTaskPlanner.ResolveDestination(task, origin, lastSiteId,
                                                   out Vector3 destination, out float arriveRadius,
                                                   out string siteId, out string siteName))
            {
                // Nowhere to go and nowhere to roam — usually means no NavMesh under this agent.
                // Wait and try again rather than spinning.
                phaseTimer = retryDelay;
                return;
            }

            // Sampled, because a site's registered position is wherever its marker sits, which for
            // a marker on a building's origin is routinely inside the building or under the sand.
            if (!goal.TrySetSampled(destination, arriveRadius, Mathf.Max(20f, arriveRadius * 2f),
                                    task.label, siteId, task.travelSpeedMultiplier))
            {
                goal.Set(destination, arriveRadius, task.label, siteId, task.travelSpeedMultiplier);
            }

            lastSiteId = siteId;
            CurrentDestinationName = siteName;
            CurrentPhase = Phase.Travelling;
            travelElapsed = 0f;

            Log($"task '{task.label}' → {(string.IsNullOrEmpty(siteName) ? destination.ToString("F0") : siteName)}");
        }

        private void TickTravelling(float deltaTime)
        {
            travelElapsed += deltaTime;

            if (goal.HasGoal && !goal.HasArrived && travelElapsed < travelTimeout)
                return;

            bool arrived = goal.HasArrived;

            // The goal is cleared BEFORE dwelling, not after. That is what hands the frame down to
            // WanderModule, so the NPC pokes about inside the site instead of standing on the
            // exact coordinate it walked to for the next forty seconds.
            goal.Clear();

            if (!arrived)
            {
                Log($"gave up travelling after {travelElapsed:F0}s");
                CurrentPhase = Phase.Choosing;
                phaseTimer = retryDelay;
                return;
            }

            CurrentPhase = Phase.Dwelling;
            phaseTimer = CurrentTask != null ? CurrentTask.RollDwell() : 10f;
            SetDwellFlag(CurrentTask != null ? CurrentTask.dwellFlag : null);
            Log($"arrived, working for {phaseTimer:F0}s");
        }

        private void TickDwelling(float deltaTime)
        {
            phaseTimer -= deltaTime;
            if (phaseTimer > 0f) return;

            CollectYield();
            SetDwellFlag(null);
            CurrentPhase = Phase.Choosing;
            phaseTimer = 0f;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Hold one animator bool for the duration of a dwell, and never more than one.
        ///
        /// Clearing the previous flag before setting the new one is the whole of it: the two tasks
        /// either side of a transition can name different flags, and setting without clearing
        /// leaves an animal grazing and drinking at once.
        /// </summary>
        private void SetDwellFlag(string flag)
        {
            if (heldDwellFlag == flag) return;

            if (animatorDriver == null)
                animatorDriver = GetComponentInChildren<AgentAnimatorDriver>();

            if (animatorDriver != null)
            {
                if (!string.IsNullOrEmpty(heldDwellFlag))
                    animatorDriver.SetBoolByName(heldDwellFlag, false);
                if (!string.IsNullOrEmpty(flag))
                    animatorDriver.SetBoolByName(flag, true);
            }

            heldDwellFlag = flag;
        }

        private void CollectYield()
        {
            NpcTask task = CurrentTask;
            if (task == null || inventory == null || !task.RollYield())
                return;

            // A full NPC simply fails to pick the thing up, which is the correct outcome and not
            // worth reporting — it is how a scavenger's bag stops growing without anybody deciding
            // it should.
            inventory.TryAddItem(task.yields);
        }

        private void EnsureHome()
        {
            if (homeResolved) return;
            homeResolved = true;

            if (WorldSiteRegistry.TryFindNearest(homeSiteKind, transform.position, homeSearchRadius,
                                                 out WorldSite home))
            {
                HomePosition = home.Position;
                return;
            }

            // Where it woke up. A group spawned in the middle of nowhere still has a centre to roam
            // around, which is better than treating "no home" as "search from wherever I am" —
            // that version drifts, because each new search is centred on the last destination.
            HomePosition = transform.position;
        }

        /// <summary>
        /// Replace the task list at runtime. Used by NpcWorldSim when it spawns a group member, so
        /// the live NPC continues the job its virtual record was already doing.
        /// </summary>
        public void SetTasks(NpcTask[] newTasks, int startIndex = -1)
        {
            tasks = newTasks;
            CurrentTaskIndex = startIndex;
            CurrentPhase = Phase.Choosing;
            phaseTimer = 0f;
            travelElapsed = 0f;
        }

        /// <summary>
        /// Force a specific task to start now, abandoning whatever is in progress.
        ///
        /// Routed through a one-shot field rather than by assigning CurrentTaskIndex, because the
        /// weighted pick treats the current index as the one to AVOID — setting it directly would
        /// make ForceTask reliably choose anything except the task it was given.
        /// </summary>
        public void ForceTask(int index)
        {
            if (tasks == null || index < 0 || index >= tasks.Length) return;

            forcedTaskIndex = index;
            CurrentPhase = Phase.Choosing;
            phaseTimer = 0f;
            lastSiteId = string.Empty;
        }

        /// <summary>Point this NPC's home somewhere specific, overriding the startup search.</summary>
        public void SetHome(Vector3 position)
        {
            HomePosition = position;
            homeResolved = true;
        }

        private void Log(string message)
        {
            if (logTransitions) Debug.Log($"[NpcTask] {name}: {message}", this);
        }

        protected override void OnValidate()
        {
            homeSearchRadius = Mathf.Max(0f, homeSearchRadius);
            retryDelay = Mathf.Max(0.5f, retryDelay);
            travelTimeout = Mathf.Max(10f, travelTimeout);

            if (tasks == null) return;
            foreach (NpcTask task in tasks)
            {
                if (task == null) continue;
                task.searchRadius = Mathf.Max(5f, task.searchRadius);
                task.arriveRadius = Mathf.Max(1f, task.arriveRadius);
                task.weight = Mathf.Max(0f, task.weight);
                task.travelSpeedMultiplier = Mathf.Max(0.01f, task.travelSpeedMultiplier);
            }
        }
    }
}
