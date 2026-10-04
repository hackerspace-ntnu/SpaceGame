// Keeps one band of every expedition-running settlement out in the world: each settlement's rotation and each
// band's record, from the evening it is announced until it is home or lost.
//
// It lives on the NpcWorldSim object in persistentScene, beside WarPartyDirector, because a Settlement exists
// only while its chunk is loaded and a band must go on while its home is kilometres away (plan 2026-10-03,
// decisions). The director decides; NpcWorldSim moves the band as a runtime group owned "expedition"; the
// settlement's performer, registered while its chunk is loaded, plays the parts that need bodies: the muster,
// the walk-out and the hand-off (BeginHandOff), the walk-in (CompleteHomecoming). A settlement nobody has loaded
// is played abstractly: its band leaves from the road point and comes home as records.
//
// Server-only. Everything decidable is in ExpeditionRules; this class only applies the answers.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Agents.Expeditions
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcWorldSim))]
    public class ExpeditionDirector : MonoBehaviour
    {
        /// <summary>The NpcWorldSim template every band's group is created from: runtimeOnly, no members of its own.</summary>
        public const string TemplateId = "settlement-expedition";

        public static ExpeditionDirector Instance { get; private set; }

        /// <summary>
        /// The residents away from a settlement changed: a hand-off, a departure or homecoming played without
        /// bodies, a finished band retired, a load. Server only. The argument is the settlement id.
        /// </summary>
        public static event Action<string> AbsenceChanged;

        // Settlements whose chunk is loaded and whose performer plays their bands' muster and walk-in.
        private static readonly HashSet<string> performed = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            AbsenceChanged = null;
            performed.Clear();
        }

        [Tooltip("GAME minutes per step when a band's days are simulated (/exp days): the band is walked and its stages " +
                 "settled once per step, in travel hours only. Smaller is finer and slower.")]
        [Min(1f)] [SerializeField] private float simulateStepMinutes = 10f;

        // Each try at a leg whose end finds no walkable ground halves it: a ravine or a wall ahead is passed in shorter steps.
        private const float LegShortening = 0.5f;

        /// <summary>One stand-in in the world: whose body it is, and the death hook on it.</summary>
        private sealed class StandIn
        {
            public GameObject Body;
            public int Member;
            public HealthComponent Health;
            public Action Died;
        }

        /// <summary>A band's group as this session knows it. Never saved: rebuilt by AdoptGroups after a load.</summary>
        private sealed class BandRun
        {
            public NpcGroup Group;

            /// <summary>The record's member index for each plan index of the group's PlannedOverride (the dead have none).</summary>
            public readonly List<int> PlanToMember = new();

            public readonly List<StandIn> StandIns = new();

            // The stage and waypoint Destination was worked out for, so the NavMesh is sampled once per target.
            public int DestinationStage = int.MinValue;
            public int DestinationWaypoint = -1;
            public Vector3? Destination;

            // The leg the spawned group walks toward Destination (spawnedLegLength at most); null until it is given one.
            public Vector3? Leg;

            // The last order given to the spawned group's leader, so it is re-given only when it changes.
            public bool WasSpawned;
            public bool Steered;
            public bool SteeredWalk;
            public Vector3 SteeredTo;
        }

        private readonly List<SettlementState> settlements = new();
        private readonly List<ExpeditionRecord> bands = new();
        private readonly Dictionary<string, WorldSiteCatalog.SettlementEntry> catalogEntries = new();
        private readonly Dictionary<string, BandRun> runs = new();
        // By settlement: the day a band last could not be formed, so the warning is logged once per morning.
        private readonly Dictionary<string, int> failedDay = new();
        private readonly HashSet<string> warnedMissingPrefab = new();
        // Groups of bands a load resolved as home: the sim may restore them after the director, so they are let go on adoption.
        private readonly HashSet<string> settledOnLoad = new();
        private readonly List<Vector3> playerPositions = new();
        private readonly List<WorldSite> sites = new();

        private NpcWorldSim sim;
        private ExpeditionCatalog catalog;
        private ExpeditionTuning tuning;
        private float timer;
        private double lastMinutes = double.NaN;
        private bool rotationHeld;
        private WorldSaveStore hydratingStore;
        private bool reportedMissingTemplate;

        // Lazy, so a test can hand in its own catalog and tuning before the first use.
        private NpcWorldSim Sim => sim != null ? sim : sim = GetComponent<NpcWorldSim>();
        private ExpeditionCatalog Catalog => catalog != null ? catalog : catalog = ExpeditionCatalog.Instance;
        private ExpeditionTuning Tuning => tuning != null ? tuning : tuning = ExpeditionTuning.Instance;

        /// <summary>The clock now; the last reading the director stepped at when the world has no running sky.</summary>
        private double Now => DayNightCycle.Main != null ? DayNightCycle.Main.GameMinutesNow
                            : double.IsNaN(lastMinutes) ? 0d : lastMinutes;

        /// <summary>Every settlement that runs bands, as last saved or seen.</summary>
        public IReadOnlyList<SettlementState> Settlements => settlements;

        /// <summary>Every band from announced until home, plus finished bands with dead the settlement has not yet retired.</summary>
        public IReadOnlyList<ExpeditionRecord> Bands => bands;

        /// <summary>
        /// Metres within which a player watching a walking-out band holds its hand-off back: the tuning's
        /// handoffObserveRadius, never past the distance within which <see cref="BeginHandOff"/> spawns stand-ins
        /// (<see cref="ExpeditionRules.EffectiveObserveRadius"/>).
        /// </summary>
        public float HandOffObserveRadius => ExpeditionRules.EffectiveObserveRadius(Tuning.handoffObserveRadius, Sim.DespawnRadius);

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Expedition] A second ExpeditionDirector. One world, one director.", this);
                enabled = false;
                return;
            }

            Instance = this;

            // The residents ask who is away, and hear when it changes, without naming the director.
            Residents.SettlementSociety.AbsenceQuery = IsAway;
            AbsenceChanged += Residents.SettlementSociety.OnAbsenceChanged;

            // Before any save is restored (SaveManager.Start), or the bands' group records are dropped on load.
            Sim.ClaimRuntimeOwner(NpcGroup.OwnerExpedition);

            WorldStreamer streamer = FindFirstObjectByType<WorldStreamer>();
            WorldSiteCatalog siteCatalog = streamer != null && streamer.Config != null ? streamer.Config.siteCatalog : null;
            if (siteCatalog != null) Seed(siteCatalog.settlements);
        }

        private void OnDestroy()
        {
            StopWaitingForHydrate();
            foreach (BandRun run in runs.Values) ForgetStandIns(run);
            runs.Clear();
            if (Instance != this) return;

            Instance = null;
            Residents.SettlementSociety.AbsenceQuery = null;
            AbsenceChanged -= Residents.SettlementSociety.OnAbsenceChanged;
        }

        private void Update()
        {
            if (!Network.Decides) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Tuning.decisionInterval;

            DayNightCycle clock = DayNightCycle.Main;
            if (clock != null) Step(clock.GameMinutesNow);
        }

        /// <summary>The settlements of the world's baked catalog whose culture runs bands; a settlement already known keeps its state.</summary>
        private void Seed(IReadOnlyList<WorldSiteCatalog.SettlementEntry> entries)
        {
            foreach (WorldSiteCatalog.SettlementEntry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.settlementId) || ProfileOf(entry) == null) continue;

                catalogEntries[entry.settlementId] = entry;
                if (StateOf(entry.settlementId) == null)
                    settlements.Add(new SettlementState
                    {
                        settlementId = entry.settlementId,
                        roster = entry.roster ?? Array.Empty<RosterEntry>(),
                    });
            }
        }

        /// <summary>
        /// Holds the rotation until <paramref name="store"/> finishes hydrating the persistent scene: the pass that
        /// called <see cref="Restore"/>, and the one that restores the bands' groups (npcworld) beside it. That pass
        /// runs on every load, in SaveManager.Start, whether or not any player has a record — unlike
        /// SaveManager.OnLoadApplied, which fires only when a binding player's record was restored. No store (a
        /// restore outside a load): nothing to wait for, no hold.
        /// </summary>
        private void HoldRotationUntilHydrated(WorldSaveStore store)
        {
            StopWaitingForHydrate();
            rotationHeld = store != null;
            if (store == null) return;

            hydratingStore = store;
            hydratingStore.OnSceneHydrated += OnSceneHydrated;
        }

        private void OnSceneHydrated(string sceneKey, Scene scene)
        {
            if (sceneKey != SceneKey.Persistent) return;

            rotationHeld = false;
            StopWaitingForHydrate();
        }

        private void StopWaitingForHydrate()
        {
            if (hydratingStore != null) hydratingStore.OnSceneHydrated -= OnSceneHydrated;
            hydratingStore = null;
        }

        // ── The decision pass ────────────────────────────────────────────────

        private void Step(double now)
        {
            double elapsed = double.IsNaN(lastMinutes) ? 0d : Math.Max(0d, now - lastMinutes);
            lastMinutes = now;

            // Adopt before anything else, as the war director does: a band restored by a load already has a group.
            AdoptGroups();

            if (!rotationHeld)
                foreach (SettlementState state in settlements)
                    Rotate(state, now);

            foreach (ExpeditionRecord band in bands.ToArray())
                StepBand(band, now, elapsed);
        }

        private void StepBand(ExpeditionRecord band, double now, double elapsed)
        {
            switch (band.phase)
            {
                case ExpeditionPhase.Announced:
                    if (now >= ExpeditionRules.DepartureMinute(band.departDay, Tuning.departHour)) Depart(band, now);
                    break;
                case ExpeditionPhase.Departing:
                    // The performer left with its chunk before the hand-off: the band left all the same.
                    if (!IsPerformed(band.settlementId)) DepartWithoutBodies(band, now);
                    break;
                case ExpeditionPhase.Out:
                    Travel(band, now, elapsed);
                    break;
                case ExpeditionPhase.Returning:
                    if (!IsPerformed(band.settlementId)) Finish(band, ExpeditionPhase.Home);
                    break;
            }
        }

        // ── The rotation (spec §3.1) ─────────────────────────────────────────

        private void Rotate(SettlementState state, double now)
        {
            if (!ExpeditionRules.NextBandDue(BandsOf(state.settlementId))) return;

            int today = ExpeditionRules.DayOf(now);
            if (failedDay.TryGetValue(state.settlementId, out int failed) && failed == today) return;

            string problem = TryRaise(state, null, now, today + 1, out _);
            if (problem == null) return;

            // "Always one out" is a rule players can see, so failing it is never silent (spec §3.1 rule 4).
            failedDay[state.settlementId] = today;
            Debug.LogWarning($"[Expedition] Settlement '{state.settlementId}' can form no band today: {problem}. " +
                             "It tries again tomorrow.", this);
        }

        /// <summary>
        /// Raises the settlement's next band, leaving on <paramref name="departDay"/>: a drawn goal (or
        /// <paramref name="goalId"/>), and members picked for it. A drawn goal whose slots cannot be filled is passed
        /// over for the next in the draw (spec §3.3). Null on success, else why no band could be formed.
        /// </summary>
        private string TryRaise(SettlementState state, string goalId, double now, int departDay, out ExpeditionRecord raised)
        {
            raised = null;
            if (!catalogEntries.TryGetValue(state.settlementId, out WorldSiteCatalog.SettlementEntry entry))
                return "it is not in this world's site catalog (Tools/SpaceGame/World/Bake Site Catalog)";
            if (!entry.hasMuster) return "it has no muster spot";

            ExpeditionProfile profile = ProfileOf(entry);
            int seed = ExpeditionRules.BandSeed(state.settlementId, state.rotation);
            int today = ExpeditionRules.DayOf(now);
            int warriorsHomeMin = ExpeditionRules.WarriorsHomeMin(ExpeditionRules.WarriorQuota(profile, entry.beds));
            var spokenFor = new HashSet<string>();
            ExpeditionRules.CollectSpokenFor(BandsOf(state.settlementId), spokenFor);

            var passedOver = new HashSet<ExpeditionGoal>();
            while (true)
            {
                ExpeditionGoal goal = goalId != null
                    ? Array.Find(profile.goals, g => g != null && g.id == goalId)
                    : ExpeditionRules.ChooseGoal(profile.goals, state.lastGoalId, seed, g => !passedOver.Contains(g), Tuning.varietyPenalty);
                if (goal == null)
                    return goalId != null ? $"its profile has no goal '{goalId}'"
                         : passedOver.Count == 0 ? "its profile has no goal to draw"
                         : "no goal's slots can be filled from the residents at home and rested";

                if (ExpeditionRules.TryPickMembers(goal, state.roster, spokenFor, state.restUntilDay, today, warriorsHomeMin,
                                                   Tuning.minHomeShare, seed, out List<MemberRecord> members))
                {
                    raised = Announce(state, entry, goal, seed, members, departDay);
                    return null;
                }

                if (goalId != null) return $"the slots of goal '{goalId}' cannot be filled from the residents at home and rested";
                passedOver.Add(goal);
            }
        }

        private ExpeditionRecord Announce(SettlementState state, WorldSiteCatalog.SettlementEntry entry, ExpeditionGoal goal,
                                          int seed, List<MemberRecord> members, int departDay)
        {
            var muster = new Pose(entry.musterPosition,
                                  entry.musterForward.sqrMagnitude > 0f ? Quaternion.LookRotation(entry.musterForward) : Quaternion.identity);
            var band = new ExpeditionRecord
            {
                id = $"exp:{state.settlementId}:{state.rotation}",
                settlementId = state.settlementId,
                goalId = goal.id,
                seed = seed,
                phase = ExpeditionPhase.Announced,
                stages = ExpeditionRules.BuildStages(goal, seed),
                members = members.ToArray(),
                departDay = departDay,
                handoffPoint = ExpeditionRules.RoadPoint(muster, Tuning.departRadius),
            };

            state.rotation++;
            state.lastGoalId = goal.id;
            bands.Add(band);
            return band;
        }

        // ── Leaving ──────────────────────────────────────────────────────────

        /// <summary>The departure hour struck: the performer musters a loaded settlement's band; an unloaded one's just leaves.</summary>
        private void Depart(ExpeditionRecord band, double now)
        {
            if (IsPerformed(band.settlementId)) band.phase = ExpeditionPhase.Departing;
            else DepartWithoutBodies(band, now);
        }

        /// <summary>The members are away and the band sets out folded from the road point (plan: departure while unloaded).</summary>
        private void DepartWithoutBodies(ExpeditionRecord band, double now)
        {
            if (!TemplateReady()) return;

            band.phase = ExpeditionPhase.Out;
            CreateGroup(band, band.handoffPoint);
            Travel(band, now, 0d);
            RaiseAbsence(band.settlementId);
        }

        private bool TemplateReady()
        {
            if (Sim.FindTemplate(TemplateId) != null) return true;

            if (!reportedMissingTemplate)
                Debug.LogError($"[Expedition] The NpcWorldSim has no '{TemplateId}' template, so no band can leave. Run " +
                               "Tools/SpaceGame/Expeditions/Wire Expedition Director.", this);
            reportedMissingTemplate = true;
            return false;
        }

        private NpcGroup CreateGroup(ExpeditionRecord band, Vector3 start)
        {
            NpcGroup group = Sim.CreateGroup(Sim.FindTemplate(TemplateId), band.id, start);
            if (group == null) return null;

            band.groupId = group.Id;
            Hook(band, group);
            return group;
        }

        /// <summary>Makes <paramref name="group"/> this band's: its owner, its members and the hooks on their bodies.</summary>
        private void Hook(ExpeditionRecord band, NpcGroup group)
        {
            if (runs.TryGetValue(band.id, out BandRun old)) ForgetStandIns(old);

            var run = new BandRun { Group = group, WasSpawned = group.Spawned };
            runs[band.id] = run;

            group.Owner = NpcGroup.OwnerExpedition;
            Replan(band, run);
            group.MemberStamp = (body, planIndex) => OnStandInSpawning(band, run, body, planIndex);
            group.ReadBack = (body, _) => OnStandInFolding(band, run, body);
        }

        /// <summary>The group's members are the living ones, never re-rolled, so the sim never spawns the dead (spec §4.3).</summary>
        private void Replan(ExpeditionRecord band, BandRun run)
        {
            SettlementState state = StateOf(band.settlementId);
            run.PlanToMember.Clear();
            run.PlanToMember.AddRange(ExpeditionRules.LivingMembers(band));

            var plan = new List<PlannedMember>(run.PlanToMember.Count);
            foreach (int index in run.PlanToMember)
            {
                MemberRecord member = band.members[index];
                plan.Add(new PlannedMember(StandInPrefab(state, band, member), member.isLeader));
            }
            run.Group.PlannedOverride = plan;
        }

        /// <summary>The prefab a member's stand-in is spawned from (its roster row's source prefab); null leaves its slot empty.</summary>
        private GameObject StandInPrefab(SettlementState state, ExpeditionRecord band, MemberRecord member)
        {
            RosterEntry entry = RosterRow(state, member.residentKey);
            GameObject prefab = entry != null ? Catalog.PrefabFor(entry.sourcePrefabGuid) : null;
            if (prefab == null && warnedMissingPrefab.Add(band.id + "/" + member.residentKey))
                Debug.LogWarning($"[Expedition] Band '{band.id}': no stand-in prefab for resident '{member.residentKey}' " +
                                 "(no source prefab in the roster, or the catalog's prefab table lacks it — Bake Site Catalog). " +
                                 "It travels with the band but is never seen.", this);
            return prefab;
        }

        // ── Settlement-facing API (the performer, Task 1.5) ──────────────────

        /// <summary>
        /// Whether a settlement's resident is away with a band: from the hand-off until its homecoming completes, and,
        /// if it died, until the settlement retires the finished band (<see cref="ExpeditionRules.IsAwayOn"/>). False
        /// with no director.
        /// </summary>
        public static bool IsAway(string settlementId, string residentKey) =>
            Instance != null && Instance.IsAwayHere(settlementId, residentKey);

        private bool IsAwayHere(string settlementId, string residentKey)
        {
            foreach (ExpeditionRecord band in bands)
                if (band.settlementId == settlementId && ExpeditionRules.IsAwayOn(band, residentKey)) return true;
            return false;
        }

        /// <summary>A settlement's chunk is loaded and its performer plays its bands' muster and walk-in. Server only.</summary>
        public static void RegisterPerformer(string settlementId)
        {
            if (!string.IsNullOrEmpty(settlementId)) performed.Add(settlementId);
        }

        /// <summary>The performer is going with its chunk: the settlement's bands are played without bodies again.</summary>
        public static void UnregisterPerformer(string settlementId)
        {
            if (!string.IsNullOrEmpty(settlementId)) performed.Remove(settlementId);
        }

        public static bool IsPerformed(string settlementId) => settlementId != null && performed.Contains(settlementId);

        /// <summary>The settlement's residents as they are now; the caller passes the living only.</summary>
        public void ReportRoster(string settlementId, RosterEntry[] roster)
        {
            if (string.IsNullOrEmpty(settlementId)) return;

            SettlementState state = StateOf(settlementId);
            if (state == null)
            {
                state = new SettlementState { settlementId = settlementId };
                settlements.Add(state);
            }
            state.roster = roster ?? Array.Empty<RosterEntry>();
        }

        /// <summary>
        /// The settlement's residents as the world's baked catalog lists them: everyone it was built with, whether or not a
        /// body turns up when its chunk loads. Empty for a settlement the catalog lacks. The reported roster
        /// (<see cref="ReportRoster"/>) lists only the residents found alive, so it cannot tell a body not found from a
        /// resident that is gone.
        /// </summary>
        public IReadOnlyList<RosterEntry> CatalogRosterOf(string settlementId) =>
            settlementId != null && catalogEntries.TryGetValue(settlementId, out WorldSiteCatalog.SettlementEntry entry) && entry.roster != null
                ? entry.roster
                : Array.Empty<RosterEntry>();

        /// <summary>The settlement's band in <paramref name="phase"/>, or null.</summary>
        public ExpeditionRecord PendingFor(string settlementId, ExpeditionPhase phase) =>
            bands.Find(b => b.settlementId == settlementId && b.phase == phase);

        /// <summary>The settlement's bands in every phase, finished ones not yet retired included.</summary>
        public List<ExpeditionRecord> BandsOf(string settlementId) => bands.FindAll(b => b.settlementId == settlementId);

        public ExpeditionRecord FindBand(string expeditionId) => bands.Find(b => b.id == expeditionId);

        /// <summary>
        /// The walking-out band is handed off (spec §4.1): its group is created where the residents stand, the
        /// members are away, and stage 0 begins. With a player near, the stand-ins are spawned now, each at its
        /// resident's pose (<paramref name="posesByMember"/>, by index into <c>members</c>), so the caller hides the
        /// residents on this same tick; otherwise the group is folded. False when the band is not Departing.
        /// </summary>
        public bool BeginHandOff(string expeditionId, IReadOnlyList<Pose> posesByMember)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            if (band == null || band.phase != ExpeditionPhase.Departing || !TemplateReady()) return false;

            Vector3 start = posesByMember != null && posesByMember.Count > 0 ? ExpeditionRules.Centroid(posesByMember) : band.handoffPoint;
            band.phase = ExpeditionPhase.Out;
            NpcGroup group = CreateGroup(band, start);
            if (group == null) return false;

            // The target first, so the spawned leader is given it as it spawns.
            Travel(band, Now, 0d);

            if (posesByMember != null && posesByMember.Count > 0 && runs.TryGetValue(band.id, out BandRun run) &&
                NearestPlayerDistance(start) <= Sim.DespawnRadius)
            {
                var poses = new List<Pose>(run.PlanToMember.Count);
                foreach (int member in run.PlanToMember)
                    poses.Add(member < posesByMember.Count ? posesByMember[member] : new Pose(start, Quaternion.identity));
                group.SpawnPoses = poses;
                Sim.SpawnNow(group);
            }

            RaiseAbsence(band.settlementId);
            return true;
        }

        /// <summary>
        /// The walked-in band is home: the living members are released and rest (restUntilDay), the group goes, and
        /// the record stays only while it has dead for the settlement to apply (read <c>members</c>, then
        /// <see cref="Retire"/>). Only a Returning band.
        /// </summary>
        public void CompleteHomecoming(string expeditionId)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            if (band != null && band.phase == ExpeditionPhase.Returning) Finish(band, ExpeditionPhase.Home);
        }

        /// <summary>The settlement has applied a finished band's deaths: the record goes. False for a band still underway.</summary>
        public bool Retire(string expeditionId)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            if (band == null || ExpeditionRules.IsUnderway(band)) return false;

            bands.Remove(band);
            RaiseAbsence(band.settlementId);
            return true;
        }

        // ── On the road (spec §5.1, §5.2) ────────────────────────────────────

        /// <summary>One decision for a band on the road: its stand-ins read, its stage advanced, its group sent on.</summary>
        private void Travel(ExpeditionRecord band, double now, double elapsed)
        {
            if (!runs.TryGetValue(band.id, out BandRun run) || run.Group == null) return;

            ReadStandIns(band, run);
            if (band.phase != ExpeditionPhase.Out) return;

            bool arrived = run.Destination.HasValue && HasArrived(run.Group, run.Destination.Value);
            Apply(band, run, ExpeditionRules.Advance(band, now, elapsed, arrived, Tuning), now);
        }

        private void Apply(ExpeditionRecord band, BandRun run, StageStep step, double now)
        {
            if (step == StageStep.SetTarget) ChooseTarget(band, run.Group);

            UpdateDestination(band, run);
            Move(run, ExpeditionRules.IsTravelHour(now, Tuning));

            if (step == StageStep.Arrive) Returned(band);
        }

        /// <summary>A stage that walks somewhere has begun: a Travel rolls its destination, a Search circles where the band stands.</summary>
        private void ChooseTarget(ExpeditionRecord band, NpcGroup group)
        {
            StageRecord stage = band.stages[band.stageIndex];
            if (stage.kind == StageKind.Travel)
            {
                Vector2 reach = Tuning.travelReach;
                sites.Clear();
                foreach (SiteKind kind in Tuning.travelSiteKinds)
                    WorldSiteRegistry.Query(kind, group.Position, Mathf.Max(reach.x, reach.y), sites);

                Vector3 destination = ExpeditionRules.TravelDestination(group.Position, sites,
                    ExpeditionRules.StageSeed(band.seed, band.stageIndex), reach);
                band.target = TrySample(destination, out Vector3 onGround) ? onGround : destination;
            }
            else if (stage.kind == StageKind.Search && band.waypointsDone == 0)
            {
                band.target = group.Position;
            }
        }

        private void UpdateDestination(ExpeditionRecord band, BandRun run)
        {
            if (run.DestinationStage == band.stageIndex && run.DestinationWaypoint == band.waypointsDone) return;

            run.DestinationStage = band.stageIndex;
            run.DestinationWaypoint = band.waypointsDone;
            run.Destination = DestinationOf(band);
            run.Leg = null;
            run.Steered = false;
        }

        /// <summary>Where the current stage walks to; null for a Halt, or once the trip is over.</summary>
        private Vector3? DestinationOf(ExpeditionRecord band)
        {
            if (band.stageIndex < 0 || band.stageIndex >= band.stages.Length) return null;

            StageRecord stage = band.stages[band.stageIndex];
            switch (stage.kind)
            {
                case StageKind.Travel: return band.target;
                case StageKind.Search: return Waypoint(band, stage);
                case StageKind.ReturnHome: return band.handoffPoint;
                default: return null;
            }
        }

        /// <summary>The Search's current waypoint on walkable ground; a ring point with none in reach gives way to the next.</summary>
        private Vector3 Waypoint(ExpeditionRecord band, StageRecord stage)
        {
            IReadOnlyList<Vector3> ring = ExpeditionRules.SearchRing(band.target, stage.waypoints,
                ExpeditionRules.StageSeed(band.seed, band.stageIndex), Tuning);
            for (int i = band.waypointsDone; i < ring.Count; i++)
                if (TrySample(ring[i], out Vector3 onGround)) return onGround;

            return band.waypointsDone < ring.Count ? ring[band.waypointsDone] : band.target;
        }

        private bool TrySample(Vector3 point, out Vector3 onGround)
        {
            bool found = NavMesh.SamplePosition(point, out NavMeshHit hit, Tuning.destinationSampleDistance, NavMesh.AllAreas);
            onGround = found ? hit.position : point;
            return found;
        }

        /// <summary>Folded: the record's own position. Spawned: any member's, so the band has arrived when its first member has.</summary>
        private bool HasArrived(NpcGroup group, Vector3 destination)
        {
            float radius = Tuning.arriveRadius;
            if (!group.Spawned) return group.FlatDistanceTo(destination) <= radius;

            foreach (GameObject member in group.Live)
                if (member != null && ExpeditionRules.FlatDistance(member.transform.position, destination) <= radius) return true;
            return false;
        }

        /// <summary>
        /// Walks the band to its destination in travel hours and halts it otherwise. Folded: the record's goal, which
        /// NpcWorldSim walks. Spawned: the live leader, one leg at a time (<see cref="LegOf"/>), re-ordered only when the
        /// order changes (a halt stands where it is), and told to stop at <c>leaderStopRadius</c> so the band is
        /// within <c>arriveRadius</c> once it stops.
        /// </summary>
        private void Move(BandRun run, bool travelHour)
        {
            NpcGroup group = run.Group;
            bool walk = travelHour && run.Destination.HasValue;

            if (group.Spawned != run.WasSpawned)
            {
                run.WasSpawned = group.Spawned;
                run.Steered = false;
            }
            // A halted or folded band starts its next leg afresh, from wherever it stands then.
            if (!walk || !group.Spawned) run.Leg = null;

            if (!group.Spawned)
            {
                group.GoalPosition = walk ? run.Destination.Value : group.Position;
                group.ArriveRadius = Tuning.arriveRadius;
                group.HasGoal = walk;
                return;
            }

            Vector3 point = walk ? LegOf(run) : group.Position;
            if (run.Steered && run.SteeredWalk == walk && (!walk || run.SteeredTo == point)) return;

            Sim.SteerSpawned(group, point, Mathf.Min(Tuning.leaderStopRadius, Tuning.arriveRadius));
            run.Steered = true;
            run.SteeredWalk = walk;
            run.SteeredTo = point;
        }

        /// <summary>
        /// The end of the leg the spawned band walks now: the one it was given until it arrives there, then the next.
        /// A NavMesh agent never finds a path a kilometre long (Expeditions.md, Gotchas), so the band is never sent
        /// farther than <c>spawnedLegLength</c> at once. Arrival at the stage's destination is judged against the
        /// destination itself, never the leg.
        /// </summary>
        private Vector3 LegOf(BandRun run)
        {
            Vector3 destination = run.Destination.Value;
            if (!run.Leg.HasValue || run.Leg.Value != destination && HasArrived(run.Group, run.Leg.Value))
                run.Leg = NextLeg(run.Group.Position, destination);
            return run.Leg.Value;
        }

        /// <summary>
        /// Up to <c>spawnedLegLength</c> along the straight line from <paramref name="from"/> to
        /// <paramref name="destination"/>, on walkable ground; a leg whose end has none within
        /// <c>destinationSampleDistance</c> is shortened (<see cref="LegShortening"/>) until one has, down to
        /// <c>arriveRadius</c>. With none at all, the full leg unsampled (the sim samples it again as it steers).
        /// </summary>
        private Vector3 NextLeg(Vector3 from, Vector3 destination)
        {
            for (float length = Tuning.spawnedLegLength; length >= Tuning.arriveRadius; length *= LegShortening)
            {
                Vector3 leg = ExpeditionRules.LegPoint(from, destination, length);
                if (leg == destination) return destination;
                if (TrySample(leg, out Vector3 onGround)) return onGround;
            }
            return ExpeditionRules.LegPoint(from, destination, Tuning.spawnedLegLength);
        }

        /// <summary>Back at the hand-off point: the performer walks a loaded settlement's band in; an unloaded one's is simply home.</summary>
        private void Returned(ExpeditionRecord band)
        {
            band.phase = ExpeditionPhase.Returning;
            if (!IsPerformed(band.settlementId)) Finish(band, ExpeditionPhase.Home);
        }

        /// <summary>Ends a band as Home or Lost, and tells the settlement.</summary>
        private void Finish(ExpeditionRecord band, ExpeditionPhase phase)
        {
            Settle(band, phase);
            RaiseAbsence(band.settlementId);
        }

        /// <summary>
        /// Ends a band as Home (the living rest) or Lost, and lets its group go — removed now if folded, once out of
        /// sight if spawned, never popped. A record with no dead has nothing left for the settlement to apply and goes.
        /// </summary>
        private void Settle(ExpeditionRecord band, ExpeditionPhase phase)
        {
            band.phase = phase;

            SettlementState state = StateOf(band.settlementId);
            if (phase == ExpeditionPhase.Home && state != null)
            {
                int restUntil = ExpeditionRules.RestUntil(ExpeditionRules.DayOf(Now), Tuning.restDays);
                foreach (MemberRecord member in band.members)
                    if (!member.dead) state.restUntilDay[member.residentKey] = restUntil;
            }

            // The group's hooks stay set: this can run from inside its own fold's read-back loop. They do nothing
            // once the run is gone (IsCurrent), and a released group is never spawned again.
            if (runs.TryGetValue(band.id, out BandRun run))
            {
                ForgetStandIns(run);
                runs.Remove(band.id);
            }
            Sim.ReleaseGroup(band.groupId);

            if (Array.TrueForAll(band.members, m => !m.dead)) bands.Remove(band);
        }

        // ── Stand-ins and losses (spec §4.3) ─────────────────────────────────

        /// <summary>
        /// A stand-in is about to be network-spawned (NpcGroup.MemberStamp): it becomes its member (name, archetype, kit,
        /// the band's formation: <see cref="ExpeditionMember.Stamp"/>), takes its member's health from the record, and its
        /// death is watched so the member is never spawned again.
        /// </summary>
        private void OnStandInSpawning(ExpeditionRecord band, BandRun run, GameObject body, int planIndex)
        {
            if (!IsCurrent(band, run) || planIndex < 0 || planIndex >= run.PlanToMember.Count) return;

            int memberIndex = run.PlanToMember[planIndex];
            MemberRecord member = band.members[memberIndex];
            ExpeditionMember.Stamp(body, band, memberIndex, ProfileOf(band.settlementId),
                                   RosterRow(StateOf(band.settlementId), member.residentKey)?.displayName);
            HealthComponent health = body.GetComponentInChildren<HealthComponent>();
            if (health != null && member.health01 < 1f)
                health.RestoreHealth(Mathf.CeilToInt(member.health01 * health.GetMaxHealth));

            var standIn = new StandIn { Body = body, Member = memberIndex, Health = health };
            standIn.Died = () =>
            {
                if (!standIn.Health.IsRestoring) MemberDied(band, run, standIn.Member);
            };
            if (health != null) health.OnDeath += standIn.Died;
            run.StandIns.Add(standIn);
        }

        /// <summary>The group is folding (NpcGroup.ReadBack): the stand-in's health goes back into the record.</summary>
        private void OnStandInFolding(ExpeditionRecord band, BandRun run, GameObject body)
        {
            int index = IsCurrent(band, run) ? run.StandIns.FindIndex(s => s.Body == body) : -1;
            if (index < 0) return;

            StandIn standIn = run.StandIns[index];
            Forget(run, index);
            if (standIn.Health == null) return;

            if (standIn.Health.Alive) band.members[standIn.Member].health01 = Health01(standIn.Health);
            else MemberDied(band, run, standIn.Member);
        }

        /// <summary>
        /// Keeps the record's health current while the band is spawned, so a save then is current too. A body
        /// destroyed without a fold (its scene went) is let go: its death, if it died, was already recorded by the
        /// OnDeath hook; alive, it keeps its last health and is spawned again with the group.
        /// </summary>
        private void ReadStandIns(ExpeditionRecord band, BandRun run)
        {
            for (int i = run.StandIns.Count - 1; i >= 0; i--)
            {
                StandIn standIn = run.StandIns[i];
                if (standIn.Body == null) Forget(run, i);
                else if (standIn.Health != null && standIn.Health.Alive) band.members[standIn.Member].health01 = Health01(standIn.Health);
            }
        }

        private void MemberDied(ExpeditionRecord band, BandRun run, int memberIndex)
        {
            MemberRecord member = band.members[memberIndex];
            if (member.dead) return;

            member.dead = true;
            member.health01 = 0f;

            if (ExpeditionRules.LivingMembers(band).Count == 0) Finish(band, ExpeditionPhase.Lost);
            else Replan(band, run);
        }

        /// <summary>The run is still the band's: not finished, and not replaced by a re-adoption.</summary>
        private bool IsCurrent(ExpeditionRecord band, BandRun run) => runs.TryGetValue(band.id, out BandRun current) && current == run;

        private static float Health01(HealthComponent health) =>
            health.GetMaxHealth > 0 ? Mathf.Clamp01((float)health.GetHealth / health.GetMaxHealth) : 1f;

        private static void Forget(BandRun run, int index)
        {
            StandIn standIn = run.StandIns[index];
            if (standIn.Health != null) standIn.Health.OnDeath -= standIn.Died;
            run.StandIns.RemoveAt(index);
        }

        private static void ForgetStandIns(BandRun run)
        {
            for (int i = run.StandIns.Count - 1; i >= 0; i--) Forget(run, i);
        }

        // ── After a load ─────────────────────────────────────────────────────

        /// <summary>
        /// Every band past the hand-off gets its group back with its hooks (a restored group has none), or a new one
        /// at the road point when the save had none; an expedition group no band owns is let go.
        /// </summary>
        private void AdoptGroups()
        {
            foreach (ExpeditionRecord band in bands.ToArray())
            {
                if (!ExpeditionRules.IsPastHandOff(band)) continue;

                NpcGroup group = Sim.FindGroup(band.groupId);
                if (group == null)
                {
                    // Walking in: the settlement's performer swapped the stand-ins back for the residents and let the group go.
                    if (band.phase == ExpeditionPhase.Returning && IsPerformed(band.settlementId)) continue;
                    if (!TemplateReady()) continue;
                    if (!string.IsNullOrEmpty(band.groupId))
                        Debug.LogWarning($"[Expedition] Band '{band.id}' had no group in the world; it sets out again from " +
                                         "its road point.", this);
                    CreateGroup(band, band.handoffPoint);
                    continue;
                }

                if (!runs.TryGetValue(band.id, out BandRun run) || run.Group != group) Hook(band, group);
            }

            foreach (NpcGroup group in new List<NpcGroup>(Sim.Groups))
            {
                if (group.Owner != NpcGroup.OwnerExpedition || group.DisbandWhenFolded) continue;
                if (bands.Exists(b => ExpeditionRules.IsPastHandOff(b) && b.groupId == group.Id)) continue;

                if (!settledOnLoad.Contains(group.Id))
                    Debug.LogWarning($"[Expedition] Group '{group.Id}' belongs to no band on the road; letting it go.", this);
                Sim.ReleaseGroup(group.Id);
            }
            settledOnLoad.Clear();
        }

        // ── Saving (ExpeditionSaveable, key "expeditions") ───────────────────

        public SettlementState[] CaptureSettlements() => settlements.ToArray();

        /// <summary>Every band, with the health of stand-ins in the world read into it first.</summary>
        public ExpeditionRecord[] CaptureBands()
        {
            foreach (ExpeditionRecord band in bands)
                if (runs.TryGetValue(band.id, out BandRun run)) ReadStandIns(band, run);
            return bands.ToArray();
        }

        /// <summary>
        /// Replaces every settlement's state and every band with a save's. Overrides are not saved, so a band
        /// saved mid-muster or mid-walk-out is resolved as departed (its group is created at the road point on the
        /// next step) and one saved walking in as home. The rotation raises nothing until the persistent scene's
        /// hydrate pass that is restoring it has finished (<see cref="HoldRotationUntilHydrated"/>).
        /// </summary>
        public void Restore(SettlementState[] savedSettlements, ExpeditionRecord[] savedBands)
        {
            foreach (BandRun run in runs.Values) ForgetStandIns(run);
            runs.Clear();

            var told = new HashSet<string>();
            foreach (SettlementState state in settlements) told.Add(state.settlementId);

            settlements.Clear();
            foreach (SettlementState state in savedSettlements ?? Array.Empty<SettlementState>())
            {
                if (state == null || string.IsNullOrEmpty(state.settlementId)) continue;
                state.roster ??= Array.Empty<RosterEntry>();
                state.restUntilDay ??= new Dictionary<string, int>();
                state.lastGoalId ??= string.Empty;
                settlements.Add(state);
            }
            Seed(new List<WorldSiteCatalog.SettlementEntry>(catalogEntries.Values));

            bands.Clear();
            foreach (ExpeditionRecord band in savedBands ?? Array.Empty<ExpeditionRecord>())
            {
                if (band == null || string.IsNullOrEmpty(band.id)) continue;
                band.members ??= Array.Empty<MemberRecord>();
                band.stages ??= Array.Empty<StageRecord>();
                band.groupId ??= string.Empty;
                bands.Add(band);
            }

            settledOnLoad.Clear();
            foreach (ExpeditionRecord band in bands.ToArray())
            {
                if (band.phase == ExpeditionPhase.Departing) band.phase = ExpeditionPhase.Out;
                if (band.phase != ExpeditionPhase.Returning) continue;

                settledOnLoad.Add(band.groupId);
                Settle(band, ExpeditionPhase.Home);
            }

            failedDay.Clear();
            lastMinutes = double.NaN;
            HoldRotationUntilHydrated(SaveManager.Instance != null ? SaveManager.Instance.World : null);

            foreach (SettlementState state in settlements) told.Add(state.settlementId);
            foreach (string settlementId in told) RaiseAbsence(settlementId);
        }

        // ── Debug (/exp, Task 1.6) ───────────────────────────────────────────

        /// <summary>
        /// Raises a band of <paramref name="goalId"/> now, whatever the rotation says; it leaves at the next departure
        /// hour. Null, with <paramref name="problem"/> saying why, when it cannot be formed.
        /// </summary>
        public ExpeditionRecord ForceBand(string settlementId, string goalId, out string problem)
        {
            SettlementState state = StateOf(settlementId);
            if (state == null)
            {
                problem = $"no settlement '{settlementId}' runs bands";
                return null;
            }

            double now = Now;
            problem = TryRaise(state, goalId, now, ExpeditionRules.NextDepartureDay(now, Tuning.departHour), out ExpeditionRecord band);
            return band;
        }

        /// <summary>
        /// An announced band leaves now instead of at its departure hour: a loaded settlement's musters, an unloaded
        /// one's sets out from its road point. False for a band that is not Announced, or could not leave.
        /// </summary>
        public bool DepartNow(string expeditionId)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            if (band == null || band.phase != ExpeditionPhase.Announced) return false;

            Depart(band, Now);
            return band.phase != ExpeditionPhase.Announced;
        }

        /// <summary>What <see cref="SendHome"/> did.</summary>
        public enum HomeResult
        {
            /// <summary>Not a band on the road with a group.</summary>
            NotOnTheRoad,
            /// <summary>No ReturnHome stage lies ahead.</summary>
            NoReturnHome,
            /// <summary>Folded and put at its hand-off point: the next step finds it arrived.</summary>
            AtHandOffPoint,
            /// <summary>Spawned, in somebody's view: it walks to its hand-off point from where it stands.</summary>
            Walking,
        }

        /// <summary>
        /// Ends a band's stages until it is on ReturnHome. A folded band is put at its hand-off point, so the next step
        /// finds it arrived and it walks in; a spawned one is never moved in view.
        /// </summary>
        public HomeResult SendHome(string expeditionId)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            if (band == null || band.phase != ExpeditionPhase.Out || !runs.TryGetValue(band.id, out BandRun run))
                return HomeResult.NotOnTheRoad;

            int ends = ExpeditionRules.StageEndsUntil(band, StageKind.ReturnHome);
            if (ends < 0) return HomeResult.NoReturnHome;

            AdvanceStage(expeditionId, ends);
            if (run.Group.Spawned) return HomeResult.Walking;

            run.Group.Position = band.handoffPoint;
            return HomeResult.AtHandOffPoint;
        }

        /// <summary>Ends a band's current stage <paramref name="count"/> times, as if its timer ran out. Only a band on the road.</summary>
        public bool AdvanceStage(string expeditionId, int count)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            if (band == null || band.phase != ExpeditionPhase.Out || !runs.TryGetValue(band.id, out BandRun run)) return false;

            double now = Now;
            for (int i = 0; i < count && band.phase == ExpeditionPhase.Out; i++)
            {
                if (band.stageIndex >= 0) band.stageMinutesLeft = 0f;
                Apply(band, run, ExpeditionRules.Advance(band, now, 0d, false, Tuning), now);
            }
            return true;
        }

        /// <summary>
        /// Runs a folded band <paramref name="days"/> game days ahead of the clock: in steps of
        /// <see cref="simulateStepMinutes"/>, the group walks as NpcWorldSim would walk it (in travel hours only) and
        /// each step is decided as a live one, arrivals included, so the band can come all the way home. The world's
        /// clock does not move. False for a band that is not on the road, or is spawned (somebody is watching it).
        /// </summary>
        public bool SimulateDays(string expeditionId, float days)
        {
            ExpeditionRecord band = FindBand(expeditionId);
            DayNightCycle clock = DayNightCycle.Main;
            NpcGroupTemplate template = Sim.FindTemplate(TemplateId);
            if (band == null || band.phase != ExpeditionPhase.Out || clock == null || template == null) return false;
            if (!runs.TryGetValue(band.id, out BandRun run) || run.Group.Spawned) return false;

            float secondsPerMinute = clock.cycleDuration / DayNightCycle.MinutesPerDay;
            double now = clock.GameMinutesNow;
            double end = now + days * (double)DayNightCycle.MinutesPerDay;
            while (now < end && band.phase == ExpeditionPhase.Out)
            {
                double step = Math.Min(simulateStepMinutes, end - now);
                if (run.Group.HasGoal) run.Group.AdvanceToward(template.travelSpeed, (float)(step * secondsPerMinute));
                now += step;
                Travel(band, now, step);
            }
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private SettlementState StateOf(string settlementId) => settlements.Find(s => s.settlementId == settlementId);

        private ExpeditionProfile ProfileOf(WorldSiteCatalog.SettlementEntry entry)
        {
            ExpeditionProfile[] profiles = Catalog.profiles;
            return entry.cultureProfileIndex >= 0 && entry.cultureProfileIndex < profiles.Length ? profiles[entry.cultureProfileIndex] : null;
        }

        private ExpeditionProfile ProfileOf(string settlementId) =>
            catalogEntries.TryGetValue(settlementId, out WorldSiteCatalog.SettlementEntry entry) ? ProfileOf(entry) : null;

        private static RosterEntry RosterRow(SettlementState state, string residentKey) =>
            state != null ? Array.Find(state.roster, r => r.residentKey == residentKey) : null;

        private static void RaiseAbsence(string settlementId) => AbsenceChanged?.Invoke(settlementId);

        private float NearestPlayerDistance(Vector3 position)
        {
            Sim.CollectPlayerPositions(playerPositions);
            float nearest = float.PositiveInfinity;
            foreach (Vector3 player in playerPositions) nearest = Mathf.Min(nearest, ExpeditionRules.FlatDistance(player, position));
            return nearest;
        }
    }
}
