// Runs every NPC group in the world, whether or not anybody is there to see it.
//
// Two states per group and one rule for moving between them:
//
//   VIRTUAL — a record. It walks a straight line toward its destination at travelSpeed, runs the
//   same NpcTaskPlanner loop a live NPC does, and costs one lerp and one distance check a second.
//   The desert is mostly open, so a straight line is a fair approximation of a path nobody can see.
//
//   SPAWNED — real prefabs in formation at the record's position, running their own AI. This
//   happens when a player comes within spawnRadius, and unwinds at despawnRadius. The gap between
//   the two is hysteresis: without it, standing exactly on the boundary spawns and despawns a
//   caravan every second.
//
// A group whose template has a transport (a Sky war party) travels in a vessel until it is
// dropped off: folded at the transport's speed, spawned seated aboard a vessel that flies it to its
// goal. From then on it walks like any other group, and the empty vessel flies home and is taken
// away once nobody can see it.
//
// Put one of these in the persistent scene. It is server-only — NPC decisions belong on the machine
// that simulates them, and a client running its own copy would produce a different caravan in a
// different place with the same name.
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class NpcWorldSim : MonoBehaviour
    {
        public static NpcWorldSim Instance { get; private set; }

        [Header("Groups")]
        [Tooltip("The groups that populate this world. One live group per template.")]
        [SerializeField] private NpcGroupTemplate[] templates;

        [Header("Spawning")]
        [Tooltip("A player within this distance of a group makes it real.")]
        [SerializeField] private float spawnRadius = 250f;

        [Tooltip("With every player beyond this distance, a group folds back into a record. Must " +
                 "stay comfortably above spawnRadius — the gap is what stops a group flickering " +
                 "in and out while a player walks along the boundary.")]
        [SerializeField] private float despawnRadius = 350f;

        [Tooltip("How far the spawner may search for walkable ground under a member's slot.")]
        [SerializeField] private float spawnSampleDistance = 25f;

        [Header("Simulation")]
        [Tooltip("Seconds between virtual ticks. A record has nothing to interpolate, so this can " +
                 "be slow — the cost of a group is this divided into one lerp.")]
        [SerializeField] private float tickInterval = 1f;

        [Tooltip("How often the list of players is refreshed. Players join, die and respawn; " +
                 "caching forever means a group never notices anyone who arrived late.")]
        [SerializeField] private float playerRefreshInterval = 2f;

        [Header("Transports")]
        [Tooltip("Seconds a vessel that came home with its party still aboard (it found nowhere to set " +
                 "down) waits at its dock before it tries the drop again.")]
        [SerializeField] private float transportRetryDelay = 15f;

        [Tooltip("A vessel parked empty at its dock within this distance of a group about to fly out is " +
                 "boarded instead of a new one being spawned beside it.")]
        [SerializeField] private float dockReuseRadius = 150f;

        [Header("Bounty hunters")]
        [Tooltip("Seconds before a lead on the player goes cold and the squad returns to roaming.")]
        [SerializeField] private float leadLifetime = 180f;

        [Tooltip("How far a hunting squad roams when it has no lead.")]
        [SerializeField] private float hunterRoamRadius = 900f;

        [Header("Debug")]
        [SerializeField] private bool logGroupEvents = false;
        [SerializeField] private bool drawGizmos = true;

        private readonly List<NpcGroup> groups = new();
        private readonly Dictionary<string, NpcGroupTemplate> templatesById = new();
        private readonly List<Transform> players = new();

        private float tickTimer;
        private float playerTimer;

        private SceneEventHook sceneEvents;
        private bool worldReplaced;

        /// <summary>Every group, live or virtual. Read by the save adapter.</summary>
        public IReadOnlyList<NpcGroup> Groups => groups;

        /// <summary>A war party's fighter saw its quarry for the first time since it spawned. Server only.</summary>
        public event Action<NpcGroup, GameObject> QuarrySighted;

        public float SpawnRadius => spawnRadius;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            BuildTemplateIndex();

            // Through a hook: there is no NetworkSceneManager until a session starts. See SceneEventHook.
            sceneEvents = new SceneEventHook(OnSceneEvent);
            sceneEvents.Poll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            sceneEvents?.Detach();
        }

        /// <summary>
        /// A single-mode load replaces the world this sim belongs to, and from that moment its groups
        /// are no longer its to decide. Netcode has already despawned every member (NpcSpawn spawns
        /// them destroyWithScene), but this scene lives on for the frames the new one takes to load:
        /// ticking through them read every group as wiped out — which the war director resolves as a
        /// defeat, posting "their resolve is weakening" over the loading screen — and spawned fresh
        /// members into a world already on its way out. The new world's sim rebuilds them all from the save.
        /// </summary>
        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType == SceneEventType.Load && sceneEvent.LoadSceneMode == LoadSceneMode.Single)
                worldReplaced = true;
        }

        private void Start()
        {
            if (groups.Count == 0) SeedGroups();
        }

        private void Update()
        {
            // Server only. NPC decisions must be made in exactly one place, or every machine gets
            // its own caravan in its own position and NetworkTransform has two truths to reconcile.
            if (Network.IsNetworked && !Network.Server) return;

            sceneEvents.Poll();
            if (worldReplaced) return;

            float delta = Time.deltaTime;

            playerTimer -= delta;
            if (playerTimer <= 0f)
            {
                playerTimer = playerRefreshInterval;
                RefreshPlayers();
            }

            tickTimer -= delta;
            if (tickTimer > 0f) return;

            float elapsed = tickInterval - tickTimer;
            tickTimer = tickInterval;

            for (int i = 0; i < groups.Count; i++)
                TickGroup(groups[i], elapsed);

            // A released group leaves once nobody can see it go (rosters plan: never popped out of view),
            // and not before the vessel it came in has gone the same way.
            groups.RemoveAll(g => g.DisbandWhenFolded && !g.Spawned && g.Transport == null);
        }

        // ── Setup ────────────────────────────────────────────────────────────────

        private void BuildTemplateIndex()
        {
            templatesById.Clear();
            if (templates == null) return;

            foreach (NpcGroupTemplate template in templates)
            {
                if (template == null || string.IsNullOrWhiteSpace(template.id)) continue;

                if (!templatesById.TryAdd(template.id, template))
                {
                    Debug.LogWarning($"[NpcWorldSim] Two group templates share the id '{template.id}'. " +
                                     "Ids key both the save record and the formation, so the second " +
                                     "is ignored.", this);
                }
            }
        }

        private void SeedGroups()
        {
            if (templates == null) return;

            foreach (NpcGroupTemplate template in templates)
            {
                if (template == null || string.IsNullOrWhiteSpace(template.id) || template.runtimeOnly) continue;

                var group = new NpcGroup
                {
                    Id = template.id,
                    TemplateId = template.id,
                    Position = ResolveStart(template),
                    RosterSeed = RosterDraw.StableHash(template.id),
                };

                groups.Add(group);
            }
        }

        private Vector3 ResolveStart(NpcGroupTemplate template)
        {
            if (template.useStartPosition) return template.startPosition;

            if (WorldSiteRegistry.TryFindRandom(template.startNearSite, transform.position,
                                                float.MaxValue, out WorldSite site))
                return site.Position;

            // Nothing registered yet. Starting at the sim's own position is honest — the group will
            // pick a roam destination on its first tick and set off from there rather than sitting
            // at the origin forever.
            return transform.position;
        }

        // ── Runtime groups ───────────────────────────────────────────────────────

        /// <summary>
        /// A group that no template seeded — a war party. Its template must be in <c>templates</c>
        /// (normally <c>runtimeOnly</c>), or a save could never restore it. Server only.
        /// </summary>
        public NpcGroup CreateGroup(NpcGroupTemplate template, string groupId, Vector3 start)
        {
            if (template == null || string.IsNullOrWhiteSpace(groupId)) return null;

            if (FindGroup(groupId) != null)
            {
                Debug.LogError($"[NpcWorldSim] A group with id '{groupId}' already exists; ids key both " +
                               "the save record and the formation.", this);
                return null;
            }

            if (!templatesById.ContainsKey(template.id ?? string.Empty))
            {
                Debug.LogError($"[NpcWorldSim] Template '{template.id}' is not in this sim's list, so a " +
                               "save could not restore the group. Add it (runtimeOnly).", this);
                return null;
            }

            var group = new NpcGroup
            {
                Id = groupId,
                TemplateId = template.id,
                Position = start,
                RosterSeed = RosterDraw.StableHash(groupId),
            };

            groups.Add(group);
            Log($"{template.displayName} created as '{groupId}' at {start:F0}");
            return group;
        }

        public NpcGroup FindGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return null;

            foreach (NpcGroup group in groups)
                if (group.Id == groupId) return group;

            return null;
        }

        /// <summary>Remove a group now, despawning any live members through the netcode path.</summary>
        public void DisbandGroup(string groupId)
        {
            NpcGroup group = FindGroup(groupId);
            if (group == null) return;

            DespawnTransport(group);
            DespawnMembers(group);
            groups.Remove(group);
            Log($"'{groupId}' disbanded");
        }

        /// <summary>
        /// Stop a group being anyone's war party. Folded with no vessel still out: removed at once.
        /// Otherwise its quarry is cleared and it is removed once it has folded and its vessel is gone,
        /// so players never watch either vanish.
        /// </summary>
        public void ReleaseGroup(string groupId)
        {
            NpcGroup group = FindGroup(groupId);
            if (group == null) return;

            if (!group.Spawned && group.Transport == null)
            {
                DisbandGroup(groupId);
                return;
            }

            group.QuarryProfileId = string.Empty;
            group.DisbandWhenFolded = true;
        }

        /// <summary>The template a tribe's war parties are created from: runtime-only, hunting, that tribe's.</summary>
        public NpcGroupTemplate WarPartyTemplateFor(FactionDefinition tribe)
        {
            if (tribe == null || templates == null) return null;

            foreach (NpcGroupTemplate template in templates)
                if (template != null && template.runtimeOnly && template.bountyHunters && template.tribe == tribe)
                    return template;

            return null;
        }

        public FactionDefinition TribeOf(NpcGroup group) => TemplateFor(group)?.tribe;

        /// <summary>The group has a transport and has not been dropped off yet: it is flying, not walking.</summary>
        public bool IsInFlight(NpcGroup group) => TemplateFor(group) is { } template && InFlight(group, template);

        private static bool InFlight(NpcGroup group, NpcGroupTemplate template) =>
            template.transport != null && template.transport.Flies && !group.Delivered;

        public void CollectPlayerPositions(List<Vector3> into)
        {
            into.Clear();
            foreach (Transform player in players)
                if (player != null) into.Add(player.position);
        }

        // ── The tick ─────────────────────────────────────────────────────────────

        private void TickGroup(NpcGroup group, float delta)
        {
            NpcGroupTemplate template = TemplateFor(group);
            if (template == null) return;

            // First: a wiped-out or released party's vessel still has to leave before it can be removed.
            TickTransport(group, template, delta);

            // Wiped out: waits for the director to resolve it rather than re-spawning at full strength.
            if (group.WipedOut) return;

            if (group.Spawned)
            {
                TickSpawned(group, template, delta);
                return;
            }

            // Released and folded: only waiting for its vessel to fly out of sight.
            if (group.DisbandWhenFolded) return;

            TickVirtual(group, template, delta);

            if (NearestPlayerDistance(group.Position) <= spawnRadius)
                Spawn(group, template);
        }

        private void TickSpawned(NpcGroup group, NpcGroupTemplate template, float delta)
        {
            PruneDead(group);

            // Live alone is not the party: a rider who dismounted is in no mount's saddle and still
            // fights. Folding here would re-spawn fresh riders beside them, or call the party beaten.
            if (WarPartyRules.IsWipedOut(group.Live.Count, GroupMembership.CountStanding(group.Fighters)))
            {
                group.Spawned = false;
                if (group.IsWarParty) group.WipedOut = true;
                Log($"{template.displayName} was wiped out");
                return;
            }

            group.Position = CurrentPosition(group);

            if (group.IsWarParty) RefreshQuarryLead(group, delta);
            else if (template.bountyHunters) RefreshLead(group, delta);

            if (NearestPlayerDistance(group.Position) > despawnRadius)
                Despawn(group, template);
        }

        private void TickVirtual(NpcGroup group, NpcGroupTemplate template, float delta)
        {
            if (group.IsWarParty)
            {
                TickWarPartyVirtual(group, template, delta);
                return;
            }

            if (template.bountyHunters)
            {
                TickHunterVirtual(group, template, delta);
                return;
            }

            if (group.DwellRemaining > 0f)
            {
                group.DwellRemaining -= delta;
                return;
            }

            if (!group.HasGoal)
            {
                ChooseTask(group, template);
                return;
            }

            if (!group.AdvanceToward(FoldedSpeed(group, template), delta)) return;

            // Arrived. Dwell for as long as the task says, exactly as the live module would.
            group.HasGoal = false;

            NpcTask task = TaskFor(template, group.TaskIndex);
            group.DwellRemaining = task != null ? task.RollDwell() : 15f;
        }

        private void TickHunterVirtual(NpcGroup group, NpcGroupTemplate template, float delta)
        {
            if (group.HasLead)
            {
                group.LeadAge += delta;

                if (group.LeadAge > leadLifetime)
                {
                    // Cold. Back to sweeping — which is what makes going quiet actually work as a
                    // way of losing them, rather than the squad being permanently omniscient.
                    group.HasLead = false;
                    Log($"{template.displayName} lost the trail");
                }
                else
                {
                    group.GoalPosition = group.Lead;
                    group.ArriveRadius = 20f;
                    group.HasGoal = true;
                }
            }

            if (!group.HasGoal)
            {
                group.GoalPosition = NpcTaskPlanner.RoamPointUnsampled(group.Position, hunterRoamRadius, null);
                group.ArriveRadius = 25f;
                group.HasGoal = true;
            }

            if (!group.AdvanceToward(FoldedSpeed(group, template), delta)) return;

            group.HasGoal = false;

            // Reaching a stale lead is what finally clears it: they went and looked, and you were
            // not there.
            if (group.HasLead && group.FlatDistanceTo(group.Lead) < 40f)
            {
                group.HasLead = false;
                Log($"{template.displayName} searched the last sighting and found nothing");
            }
        }

        /// <summary>
        /// A war party's lead is the director's (rosters spec §5.2): it never goes cold, and reaching it
        /// is not the end of the search — the director will refresh it, and sets the arrive radius with
        /// it. With no lead yet it waits.
        /// </summary>
        private static void TickWarPartyVirtual(NpcGroup group, NpcGroupTemplate template, float delta)
        {
            if (!group.HasLead) return;

            group.LeadAge += delta;
            group.GoalPosition = group.Lead;
            group.HasGoal = true;
            group.AdvanceToward(FoldedSpeed(group, template), delta);
        }

        /// <summary>A record still on its way in a vessel moves at the vessel's speed; altitude does not matter folded.</summary>
        private static float FoldedSpeed(NpcGroup group, NpcGroupTemplate template) =>
            InFlight(group, template) ? template.transport.travelSpeed : template.travelSpeed;

        private void ChooseTask(NpcGroup group, NpcGroupTemplate template)
        {
            if (template.tasks == null || template.tasks.Length == 0)
            {
                group.GoalPosition = NpcTaskPlanner.RoamPointUnsampled(group.Position, 600f, null);
                group.ArriveRadius = 15f;
                group.HasGoal = true;
                return;
            }

            int next = NpcTaskPlanner.PickTask(template.tasks, group.TaskIndex);
            if (next < 0) return;

            group.TaskIndex = next;
            NpcTask task = template.tasks[next];

            // The identical call the live module makes, which is the point of NpcTaskPlanner being
            // static: a group that spawns mid-journey continues the job its record was doing rather
            // than re-deciding on different rules.
            if (NpcTaskPlanner.ResolveDestination(task, group.Position, group.LastSiteId,
                                                  out Vector3 destination, out float radius,
                                                  out string siteId, out string siteName))
            {
                group.GoalPosition = destination;
                group.ArriveRadius = radius;
                group.LastSiteId = siteId;
                group.HasGoal = true;

                Log($"{template.displayName}: {task.label} → " +
                    $"{(string.IsNullOrEmpty(siteName) ? destination.ToString("F0") : siteName)}");
                return;
            }

            group.GoalPosition = NpcTaskPlanner.RoamPointUnsampled(group.Position, task.searchRadius, null);
            group.ArriveRadius = task.arriveRadius;
            group.HasGoal = true;
        }

        // ── Spawning ─────────────────────────────────────────────────────────────

        private void Spawn(NpcGroup group, NpcGroupTemplate template)
        {
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);
            if (plan.Count == 0) return;

            group.Live.Clear();
            group.Fighters.Clear();
            group.FightersSpawned = 0;
            group.FightersDead = 0;
            group.QuarrySeenThisSpawn = false;

            // The riders are counted before anyone is spawned, because the count chooses the vessel.
            int riderCount = plan.Count(NpcGroupComposition.Rides);
            GameObject vessel = InFlight(group, template) && riderCount > 0
                ? BoardTransport(group, template, riderCount)
                : null;
            List<GameObject> riders = vessel != null ? new List<GameObject>() : null;
            int seats = vessel != null ? vessel.GetComponent<VesselSeats>().Capacity : 0;

            if (vessel != null && riderCount > seats)
                Debug.LogWarning($"[NpcWorldSim] '{group.Id}' has {riderCount} riders for {seats} seats on " +
                                 $"'{vessel.name}'; {riderCount - seats} go on foot. Give '{template.id}' a larger vessel.", this);

            // Under the vessel, so the riders are put on the NavMesh right below the seats they take.
            Vector3 origin = vessel != null
                ? new Vector3(vessel.transform.position.x, group.Position.y, vessel.transform.position.z)
                : group.Position;
            Vector3 heading = group.Heading;
            int followerIndex = 0;
            bool leaderTaken = false;

            for (int index = 0; index < plan.Count; index++)
            {
                PlannedMember planned = plan[index];
                if (planned.Prefab == null) continue;

                bool leads = planned.Leads && !leaderTaken;

                Vector3 slot = leads
                    ? origin
                    : FormationMath.SlotPosition(followerIndex, origin, heading,
                                                 template.formation, followerIndex * 7919, 0f);

                if (!leads) followerIndex++;

                // Known before the spawn, so a rider is made with its NavMeshAgent already off.
                bool seated = riders != null && NpcGroupComposition.Rides(planned) && riders.Count < seats;

                // Stamped before the network spawn, so the loadout roll in OnNetworkSpawn is seeded.
                int memberIndex = index;
                GameObject member = SpawnMember(planned.Prefab, slot, heading, seated,
                    instance => GroupMembership.Stamp(instance, group, memberIndex, template.tribe));
                if (member == null) continue;

                leaderTaken |= leads;
                group.Live.Add(member);
                if (seated) riders.Add(member);

                Configure(member, group, template, leads);
            }

            if (vessel != null) Launch(vessel, group, template, riders);

            if (group.Live.Count == 0) return;

            // Nobody was flagged, so the first spawned leads. FormationModule falls back the same
            // way, but making it explicit here means the task list lands on the right member.
            if (!leaderTaken && group.Live[0].TryGetComponent(out FormationModule first))
                first.SetFormation(group.Id, true);

            group.Spawned = true;
            Log($"{template.displayName} spawned ({group.Live.Count} members)");
        }

        /// <summary>
        /// The vessel a group still on its way flies in, chosen by how many ride: an empty one of that
        /// kind parked at its dock nearby if there is one, else a new one at the group's position lifted
        /// to cruise height before anyone can see it, prow toward the quarry. With no flyable vessel the
        /// party is logged and put down on foot rather than not appearing at all.
        /// </summary>
        private GameObject BoardTransport(NpcGroup group, NpcGroupTemplate template, int riders)
        {
            GameObject prefab = template.transport.VesselFor(riders);
            if (prefab == null || prefab.GetComponent<VesselPilot>() == null)
            {
                Debug.LogError($"[NpcWorldSim] '{group.Id}' should fly in, but '{template.id}' has no vessel " +
                               $"with a VesselPilot for {riders} riders; it spawns on foot.", this);
                group.Delivered = true;
                return null;
            }

            if (TryTakeParkedHull(group, prefab)) return group.Transport;

            GameObject vessel = NpcSpawn.Create(prefab, group.Position, FacingQuarry(group), this,
                instance => instance.GetComponent<VesselPilot>().RiseToCruise(QuarryPoint(group)));
            if (vessel == null) return null;

            group.Transport = vessel;
            group.TransportPrefab = prefab;
            group.TransportDock = FirstFreeDock(group);
            return vessel;
        }

        /// <summary>
        /// Board a hull of <paramref name="prefab"/>'s kind that another group left parked empty at its
        /// dock within <see cref="dockReuseRadius"/>: no second hull spawned into the one already there.
        /// It keeps its dock slot.
        /// </summary>
        private bool TryTakeParkedHull(NpcGroup group, GameObject prefab)
        {
            foreach (NpcGroup other in groups)
            {
                if (other == group || other.Transport == null || other.TransportPrefab != prefab) continue;
                if (!other.Transport.TryGetComponent(out VesselPilot pilot) || pilot.IsWrecked ||
                    pilot.State != VesselMissionState.Done || pilot.Seats.Occupied > 0) continue;
                if (FlatDistance(other.Transport.transform.position, group.Position) > dockReuseRadius) continue;

                group.Transport = other.Transport;
                group.TransportPrefab = other.TransportPrefab;
                group.TransportDock = other.TransportDock;
                ForgetTransport(other);
                Log($"'{group.Id}' boards the hull '{other.Id}' left parked");
                return true;
            }

            return false;
        }

        private int FirstFreeDock(NpcGroup group)
        {
            var taken = new HashSet<int>();
            foreach (NpcGroup other in groups)
                if (other != group && other.Transport != null && other.TransportDock >= 0) taken.Add(other.TransportDock);

            return NpcGroupTransport.FirstFreeDock(taken);
        }

        /// <summary>Seat the riders and send the vessel after the group's quarry; it flies back to its dock.</summary>
        private void Launch(GameObject vessel, NpcGroup group, NpcGroupTemplate template, IReadOnlyList<GameObject> riders)
        {
            group.TransportParkedFor = 0f;
            vessel.GetComponent<VesselPilot>().Begin(DockOf(group, template), () => QuarryPoint(group), riders, despawnRadius);
        }

        /// <summary>The group's dock slot around the transport's home site, or around the group's own position with no site registered.</summary>
        private static Vector3 DockOf(NpcGroup group, NpcGroupTemplate template)
        {
            Vector3 home = WorldSiteRegistry.TryFindByName(template.transport.homeSiteName, out WorldSite site)
                ? site.Position
                : group.Position;

            return NpcGroupTransport.DockPoint(home, group.TransportDock, template.transport.dockSpacing);
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>Where the group is headed: the director's fix on its quarry, else its goal.</summary>
        private static Vector3 QuarryPoint(NpcGroup group) =>
            group.HasLead ? group.Lead : group.HasGoal ? group.GoalPosition : group.Position;

        private static Quaternion FacingQuarry(NpcGroup group)
        {
            Vector3 toward = QuarryPoint(group) - group.Position;
            toward.y = 0f;
            return toward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toward, Vector3.up) : Quaternion.identity;
        }

        private GameObject SpawnMember(GameObject prefab, Vector3 position, Vector3 heading, bool seated,
                                       Action<GameObject> beforeSpawn)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, spawnSampleDistance, NavMesh.AllAreas))
                position = hit.position;

            Quaternion rotation = heading.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(heading, Vector3.up)
                : Quaternion.identity;

            return NpcSpawn.Create(prefab, position, rotation, this, beforeSpawn, seated);
        }

        private void Configure(GameObject member, NpcGroup group, NpcGroupTemplate template, bool leads)
        {
            if (member.TryGetComponent(out FormationModule formation))
            {
                formation.SetFormation(group.Id, leads);
                formation.SetShape(template.formation);
            }

            // Only the leader gets the task list. A follower with its own tasks would set its own
            // goal, and GoalTravelModule sits below the formation — so it would spend the journey
            // being pulled two ways and arrive at neither.
            if (leads && member.TryGetComponent(out NpcTaskModule tasks))
            {
                tasks.SetTasks(template.tasks, group.TaskIndex);
                tasks.SetHome(group.Position);

                if (group.HasGoal && member.TryGetComponent(out AgentGoal goal))
                    SetGoal(goal, group);
            }

            // See SceneTracked.SetKeepChunksLoaded. A spawned member is by definition within
            // despawnRadius of a player, so its chunk is loaded anyway — pinning would mean every
            // group in the world dragging nine chunks behind it.
            if (member.TryGetComponent(out SceneTracked tracked))
                tracked.SetKeepChunksLoaded(false);

            // This group's record owns its members, so the world store must not also save them
            // individually — one caravan in, two caravans out. That disowning is NpcSpawn.Create's
            // job now, so that it also covers the riders a member seats on itself; see the note
            // there for what a member's own world record actually costs.
        }

        private void SetGoal(AgentGoal goal, NpcGroup group)
        {
            if (!goal.TrySetSampled(group.GoalPosition, group.ArriveRadius, spawnSampleDistance))
                goal.Set(group.GoalPosition, group.ArriveRadius);
        }

        /// <summary>
        /// Send a spawned group's leader somewhere new, the way <see cref="Configure"/> does at spawn.
        /// The director decides when a war party's trail is refreshed; this only moves it. Server only.
        /// </summary>
        public void SteerSpawned(NpcGroup group, Vector3 point, float arriveRadius)
        {
            if (group == null || !group.Spawned) return;

            group.GoalPosition = point;
            group.ArriveRadius = arriveRadius;
            group.HasGoal = true;

            foreach (GameObject member in group.Live)
            {
                if (member == null) continue;
                if (!member.TryGetComponent(out FormationModule formation) || !formation.IsLeader) continue;

                if (member.TryGetComponent(out AgentGoal goal)) SetGoal(goal, group);
                return;
            }
        }

        // ── Despawning ───────────────────────────────────────────────────────────

        private void Despawn(NpcGroup group, NpcGroupTemplate template)
        {
            group.Position = CurrentPosition(group);

            // Read the leader's live goal back into the record, so a caravan that chose a new
            // destination while it was real does not forget it the moment it folds away.
            foreach (GameObject member in group.Live)
            {
                if (member == null) continue;
                if (!member.TryGetComponent(out FormationModule formation) || !formation.IsLeader) continue;

                if (member.TryGetComponent(out AgentGoal goal) && goal.HasGoal)
                {
                    group.GoalPosition = goal.Position;
                    group.ArriveRadius = goal.ArriveRadius;
                    group.HasGoal = true;
                }

                if (member.TryGetComponent(out NpcTaskModule tasks))
                    group.TaskIndex = tasks.CurrentTaskIndex;

                break;
            }

            // Still aboard: the vessel folds with its riders, and the record flies on from where it was.
            // Dropped off: the empty vessel is TickTransport's, unless it is already out of sight too.
            if (group.Transport != null &&
                (!group.Delivered || NearestPlayerDistance(group.Transport.transform.position) > despawnRadius))
                DespawnTransport(group);

            DespawnMembers(group);
            group.Spawned = false;
            Log($"{template.displayName} folded back to a record");
        }

        /// <summary>
        /// The vessel a group flew in on. It is the group's ride until the party is off (nobody left
        /// seated, the vessel shot down or gone) and from then only a hull flying home, taken away once
        /// every player is beyond despawnRadius. A run that came home with the party still aboard (no
        /// landing site) is not a delivery: in sight it waits transportRetryDelay at its dock and flies
        /// the drop again; out of sight the group folds with it and flies in afresh. Polled rather than
        /// waiting for VesselPilot.Finished, which a vessel despawned mid-run never raises.
        /// </summary>
        private void TickTransport(NpcGroup group, NpcGroupTemplate template, float delta)
        {
            if (!InFlight(group, template) && group.Transport == null) return;

            VesselPilot pilot = group.Transport != null ? group.Transport.GetComponent<VesselPilot>() : null;
            bool wrecked = pilot != null && pilot.IsWrecked;
            int aboard = pilot != null ? pilot.Seats.Occupied : 0;

            if (!group.Delivered && (group.Spawned || group.Transport != null) &&
                NpcGroupTransport.IsDelivered(pilot != null, wrecked, aboard))
            {
                group.Delivered = true;
                Log($"{template.displayName} was dropped off");
            }

            if (group.Transport == null)
            {
                ForgetTransport(group);   // destroyed by its own wreck timer: let go of the dead reference
                return;
            }

            if (!group.Delivered)
            {
                bool runDone = pilot != null && pilot.State == VesselMissionState.Done;
                group.TransportParkedFor = runDone ? group.TransportParkedFor + delta : 0f;

                if (NpcGroupTransport.ShouldRelaunch(runDone, wrecked, aboard, group.TransportParkedFor, transportRetryDelay))
                {
                    Log($"{template.displayName} flies the drop again");
                    Launch(group.Transport, group, template, Array.Empty<GameObject>());
                }

                return;
            }

            if (NearestPlayerDistance(group.Transport.transform.position) > despawnRadius)
                DespawnTransport(group);
        }

        /// <summary>
        /// Despawn a group's vessel. Before its members, always: the hull's despawn hook puts anyone
        /// still seated down on the NavMesh below, so a member despawned afterwards is a plain NPC,
        /// never a seated body netcode lifts off a vanished hull.
        /// </summary>
        private static void DespawnTransport(NpcGroup group)
        {
            DestroyMember(group.Transport);
            ForgetTransport(group);
        }

        private static void ForgetTransport(NpcGroup group)
        {
            group.Transport = null;
            group.TransportPrefab = null;
            group.TransportDock = -1;
            group.TransportParkedFor = 0f;
        }

        /// <summary>A spawned group's position: its vessel's while aboard, else its members' centroid.</summary>
        private static Vector3 CurrentPosition(NpcGroup group) =>
            !group.Delivered && group.Transport != null ? group.Transport.transform.position : Centroid(group);

        /// <summary>
        /// Take a group's bodies out of the world: its spawned members, and every fighter that is no
        /// longer one of them — a rider who dismounted is tracked by nothing else and would otherwise
        /// stand in the desert for the rest of the session. A rider still in a live mount's saddle is
        /// left to that mount: NpcPassenger.OnDestroy despawns the rider it seated, and doing it here
        /// as well would despawn it twice.
        /// </summary>
        private static void DespawnMembers(NpcGroup group)
        {
            foreach (GameObject fighter in group.Fighters)
                if (fighter != null && !group.Live.Contains(fighter) && !IsSeatedInGroup(group, fighter))
                    DestroyMember(fighter);

            foreach (GameObject member in group.Live)
                DestroyMember(member);

            group.Live.Clear();
            group.Fighters.Clear();
        }

        private static bool IsSeatedInGroup(NpcGroup group, GameObject rider)
        {
            foreach (GameObject member in group.Live)
                if (member != null && member.TryGetComponent(out NpcPassenger passenger) && passenger.Rider == rider)
                    return true;

            return false;
        }

        private static void DestroyMember(GameObject member)
        {
            if (member == null) return;

            if (member.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
            {
                netObj.Despawn(destroy: true);
                return;
            }

            Destroy(member);
        }

        private static void PruneDead(NpcGroup group)
        {
            for (int i = group.Live.Count - 1; i >= 0; i--)
                if (group.Live[i] == null) group.Live.RemoveAt(i);
        }

        private static Vector3 Centroid(NpcGroup group)
        {
            if (group.Live.Count == 0) return group.Position;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (GameObject member in group.Live)
            {
                if (member == null) continue;
                sum += member.transform.position;
                count++;
            }

            return count > 0 ? sum / count : group.Position;
        }

        // ── Bounty hunter leads ──────────────────────────────────────────────────

        /// <summary>
        /// While a hunting squad is real, its members' own targeting is the lead. Anything they
        /// currently see refreshes it; losing you leaves the last sighting behind, which is what
        /// they walk to once they fold back into a record.
        /// </summary>
        private void RefreshLead(NpcGroup group, float delta)
        {
            foreach (GameObject member in group.Live)
            {
                if (member == null) continue;
                if (!member.TryGetComponent(out AgentTargeting targeting)) continue;

                if (targeting.HasTarget && targeting.CanSeeTarget)
                {
                    group.Lead = targeting.Target.position;
                    group.HasLead = true;
                    group.LeadAge = 0f;
                    return;
                }

                if (targeting.HasLastKnownPosition && !group.HasLead)
                {
                    group.Lead = targeting.LastKnownPosition;
                    group.HasLead = true;
                    group.LeadAge = targeting.TimeSinceSeen;
                }
            }

            if (group.HasLead) group.LeadAge += delta;
        }

        /// <summary>
        /// A spawned war party follows only its quarry. Anybody else its members shoot at is a fight on
        /// the way, not a lead — and ReportSighting cannot say whose position it carries.
        /// </summary>
        private void RefreshQuarryLead(NpcGroup group, float delta)
        {
            foreach (GameObject member in group.Live)
            {
                GameObject fighter = GroupMembership.FighterOf(member);
                if (fighter == null || !fighter.TryGetComponent(out AgentTargeting targeting)) continue;
                if (!targeting.HasTarget || !targeting.CanSeeTarget || !IsQuarry(group, targeting.Target)) continue;

                group.Lead = targeting.Target.position;
                group.HasLead = true;
                group.LeadAge = 0f;

                if (!group.QuarrySeenThisSpawn)
                {
                    group.QuarrySeenThisSpawn = true;
                    QuarrySighted?.Invoke(group, fighter);
                }

                return;
            }

            if (group.HasLead) group.LeadAge += delta;
        }

        private static bool IsQuarry(NpcGroup group, Transform target)
        {
            EntityFaction entity = target != null ? target.GetComponentInParent<EntityFaction>() : null;
            PlayerSaveService players = SaveManager.Instance?.Players;

            return entity != null && players != null
                   && players.TryGetProfileFor(entity.gameObject, out string profileId)
                   && profileId == group.QuarryProfileId;
        }

        /// <summary>Tell every hunting squad where a player just was. For noise, gunfire, witnesses.</summary>
        public void ReportSighting(Vector3 position)
        {
            foreach (NpcGroup group in groups)
            {
                NpcGroupTemplate template = TemplateFor(group);
                if (template == null || !template.bountyHunters || group.IsWarParty || group.DisbandWhenFolded) continue;

                group.Lead = position;
                group.HasLead = true;
                group.LeadAge = 0f;
            }
        }

        // ── Players ──────────────────────────────────────────────────────────────

        private void RefreshPlayers()
        {
            players.Clear();

            NetworkManager manager = NetworkManager.Singleton;

            if (manager != null && manager.IsListening && manager.IsServer)
            {
                foreach (NetworkClient client in manager.ConnectedClientsList)
                {
                    if (client?.PlayerObject != null)
                        players.Add(client.PlayerObject.transform);
                }

                if (players.Count > 0) return;
            }

            // Offline, or a session with no spawned player objects yet.
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                players.Add(player.transform);
        }

        private float NearestPlayerDistance(Vector3 position)
        {
            float best = float.PositiveInfinity;

            foreach (Transform player in players)
            {
                if (player == null) continue;

                Vector3 delta = player.position - position;
                delta.y = 0f;

                float distance = delta.magnitude;
                if (distance < best) best = distance;
            }

            return best;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private NpcGroupTemplate TemplateFor(NpcGroup group) =>
            group != null && group.TemplateId != null && templatesById.TryGetValue(group.TemplateId, out NpcGroupTemplate t)
                ? t
                : null;

        private static NpcTask TaskFor(NpcGroupTemplate template, int index) =>
            template.tasks != null && index >= 0 && index < template.tasks.Length
                ? template.tasks[index]
                : null;

        private void Log(string message)
        {
            if (logGroupEvents) Debug.Log($"[NpcWorldSim] {message}", this);
        }

        // ── Save support ─────────────────────────────────────────────────────────

        /// <summary>Every group's record, for the save file.</summary>
        public NpcGroup.Record[] CaptureRecords()
        {
            var records = new NpcGroup.Record[groups.Count];

            for (int i = 0; i < groups.Count; i++)
            {
                // A spawned group's record is stale — its members have walked since. Refresh the
                // position from them so a save taken while you are standing next to a caravan puts
                // it back where you last saw it, not where it was when it spawned.
                if (groups[i].Spawned) groups[i].Position = CurrentPosition(groups[i]);

                records[i] = groups[i].ToRecord();
            }

            return records;
        }

        /// <summary>
        /// Replace live state with a save's.
        ///
        /// Spawned groups are torn down first: the members standing in the world belong to the
        /// session being replaced, and leaving them would put two copies of one caravan in the
        /// world — one at the loaded position and one where the previous session left it.
        /// </summary>
        public void RestoreRecords(NpcGroup.Record[] records)
        {
            foreach (NpcGroup group in groups)
            {
                DespawnTransport(group);
                if (!group.Spawned) continue;

                DespawnMembers(group);
                group.Spawned = false;
            }

            if (records == null) return;

            BuildTemplateIndex();

            // A runtime group belongs to the session being replaced, whatever id it has: its
            // [NonSerialized] WipedOut and DisbandWhenFolded are this session's, and matching one in
            // place by a repeated id (warparty:sand:p:N) would carry them into the loaded party —
            // frozen forever, deleted next tick, or kept with no quarry as a generic hunter. Remove
            // every runtime group here; the record loop below rebuilds each one fresh from its own
            // record, so the "released mid-fold" skip applies uniformly with no live group to fall
            // back into ApplyRecord. Seeded groups are matched by id as before.
            groups.RemoveAll(IsRuntime);

            var byId = new Dictionary<string, NpcGroup>();
            foreach (NpcGroup group in groups) byId[group.Id] = group;

            foreach (NpcGroup.Record record in records)
            {
                if (string.IsNullOrEmpty(record.id)) continue;

                if (byId.TryGetValue(record.id, out NpcGroup existing))
                {
                    existing.ApplyRecord(in record);
                    continue;
                }

                // A record for a template that no longer exists. Skipped rather than resurrected:
                // there is nothing to spawn it from, and keeping it would mean the save grows a
                // permanent entry for a group that can never appear.
                if (!templatesById.ContainsKey(record.templateId ?? string.Empty)) continue;

                // A released war party saved mid-fold: it was already leaving. Restoring it would leave a
                // hunter with nobody to hunt.
                if (templatesById[record.templateId].runtimeOnly && string.IsNullOrEmpty(record.quarryProfileId))
                    continue;

                // Seeded as CreateGroup seeds it, so a record from before rosterSeed existed still draws
                // the same people (ApplyRecord keeps this when the record's seed reads 0).
                var restored = new NpcGroup
                {
                    Id = record.id,
                    TemplateId = record.templateId,
                    RosterSeed = RosterDraw.StableHash(record.id),
                };
                restored.ApplyRecord(in record);
                groups.Add(restored);
            }
        }

        private bool IsRuntime(NpcGroup group) => TemplateFor(group) is { runtimeOnly: true };

        private void OnValidate()
        {
            spawnRadius = Mathf.Max(20f, spawnRadius);
            despawnRadius = Mathf.Max(spawnRadius + 50f, despawnRadius);
            spawnSampleDistance = Mathf.Max(1f, spawnSampleDistance);
            transportRetryDelay = Mathf.Max(0f, transportRetryDelay);
            dockReuseRadius = Mathf.Max(0f, dockReuseRadius);
            tickInterval = Mathf.Clamp(tickInterval, 0.1f, 10f);
            playerRefreshInterval = Mathf.Max(0.5f, playerRefreshInterval);
            leadLifetime = Mathf.Max(10f, leadLifetime);
            hunterRoamRadius = Mathf.Max(50f, hunterRoamRadius);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || groups.Count == 0) return;

            foreach (NpcGroup group in groups)
            {
                NpcGroupTemplate template = TemplateFor(group);
                bool hunter = template != null && template.bountyHunters;

                Gizmos.color = group.Spawned
                    ? Color.green
                    : hunter ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 0.7f, 1f);

                Gizmos.DrawWireSphere(group.Position, 8f);

                if (group.HasGoal)
                    Gizmos.DrawLine(group.Position, group.GoalPosition);
            }
        }
    }
}
