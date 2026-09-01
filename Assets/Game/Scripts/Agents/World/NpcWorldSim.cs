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
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Characters;
using SpaceGame.Core;
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
                if (template == null || string.IsNullOrWhiteSpace(template.id)) continue;

                var group = new NpcGroup
                {
                    Id = template.id,
                    TemplateId = template.id,
                    Position = ResolveStart(template),
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

        // ── The tick ─────────────────────────────────────────────────────────────

        private void TickGroup(NpcGroup group, float delta)
        {
            NpcGroupTemplate template = TemplateFor(group);
            if (template == null) return;

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

            if (group.Live.Count == 0)
            {
                // Wiped out. The record stays so the save still knows this group existed and where
                // it fell, but it never spawns again.
                group.Spawned = false;
                Log($"{template.displayName} was wiped out");
                return;
            }

            group.Position = Centroid(group);

            if (template.bountyHunters) RefreshLead(group, delta);

            if (NearestPlayerDistance(group.Position) > despawnRadius)
                Despawn(group, template);
        }

        private void TickVirtual(NpcGroup group, NpcGroupTemplate template, float delta)
        {
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
            if (template.members == null || template.members.Length == 0) return;

            group.Live.Clear();

            Vector3 heading = group.Heading;
            int followerIndex = 0;
            bool leaderTaken = false;

            foreach (NpcGroupMemberSpec spec in template.members)
            {
                if (spec == null || spec.prefab == null) continue;

                for (int i = 0; i < Mathf.Max(1, spec.count); i++)
                {
                    bool leads = spec.isLeader && !leaderTaken;

                    Vector3 slot = leads
                        ? group.Position
                        : FormationMath.SlotPosition(followerIndex, group.Position, heading,
                                                     template.formation, followerIndex * 7919, 0f);

                    if (!leads) followerIndex++;

                    GameObject member = SpawnMember(spec.prefab, slot, heading);
                    if (member == null) continue;

                    leaderTaken |= leads;
                    group.Live.Add(member);

                    Configure(member, group, template, leads);
                }
            }

            if (group.Live.Count == 0) return;

            // Nobody was flagged, so the first spawned leads. FormationModule falls back the same
            // way, but making it explicit here means the task list lands on the right member.
            if (!leaderTaken && group.Live[0].TryGetComponent(out FormationModule first))
                first.SetFormation(group.Id, true);

            group.Spawned = true;
            Log($"{template.displayName} spawned ({group.Live.Count} members)");
        }

        private GameObject SpawnMember(GameObject prefab, Vector3 position, Vector3 heading)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, spawnSampleDistance, NavMesh.AllAreas))
                position = hit.position;

            Quaternion rotation = heading.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(heading, Vector3.up)
                : Quaternion.identity;

            return NpcSpawn.Create(prefab, position, rotation, this);
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
                {
                    if (!goal.TrySetSampled(group.GoalPosition, group.ArriveRadius, spawnSampleDistance))
                        goal.Set(group.GoalPosition, group.ArriveRadius);
                }
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

            foreach (GameObject member in group.Live)
                DestroyMember(member);

            group.Live.Clear();
            group.Spawned = false;
            Log($"{template.displayName} folded back to a record");
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

        /// <summary>Tell every hunting squad where a player just was. For noise, gunfire, witnesses.</summary>
        public void ReportSighting(Vector3 position)
        {
            foreach (NpcGroup group in groups)
            {
                NpcGroupTemplate template = TemplateFor(group);
                if (template == null || !template.bountyHunters) continue;

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

                foreach (GameObject member in group.Live)
                    DestroyMember(member);

                group.Live.Clear();
                group.Spawned = false;
            }

            if (records == null) return;

            BuildTemplateIndex();

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

                var restored = new NpcGroup { Id = record.id, TemplateId = record.templateId };
                restored.ApplyRecord(in record);
                groups.Add(restored);
            }
        }

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
