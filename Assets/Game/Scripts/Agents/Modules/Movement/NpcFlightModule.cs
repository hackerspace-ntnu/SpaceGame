// Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs
// Flies a Sky nomad on its wing pack. When where it is going is too far to walk — its own goal, or for a
// formation follower its leader's (D5: each pilot flies alone to the shared goal) — it deploys the
// same craft the player flies (NpcOrnithopter), rides it there and steps off; GoalTravelModule walks the
// last metres. The craft does the flying (NpcAviator); this module only decides and launches, and stops
// ticking by itself once seated (AgentController.RidesAsPassenger runs side-effect modules only).
//
// When (D2): a goal further than minFlightDistance, a wing pack worn, and room to launch — already in the
// air, or minLaunchClearance of empty sky above it (the NPC craft just climbs away: NpcFlightPlan, no
// energy model, so no ledge is needed). Never in a fight: a nomad with a target fights on foot. A nomad
// that has been falling for fallDeploySeconds deploys to land, fight or not: that is saving itself, not
// taking off.
// Off the Sky City (an airborne site) a resident with nowhere to be now and then flies a SORTIE to a
// ground site; it counts as one only once it is in the air. A sortie flier is never saved and is taken
// away once unseen after sortieLifetime (UnseenRemoval), so the city's refills cannot pile people up on
// the ground.
//
// Persistence (D4): a world-scope flier is withheld from the world save for the flight (SaveScopeHold:
// its record is dropped, not just skipped) and given back on landing; a dead pilot is given back as a
// corpse once its loot is down (LootAwaitingGround); a group member is External throughout.
using System.Collections.Generic;
using UnityEngine;
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
        [SerializeField, Min(10f)] private float minFlightDistance = 250f;

        [Tooltip("Seconds after stepping off (or a refused launch) before flying again.")]
        [SerializeField, Min(0f)] private float relaunchCooldown = 20f;

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

        [Tooltip("Craft spawned this far above the feet, metres — clear of the ground (or the city deck) it climbs away from.")]
        [SerializeField, Min(0f)] private float takeoffLift = 3f;

        [Tooltip("Height of the empty sky needed above a take-off from the ground, metres.")]
        [SerializeField, Min(1f)] private float minLaunchClearance = 12f;

        [Tooltip("Half-width of that empty sky, metres — about half the craft's 10 m span.")]
        [SerializeField, Min(1f)] private float takeoffClearRadius = 6f;

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private PhysicsGroundProbe takeoffProbe = new PhysicsGroundProbe();

        [Header("Sorties off an airborne site (the Sky City)")]
        [Tooltip("Where a sortie goes: resolved like a task (NpcTaskPlanner), airborne sites excluded.")]
        [SerializeField] private NpcTask sortieTask = new NpcTask();

        [Tooltip("Chance per check that a resident standing on an airborne site flies a sortie.")]
        [SerializeField, Range(0f, 1f)] private float sortieChance = 0.05f;

        [SerializeField, Min(1f)] private float sortieCheckInterval = 30f;

        [Tooltip("Seconds a landed sortie flier stays before it may be taken away unseen.")]
        [SerializeField, Min(0f)] private float sortieLifetime = 300f;

        [Tooltip("No player within this, flat metres, counts as unseen.")]
        [SerializeField, Min(1f)] private float sortieUnseenDistance = 350f;

        [Tooltip("How far to look for the airborne site a resident stands on, metres.")]
        [SerializeField, Min(1f)] private float airborneSiteSearch = 300f;

        private readonly SaveScopeHold saveHold = new SaveScopeHold();
        private readonly List<Vector3> players = new();
        private EntityBodyEquipment body;
        private float nextAttempt;
        private float airborneFor;
        private float nextSortieCheck;
        private bool landedFromSortie;
        private float sortieRemaining;

        public NpcAviator Aviator { get; private set; }
        public bool InFlight => Aviator != null;
        public bool OnSortie { get; private set; }

        public override string ModuleDescription =>
            "Flies a far AgentGoal on the worn wing pack (NpcOrnithopter); never in a fight; deploys when falling; " +
            "sorties off an airborne site. Override priority, claims only the deploy frame.";

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        // Lazy: EditMode tests run no Awake.
        private EntityBodyEquipment Body => body != null ? body : body = GetComponent<EntityBodyEquipment>();

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (landedFromSortie && TickSortieExpiry(deltaTime)) return null;
            if (InFlight || !WearsWingPack())
            {
                airborneFor = 0f;
                return null;
            }

            Vector3 feet = transform.position;
            bool airborne = FlightLaunch.IsAirborne(feet, groundClearance, groundMask);
            airborneFor = airborne ? airborneFor + deltaTime : 0f;
            bool falling = airborneFor >= fallDeploySeconds;
            if (Time.time < nextAttempt) return null;
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
                return null;
            }

            if (sortie)
            {
                context.Goal.Set(destination, sortieArrive, sortieTask.label, sortieSite);
                OnSortie = true;
            }
            return MoveIntent.Idle();
        }

        private bool WearsWingPack()
        {
            if (craftPrefab == null || Body == null) return false;
            InventoryItem item = Body.ItemIn(BodySlot.Torso);
            return item != null && item.itemPrefab != null && item.itemPrefab.GetComponent<WingPackItem>() != null;
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
            if (!airborne)
            {
                takeoffProbe.IgnoreHierarchy(transform);
                if (!takeoffProbe.IsClear(feet + Vector3.up * takeoffLift, takeoffClearRadius, minLaunchClearance)) return false;
            }

            Quaternion facing = Quaternion.LookRotation(heading, Vector3.up);
            Vector3 seat = craftPrefab.GetComponent<VesselSeats>().SeatPose(0).position;
            Vector3 at = CraftDeployment.LaunchPosition(craftPrefab.transform, seat, feet, facing, takeoffLift);

            GameObject craft = GameServices.World.Spawn(craftPrefab, at, facing);
            if (craft == null) return false;
            if (!craft.TryGetComponent(out NpcAviator aviator))
            {
                Debug.LogError($"{name}: craftPrefab '{craftPrefab.name}' has no NpcAviator.", this);
                CraftDeployment.Retire(craft);
                return false;
            }

            saveHold.Hold(gameObject);
            if (!aviator.Fly(gameObject, destination, cruiseHeight, landingSampleDistance))
            {
                saveHold.Release();
                CraftDeployment.Retire(craft);
                return false;
            }

            Aviator = aviator;
            aviator.PilotReleased += OnPilotReleased;
            return true;
        }

        private void OnPilotReleased(GameObject npc, bool alive)
        {
            if (Aviator != null) Aviator.PilotReleased -= OnPilotReleased;
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
        /// the edge of the city.
        /// </summary>
        private bool TryPickSortie(AgentGoal own, out Vector3 destination, out float arriveRadius, out string siteId)
        {
            destination = transform.position;
            arriveRadius = 0f;
            siteId = null;
            if (OnSortie || own == null || own.HasGoal || Time.time < nextSortieCheck) return false;

            nextSortieCheck = Time.time + sortieCheckInterval;
            return OnAirborneSite() && Random.value <= sortieChance &&
                   NpcTaskPlanner.ResolveDestination(sortieTask, transform.position, null,
                                                     out destination, out arriveRadius, out siteId, out _);
        }

        private bool OnAirborneSite() =>
            WorldSiteRegistry.TryFindNearest(SiteKind.Home, transform.position, airborneSiteSearch, out WorldSite site,
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

        // Below this a flat vector names no heading.
        private const float MinHeadingSqr = 1e-4f;

        private void OnDestroy()
        {
            if (Aviator != null) Aviator.PilotReleased -= OnPilotReleased;
        }
    }
}
