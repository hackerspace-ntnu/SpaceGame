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
// Put one of these in the persistent scene. It is server-only — NPC decisions belong on the machine
// that simulates them, and a client running its own copy would produce a different caravan in a
// different place with the same name.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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

            // A released group leaves once nobody can see it go (rosters plan: never popped out of view).
            groups.RemoveAll(g => g.DisbandWhenFolded && !g.Spawned);
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

            DespawnMembers(group);
            groups.Remove(group);
            Log($"'{groupId}' disbanded");
        }

        /// <summary>
        /// Stop a group being anyone's war party. Folded: removed at once. Spawned: its quarry is cleared
        /// and it is removed when it folds, so players never watch it vanish.
        /// </summary>
        public void ReleaseGroup(string groupId)
        {
            NpcGroup group = FindGroup(groupId);
            if (group == null) return;

            if (!group.Spawned)
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

            // Wiped out: waits for the director to resolve it rather than re-spawning at full strength.
            if (group.WipedOut) return;

            if (group.Spawned)
            {
                TickSpawned(group, template, delta);
                return;
            }

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

            group.Position = Centroid(group);

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

            if (!group.AdvanceToward(template.travelSpeed, delta)) return;

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

            if (!group.AdvanceToward(template.travelSpeed, delta)) return;

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
            group.AdvanceToward(template.travelSpeed, delta);
        }

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

            Vector3 heading = group.Heading;
            int followerIndex = 0;
            bool leaderTaken = false;

            for (int index = 0; index < plan.Count; index++)
            {
                PlannedMember planned = plan[index];
                if (planned.Prefab == null) continue;

                bool leads = planned.Leads && !leaderTaken;

                Vector3 slot = leads
                    ? group.Position
                    : FormationMath.SlotPosition(followerIndex, group.Position, heading,
                                                 template.formation, followerIndex * 7919, 0f);

                if (!leads) followerIndex++;

                // Stamped before the network spawn, so the loadout roll in OnNetworkSpawn is seeded.
                int memberIndex = index;
                GameObject member = SpawnMember(planned.Prefab, slot, heading,
                    instance => GroupMembership.Stamp(instance, group, memberIndex, template.tribe));
                if (member == null) continue;

                leaderTaken |= leads;
                group.Live.Add(member);

                Configure(member, group, template, leads);
            }

            if (group.Live.Count == 0) return;

            // Nobody was flagged, so the first spawned leads. FormationModule falls back the same
            // way, but making it explicit here means the task list lands on the right member.
            if (!leaderTaken && group.Live[0].TryGetComponent(out FormationModule first))
                first.SetFormation(group.Id, true);

            group.Spawned = true;
            Log($"{template.displayName} spawned ({group.Live.Count} members)");
        }

        private GameObject SpawnMember(GameObject prefab, Vector3 position, Vector3 heading,
                                       Action<GameObject> beforeSpawn)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, spawnSampleDistance, NavMesh.AllAreas))
                position = hit.position;

            Quaternion rotation = heading.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(heading, Vector3.up)
                : Quaternion.identity;

            return NpcSpawn.Create(prefab, position, rotation, this, beforeSpawn);
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
            group.Position = Centroid(group);

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

            DespawnMembers(group);
            group.Spawned = false;
            Log($"{template.displayName} folded back to a record");
        }

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
                if (groups[i].Spawned) groups[i].Position = Centroid(groups[i]);

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
