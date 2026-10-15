// Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs
// Flies a Sky nomad on its wing pack. When where it is going is too far to walk — its own goal, or for a
// formation follower its leader's (D5: each pilot flies alone to the shared goal) — it deploys the
// same craft the player flies (NpcOrnithopter), rides it there and steps off; GoalTravelModule walks the
// last metres. The craft does the flying (NpcAviator); this module only decides and launches, and stops
// ticking by itself once seated (AgentController.RidesAsPassenger runs side-effect modules only).
//
// When (D2): a goal further than minFlightDistance, a wing pack worn, and room to launch — already in the
// air, or minLaunchClearance of empty sky above it (the NPC craft just climbs away: NpcFlightPlan, no
// energy model, so no ledge is needed), and launchDelayAfterSpawn since it was spawned, so it is seen to stand
// before it takes off. Never in a fight: a nomad with a target fights on foot. A nomad
// that has been falling for fallDeploySeconds deploys to land, fight or not: that is saving itself, not
// taking off. Falling means off the NavMesh (its NavMeshAgent off, or no NavMesh at its feet) AND no
// ground under a ray cast from fallProbeLift above them: a nomad's root stands on the NavMesh, which can
// lie under a one-sided terrain or mesh surface (a dune crest) that a short ray from the feet never hits.
// Off the Sky City (an airborne site) a resident with nowhere to be now and then flies a SORTIE to a
// ground site (else a NavMesh point within sortieRoamBand, near enough to watch); it counts as one only
// once it is in the air (a won roll whose launch is refused stays
// pending for sortiePendingSeconds, retried every retryInterval). A sortie flier is never saved and is taken
// away once unseen after sortieLifetime (UnseenRemoval), so the city's refills cannot pile people up on
// the ground.
//
// On an owner's ORDER none of the above is asked: TakeOffInAir puts a nomad its group spawned seated in
// mid-air into a craft made around it (a war party's escort, an air patrol), and TakeOffNow launches one
// standing on a deck (the Sky City's swarm) with only the room-to-launch check. Either hands the craft back
// with no order of its own; the owner gives it one (NpcAviator.Escort / CruiseTo / LandAt / LandOnDeck).
//
// Persistence (D4): a world-scope flier is withheld from the world save for the flight (SaveScopeHold:
// its record is dropped, not just skipped) and given back on landing; a dead pilot is given back as a
// corpse once its loot is down (LootAwaitingGround); a group member is External throughout.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class NpcFlightModule : BehaviourModuleBase
    {
        [Header("Craft")]
        [Tooltip("What is deployed: NpcOrnithopter.prefab (NpcAviator + one-seat VesselSeats).")]
        [SerializeField] private GameObject craftPrefab;

        [Header("When to fly")]
        [Tooltip("Goals nearer than this, flat metres, are walked.")]
        [SerializeField, Min(10f)] private float minFlightDistance = 150f;

        [Tooltip("Seconds after stepping off (or a refused launch) before flying again.")]
        [SerializeField, Min(0f)] private float relaunchCooldown = 20f;

        [Tooltip("Seconds after the nomad is spawned (or switched back on) before it may take off: it stands a " +
                 "moment first, so a player who meets it sees it launch. A fall never waits.")]
        [SerializeField, Min(0f)] private float launchDelayAfterSpawn = 4f;

        [Tooltip("Seconds in the air before a nomad counts as falling and deploys to land — a hop or a " +
                 "step off a ledge is not a fall.")]
        [SerializeField, Min(0f)] private float fallDeploySeconds = 0.5f;

        [Tooltip("Seconds between launch attempts while there is no room to launch.")]
        [SerializeField, Min(0.1f)] private float retryInterval = 2f;

        [Header("Flight")]
        [Tooltip("Height above the ground to cruise at, metres.")]
        [SerializeField, Min(5f)] private float cruiseHeight = 60f;

        [Tooltip("How far from the touchdown point the nomad may be put onto the NavMesh, metres.")]
        [SerializeField, Min(0.5f)] private float landingSampleDistance = 6f;

        [Header("Launch")]
        [Tooltip("Ground nearer than this straight down counts as standing on it, metres.")]
        [SerializeField, Min(0.1f)] private float groundClearance = 0.6f;

        [Tooltip("The falling check's ground ray starts this far above the feet, metres — over a surface the " +
                 "root may stand just under, where the NavMesh lies below a one-sided terrain or mesh.")]
        [SerializeField, Min(0f)] private float fallProbeLift = 1f;

        [Tooltip("Feet within this of the NavMesh, with the NavMeshAgent on, are walking on it — never falling, metres.")]
        [SerializeField, Min(0.1f)] private float navMeshStandTolerance = 1f;

        [Tooltip("Craft spawned this far above the feet, metres — clear of the ground (or the city deck) it climbs away from.")]
        [SerializeField, Min(0f)] private float takeoffLift = 3f;

        [Tooltip("Height of the empty sky needed above a take-off from the ground, metres.")]
        [SerializeField, Min(1f)] private float minLaunchClearance = 6f;

        [Tooltip("Half-width of that empty sky, metres — about half the craft's 10 m span.")]
        [SerializeField, Min(1f)] private float takeoffClearRadius = 4f;

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private PhysicsGroundProbe takeoffProbe = new PhysicsGroundProbe();

        [Header("Sorties off an airborne site (the Sky City)")]
        [Tooltip("Where a sortie goes: resolved like a task (NpcTaskPlanner), airborne sites excluded.")]
        [SerializeField] private NpcTask sortieTask = new NpcTask();

        [Tooltip("Chance per check that a resident standing on an airborne site flies a sortie.")]
        [SerializeField, Range(0f, 1f)] private float sortieChance = 0.05f;

        [SerializeField, Min(1f)] private float sortieCheckInterval = 30f;

        [Tooltip("With no ground site of sortieTask's kind in reach, a sortie flies to a NavMesh point this far " +
                 "from the resident, flat metres (min, max): near enough that a player at the city watches it land.")]
        [SerializeField] private Vector2 sortieRoamBand = new Vector2(250f, 600f);

        [Tooltip("How far from a sortie roam point to look for the ground's NavMesh, metres — more than the " +
                 "moored city's height over the desert. The city's own deck is never picked.")]
        [SerializeField, Min(1f)] private float sortieGroundReach = 300f;

        [Tooltip("Seconds a won sortie roll whose launch was refused (no room) stays pending, retried every " +
                 "retryInterval, before it is given up.")]
        [SerializeField, Min(0f)] private float sortiePendingSeconds = 10f;

        [Tooltip("Seconds a landed sortie flier stays before it may be taken away unseen.")]
        [SerializeField, Min(0f)] private float sortieLifetime = 300f;

        [Tooltip("No player within this, flat metres, counts as unseen.")]
        [SerializeField, Min(1f)] private float sortieUnseenDistance = 350f;

        [Tooltip("How far to look for the airborne site a resident stands on, metres.")]
        [SerializeField, Min(1f)] private float airborneSiteSearch = 300f;

        private readonly SaveScopeHold saveHold = new SaveScopeHold();
        private readonly List<Vector3> players = new();
        private EntityBodyEquipment body;
        private NavMeshAgent navAgent;
        private float nextAttempt;
        private float launchReadyAt;
        private float airborneFor;
        private float nextSortieCheck;
        private PendingSortie? pendingSortie;
        private float sortiePendingUntil;
        private bool landedFromSortie;
        private float sortieRemaining;

        public NpcAviator Aviator { get; private set; }
        public bool InFlight => Aviator != null;
        /// <summary>Off the ground right now — a fall it may be about to deploy from. Read by simulation distance.</summary>
        public bool IsAirborne => airborneFor > 0f;
        public bool OnSortie { get; private set; }

        /// <summary>How high over the ground this nomad cruises, metres: where a group spawned in the air is made.</summary>
        public float CruiseHeight => cruiseHeight;

        public override string ModuleDescription =>
            "Flies a far AgentGoal on the worn wing pack (NpcOrnithopter); never in a fight; deploys when falling; " +
            "sorties off an airborne site. Override priority, claims only the deploy frame.";

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        // Lazy: EditMode tests run no Awake.
        private EntityBodyEquipment Body => body != null ? body : body = GetComponent<EntityBodyEquipment>();
        private NavMeshAgent NavAgent => navAgent != null ? navAgent : navAgent = GetComponent<NavMeshAgent>();

        private void OnEnable() => launchReadyAt = Time.time + launchDelayAfterSpawn;

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (landedFromSortie && TickSortieExpiry(deltaTime)) return null;
            if (InFlight || !WearsWingPack())
            {
                airborneFor = 0f;
                return null;
            }

            Vector3 feet = transform.position;
            bool airborne = IsOffTheGround(feet);
            airborneFor = airborne ? airborneFor + deltaTime : 0f;
            bool falling = airborneFor >= fallDeploySeconds;
            if (Time.time < nextAttempt) return null;
            if (!falling && Time.time < launchReadyAt) return null;
            if (!falling && context.Targeting != null && context.Targeting.Target != null) return null;   // D2: fights on foot

            AgentGoal goal = GoalOf(context.Goal);
            bool sortie = false;
            Vector3 destination = feet;
            float sortieArrive = 0f;
            string sortieSite = null;
            if (goal != null) destination = goal.Position;
            else if (!falling) sortie = TryPickSortie(context.Goal, out destination, out sortieArrive, out sortieSite);

            bool far = (goal != null || sortie) && MotorOrders.FlatDistance(feet, destination) >= minFlightDistance;
            if (!far && !falling) return null;

            Vector3 heading = Flat(destination - feet, transform.forward);
            if (!TryLaunch(feet, heading, airborne, destination))
            {
                nextAttempt = Time.time + retryInterval;
                if (sortie && pendingSortie == null)
                {
                    pendingSortie = new PendingSortie(destination, sortieArrive, sortieSite);
                    sortiePendingUntil = Time.time + sortiePendingSeconds;
                }
                return null;
            }

            pendingSortie = null;
            if (sortie)
            {
                context.Goal.Set(destination, sortieArrive, sortieTask.label, sortieSite);
                OnSortie = true;
            }
            return MoveIntent.Idle();
        }

        /// <summary>Off the NavMesh and over no ground: a fall, or the start of one.</summary>
        private bool IsOffTheGround(Vector3 feet)
        {
            NavMeshAgent agent = NavAgent;
            if (agent != null && agent.enabled &&
                NavMesh.SamplePosition(feet, out _, navMeshStandTolerance, agent.areaMask))
                return false;

            return !Physics.Raycast(feet + Vector3.up * fallProbeLift, Vector3.down, fallProbeLift + groundClearance,
                                    groundMask, QueryTriggerInteraction.Ignore);
        }

        private bool WearsWingPack()
        {
            if (craftPrefab == null || Body == null) return false;
            return WingPackItem.IsWingPack(Body.ItemIn(BodySlot.Torso));
        }

        /// <summary>Own goal, else — for a formation follower — its leader's (D5).</summary>
        private AgentGoal GoalOf(AgentGoal own)
        {
            if (own != null && own.HasGoal) return own;
            if (!TryGetComponent(out FormationModule formation) || formation.IsLeader) return null;

            FormationModule leader = FormationModule.LeaderOf(formation.FormationId);
            return leader != null && leader.TryGetComponent(out AgentGoal theirs) && theirs.HasGoal ? theirs : null;
        }

        private bool TryLaunch(Vector3 feet, Vector3 heading, bool airborne, Vector3 destination)
        {
            if (!TryTakeOff(feet, heading, airborne, out NpcAviator aviator)) return false;
            aviator.LandAt(destination);
            return true;
        }

        /// <summary>
        /// Take off from where it stands, on its owner's order and with no order for the craft yet: no launch
        /// delay, no flight distance, no fight check (the caller decided) — only room to launch. Server only.
        /// </summary>
        public bool TakeOffNow(Vector3 heading, out NpcAviator aviator)
        {
            aviator = null;
            if (InFlight || !WearsWingPack()) return false;
            return TryTakeOff(transform.position, Flat(heading, transform.forward), false, out aviator);
        }

        /// <summary>
        /// Already in the air — spawned seated at cruise height by its group: the craft is made around it where
        /// it is, flying, with no take-off at all. <paramref name="groundBelow"/> is the ground under it, which
        /// the craft cannot read off a pilot in mid-air. <paramref name="cruise"/> is the owner's cruise height, metres
        /// over the ground; null flies this nomad's own <see cref="CruiseHeight"/>. Server only.
        /// </summary>
        public bool TakeOffInAir(Vector3 heading, Vector3 groundBelow, out NpcAviator aviator, float? cruise = null)
        {
            aviator = null;
            if (InFlight || !WearsWingPack()) return false;

            Quaternion facing = Quaternion.LookRotation(Flat(heading, transform.forward), Vector3.up);
            return TryDeploy(CraftPositionFor(transform.position, facing, 0f), facing, groundBelow, cruise ?? cruiseHeight, out aviator);
        }

        /// <summary>Room to launch (unless already falling), then the craft, with this nomad boarded and no order given.</summary>
        private bool TryTakeOff(Vector3 feet, Vector3 heading, bool airborne, out NpcAviator aviator)
        {
            aviator = null;
            if (!airborne)
            {
                takeoffProbe.IgnoreHierarchy(transform);
                if (!takeoffProbe.IsClear(feet + Vector3.up * takeoffLift, takeoffClearRadius, minLaunchClearance)) return false;
            }

            Quaternion facing = Quaternion.LookRotation(heading, Vector3.up);
            return TryDeploy(CraftPositionFor(feet, facing, takeoffLift), facing, null, cruiseHeight, out aviator);
        }

        /// <summary>Where the craft's root goes so that its cradle is <paramref name="lift"/> above <paramref name="feet"/>.</summary>
        private Vector3 CraftPositionFor(Vector3 feet, Quaternion facing, float lift)
        {
            Vector3 seat = craftPrefab.GetComponent<VesselSeats>().SeatPose(0).position;
            return CraftDeployment.LaunchPosition(craftPrefab.transform, seat, feet, facing, lift);
        }

        /// <summary>Spawn the craft, hold this nomad out of the save and board it. Undone in full on any refusal.</summary>
        private bool TryDeploy(Vector3 at, Quaternion facing, Vector3? groundHint, float cruise, out NpcAviator aviator)
        {
            aviator = null;
            GameObject craft = GameServices.World.Spawn(craftPrefab, at, facing);
            if (craft == null) return false;
            if (!craft.TryGetComponent(out NpcAviator spawned))
            {
                Debug.LogError($"{name}: craftPrefab '{craftPrefab.name}' has no NpcAviator.", this);
                CraftDeployment.Retire(craft);
                return false;
            }

            saveHold.Hold(gameObject);
            if (!spawned.Board(gameObject, cruise, landingSampleDistance, groundHint))
            {
                saveHold.Release();
                CraftDeployment.Retire(craft);
                return false;
            }

            Aviator = spawned;
            spawned.PilotReleased += OnPilotReleased;
            aviator = spawned;
            return true;
        }

        private void OnPilotReleased(GameObject npc, bool alive)
        {
            if (Aviator != null)
            {
                Aviator.PilotReleased -= OnPilotReleased;
                // Landed short of a goal whose ground never streamed in: flying back would only circle
                // it again, so the goal is dropped and whatever gives goals hands out the next one.
                if (Aviator.GaveUp && TryGetComponent(out AgentGoal own)) own.Clear();
            }
            Aviator = null;
            nextAttempt = Time.time + relaunchCooldown;

            // A sortie flier stays out of the save for good and is taken away unseen; anyone else is
            // given back to whatever saved them before — including a corpse, so its Remains count.
            if (OnSortie && alive)
            {
                landedFromSortie = true;
                sortieRemaining = sortieLifetime;
                return;
            }

            // A corpse only once its loot is down: saved while the pack still waits on it, the reload
            // (IsRestoring) never drops it. `npc` may be destroyed already — never dereferenced here.
            if (alive) saveHold.Release();
            else LootAwaitingGround.WhenDropped(npc, saveHold.Release);
        }

        /// <summary>
        /// A sortie for a resident of an airborne site with nowhere else to be: where to, never yet set as
        /// its goal — that waits for the launch, so a sortie that cannot take off sends nobody walking off
        /// the edge of the city. A won roll whose launch was refused stays pending for sortiePendingSeconds
        /// and is handed back here until then, so a crowded deck delays a sortie rather than wasting it.
        /// </summary>
        private bool TryPickSortie(AgentGoal own, out Vector3 destination, out float arriveRadius, out string siteId)
        {
            destination = transform.position;
            arriveRadius = 0f;
            siteId = null;
            if (OnSortie || own == null || own.HasGoal)
            {
                pendingSortie = null;
                return false;
            }

            if (pendingSortie is PendingSortie pending)
            {
                if (Time.time <= sortiePendingUntil)
                {
                    destination = pending.Destination;
                    arriveRadius = pending.ArriveRadius;
                    siteId = pending.SiteId;
                    return true;
                }
                pendingSortie = null;
            }

            if (Time.time < nextSortieCheck) return false;

            nextSortieCheck = Time.time + sortieCheckInterval;
            return TryFindAirborneHome(out WorldSite home) && Random.value <= sortieChance &&
                   TryPickSortieDestination(home, out destination, out arriveRadius, out siteId);
        }

        /// <summary>
        /// A ground site of <see cref="sortieTask"/>'s kind within its search radius, else a NavMesh point within
        /// <see cref="sortieRoamBand"/> of the resident — off <paramref name="home"/>'s own deck.
        /// </summary>
        private bool TryPickSortieDestination(WorldSite home, out Vector3 destination, out float arriveRadius, out string siteId)
        {
            Vector3 origin = transform.position;
            if (NpcTaskPlanner.TryResolveSite(sortieTask, origin, null, out destination, out arriveRadius, out siteId, out _))
                return true;

            return NpcTaskPlanner.TryRoamPoint(origin, sortieRoamBand.x, sortieRoamBand.y, sortieGroundReach,
                                               point => home.FlatDistanceTo(point) > home.Radius, out destination);
        }

        private bool TryFindAirborneHome(out WorldSite site) =>
            WorldSiteRegistry.TryFindNearest(SiteKind.Home, transform.position, airborneSiteSearch, out site,
                                             includeAirborne: true) &&
            site.Airborne && site.FlatDistanceTo(transform.position) <= site.Radius;

        /// <returns>True when the flier was taken away.</returns>
        private bool TickSortieExpiry(float deltaTime)
        {
            sortieRemaining -= deltaTime;
            if (!UnseenRemoval.IsDue(sortieRemaining, transform.position, sortieUnseenDistance, players)) return false;

            NpcSpawn.Remove(gameObject);
            return true;
        }

        private static Vector3 Flat(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > MinHeadingSqr) return direction.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > MinHeadingSqr ? fallback.normalized : Vector3.forward;
        }

        /// <summary>Where a won sortie roll goes, kept while its launch waits for room.</summary>
        private readonly struct PendingSortie
        {
            public readonly Vector3 Destination;
            public readonly float ArriveRadius;
            public readonly string SiteId;

            public PendingSortie(Vector3 destination, float arriveRadius, string siteId)
            {
                Destination = destination;
                ArriveRadius = arriveRadius;
                SiteId = siteId;
            }
        }

        // Below this a flat vector names no heading.
        private const float MinHeadingSqr = 1e-4f;

        private void OnDestroy()
        {
            if (Aviator != null) Aviator.PilotReleased -= OnPilotReleased;
        }
    }
}
