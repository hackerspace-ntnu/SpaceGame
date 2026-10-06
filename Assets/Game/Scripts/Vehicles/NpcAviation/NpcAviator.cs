// Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs
// Flies one NPC on the craft the wing pack deploys and puts it down where it was going — the NPC-side
// counterpart of WingPackItem's flight, on NpcOrnithopter.prefab. NOT the player's energy model: the
// craft is flown by FlyingRigidbodyMotor, steered by this module through MoveIntents taken from the
// pure NpcFlightPlan (user decision 2026-10-06: NPC flight is simple; the player's flight is untouched).
//
// Seating is VesselSeats' (one marker, SEAT_Cradle): feet off, brain on, so a pilot shoots from the
// cradle (D6), and a dead pilot is dropped straight down. LandingSiteFinder picks reachable ground near
// the goal once that ground has streamed in; until then (or if there is none) the craft lands at the
// goal itself, projected onto the ground. This class owns the endings:
//   • touchdown (the plan says down) or a crash (flying into the world past the launch grace): price
//     the arrival, stand the pilot on the NavMesh, retire the craft;
//   • the pilot dies: the plan spirals the craft in as a wreck (D8), retired when it reaches the ground
//     (or, over no ground at all, once it has fallen noGroundDepth);
//   • the pilot is taken away underneath it (its group folded): retire the craft at once;
//   • the craft is destroyed from outside with the pilot aboard: the release is still announced.
// Server-decided throughout (AgentController only ticks modules where it simulates); every peer sees the
// spawn, the parenting and the despawn. Never saved: SaveablePolicy.NeedsSaving refuses any root
// carrying this (D4).
using System;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VesselSeats))]
    [RequireComponent(typeof(FlyingRigidbodyMotor))]
    public class NpcAviator : BehaviourModuleBase, IAirborneCarrier
    {
        private const int PilotSeat = 0;

        // MoveIntent.MoveTo's own default: the plan already slows the craft for its touchdown.
        private const float ArriveStopDistance = 0.2f;

        [Header("Flight")]
        [SerializeField] private NpcFlightSettings flight = new NpcFlightSettings();

        [Tooltip("Seconds after launch during which a collision is ignored — the deck or ledge just left.")]
        [SerializeField, Min(0f)] private float launchGraceSeconds = 0.5f;

        [Tooltip("With no ground found below the craft, the ground counts as this far down, metres. A wreck " +
                 "that has fallen this far below where its pilot died, still over no ground, is retired.")]
        [SerializeField, Min(1f)] private float noGroundDepth = 300f;

        [Header("Landing")]
        [Tooltip("Half-width of the ground the craft sets down on, metres.")]
        [SerializeField, Min(0.5f)] private float footprintRadius = 6f;

        [Tooltip("Where around the goal a landing site may be: tighter rings than a vessel's, because the " +
                 "spec wants the pilot down within about 15 m of where it was going.")]
        [SerializeField] private LandingSettings landing = CraftLanding;

        [SerializeField] private PhysicsGroundProbe probe = new PhysicsGroundProbe();

        [Tooltip("Seconds between looks for a landing site while none is chosen.")]
        [SerializeField, Min(0.1f)] private float siteCheckInterval = 1f;

        [Header("Crash")]
        [Tooltip("What an arrival costs the pilot, by closing speed — the player's craft's own curve.")]
        [SerializeField] private OrnithopterCrashConfig crash = new OrnithopterCrashConfig();

        [Tooltip("Layers that are the world: only a contact on these, with something that has no body or a " +
                 "kinematic one (terrain, rock, a city hull or a crewed transport), ends a flight. Never a " +
                 "person or creature, and never a dynamic body (projectiles, ragdolls, other craft). The " +
                 "builder writes vision's solid geometry layers.")]
        [SerializeField] private LayerMask crashMask = ~0;

        public static LandingSettings CraftLanding => new LandingSettings
        {
            ringMin = 3f,
            ringMax = 14f,
            candidatesPerRing = 12,
            rings = 3,
            maxSlopeDegrees = 18f,
            maxHeightSpread = 2f,
            clearanceHeight = 15f,
            hoverHeight = 0f,
            navMeshReach = 4f,
            farSidePenalty = 10f,
        };

        private VesselSeats seats;
        private FlyingRigidbodyMotor motor;
        private NpcFlightPlan plan;
        private WorldStreamer streamer;
        private HealthComponent pilotHealth;
        private GameObject flyingPilot;
        private Transform releasedBody;
        private Vector3 goal;
        private Vector3 landingPoint;
        private Vector3 lastVelocity;
        private float cruiseHeight;
        private float landingReach;
        private float launchedAt;
        private float nextSiteCheck;
        private float wreckedAtHeight;
        private bool flying;
        private bool unseatFailureReported;

        public event Action<GameObject, bool> PilotReleased;

        // Lazy, not cached in Awake: EditMode tests run no Awake.
        private VesselSeats Seats => seats != null ? seats : seats = GetComponent<VesselSeats>();
        private FlyingRigidbodyMotor Motor => motor != null ? motor : motor = GetComponent<FlyingRigidbodyMotor>();
        private NpcFlightPlan Plan => plan ??= new NpcFlightPlan(flight);

        public GameObject Pilot => Seats.OccupantAt(PilotSeat);
        public bool HasSite { get; private set; }
        public bool Wrecked { get; private set; }
        public Vector3 Goal => goal;
        public Vector3 LandingPoint => landingPoint;
        public NpcFlightPhase Phase => Plan.Phase;

        public override string ModuleDescription =>
            "Flies the seated NPC pilot to its goal on FlyingRigidbodyMotor (NpcFlightPlan: climb, cruise, " +
            "approach, spiral, flare), lands it on NavMesh and retires the craft; spirals in as a wreck if the pilot dies.";

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        /// <summary>Seat <paramref name="pilot"/> and fly it to <paramref name="destination"/>. Server only.</summary>
        /// <param name="landingSampleDistance">How far from where the craft sets down the pilot may be stood on NavMesh.</param>
        public bool Fly(GameObject pilot, Vector3 destination, float cruiseHeight, float landingSampleDistance)
        {
            if (pilot == null || flying || !IsAlive(pilot) || !Network.Simulates(this)) return false;
            if (!Seats.Seat(PilotSeat, pilot)) return false;

            probe.IgnoreHierarchy(transform);
            goal = destination;
            landingPoint = probe.TryGround(destination, out Vector3 ground, out _) ? ground : destination;
            this.cruiseHeight = cruiseHeight;
            landingReach = landingSampleDistance;
            HasSite = false;
            Wrecked = false;
            flying = true;
            flyingPilot = pilot;
            releasedBody = null;
            unseatFailureReported = false;
            launchedAt = Time.time;
            nextSiteCheck = 0f;
            streamer = FindFirstObjectByType<WorldStreamer>();

            WatchPilot(pilot);
            Plan.Begin();
            return true;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!flying) return null;

            // Taken away underneath us — its group folded and NpcSpawn.Remove despawned it. Nothing is
            // aboard and nobody is watching (a fold happens beyond despawnRadius).
            if (!Wrecked && Pilot == null)
            {
                Retire();
                return MoveIntent.Idle();
            }

            if (!Wrecked && !HasSite && Time.time >= nextSiteCheck)
            {
                nextSiteCheck = Time.time + siteCheckInterval;
                TryChooseSite();
            }

            Vector3 position = transform.position;
            lastVelocity = Motor.Velocity;
            bool grounded = probe.TryGroundBelow(position, out Vector3 below);

            // A wreck over nothing — past the map's edge, over ground that never streamed in — would fall
            // forever, a server object nobody retires.
            if (Wrecked && !grounded && position.y < wreckedAtHeight - noGroundDepth)
            {
                Retire();
                return MoveIntent.Idle();
            }

            float groundBelow = grounded ? below.y : position.y - noGroundDepth;
            NpcFlightStep step = Plan.Step(position, groundBelow, landingPoint, cruiseHeight);

            // Landed: touched down this step, or a touchdown that could not set its pilot down last tick
            // and is retried.
            if (Plan.Phase == NpcFlightPhase.Landed)
            {
                // Down over no ground is down at the landing point itself: the pilot stands under the
                // cradle, never at the stand-in depth.
                Vector3 ground = grounded
                    ? new Vector3(position.x, below.y, position.z)
                    : position - Vector3.up * flight.TouchdownHeight;
                Touchdown(ground, Mathf.Max(0f, -lastVelocity.y));
                return MoveIntent.Idle();
            }

            return MoveIntent.MoveTo(step.Target, ArriveStopDistance, step.Speed);
        }

        private void TryChooseSite()
        {
            // The rule VesselPilot learned the hard way: ground that has not streamed in has no site to
            // find, and "no site" there is not an answer.
            if (streamer != null && !streamer.IsGroundLoadedAround(goal, landing.ringMax + footprintRadius)) return;

            DropSite? site = LandingSiteFinder.Find(goal, transform.position, footprintRadius, landing, probe);
            if (!site.HasValue || site.Value.Mode != DropMode.Land) return;

            HasSite = true;
            landingPoint = site.Value.Point;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;
            ContactPoint contact = collision.GetContact(0);
            OnContact(collision.collider, contact.point, contact.normal);
        }

        /// <summary>Something touched the hull: only the world ends a flight.</summary>
        private void OnContact(Collider other, Vector3 point, Vector3 normal)
        {
            if (IsWorld(other)) Crash(point, normal);
        }

        private bool IsWorld(Collider other)
        {
            if ((crashMask.value & (1 << other.gameObject.layer)) == 0) return false;

            // The pilot's own body, let go inside the cradle when it died: VesselSeats hands its collisions
            // with the hull back as it drops, and the wreck must not crash into its own corpse.
            if (releasedBody != null && other.transform.IsChildOf(releasedBody)) return false;

            Rigidbody body = other.attachedRigidbody;
            return (body == null || body.isKinematic) && !IsCreature(other);
        }

        /// <summary>
        /// A person or a creature: something with health that is not a crewed hull. A Sky nomad is a
        /// Default-layer collider on a kinematic root body — rock, by layer and body alone — and a
        /// group-mate brushing the wings in a crowded launch must not end the flight. Not AgentController:
        /// transports and walking houses carry one too, and those stay crashable.
        /// </summary>
        private static bool IsCreature(Collider other)
        {
            HealthComponent health = other.GetComponentInParent<HealthComponent>();
            return health != null && health.GetComponent<VesselSeats>() == null;
        }

        /// <summary>Flew into something — a cliff, a rock, a city hull. Priced on the velocity from before the contact.</summary>
        private void Crash(Vector3 point, Vector3 normal)
        {
            if (!flying || !Network.Simulates(this) || Time.time - launchedAt < launchGraceSeconds) return;

            Vector3 ground = probe.TryGround(point, out Vector3 hit, out _) ? hit : point;
            Touchdown(ground, OrnithopterCrash.ClosingSpeed(lastVelocity, normal));
        }

        private void Touchdown(Vector3 ground, float closingSpeed)
        {
            GameObject pilot = Wrecked ? null : Pilot;
            if (pilot != null)
            {
                // Priced first, the hit landing last with the pilot standing — WingPackItem.HandleLanded's
                // order, for the same reason: a fatal arrival leaves the body at the craft.
                int damage = OrnithopterCrash.ImpactDamage(closingSpeed, crash);
                if (Seats.Unseat(PilotSeat, ground, landingReach) == null)
                {
                    // Still in the cradle: neither released nor retired with the craft. Tick retries while
                    // the plan reads Landed, so this is said once per landing, not once per tick.
                    if (!unseatFailureReported)
                        Debug.LogError($"[NpcAviator] '{name}' could not set its pilot '{pilot.name}' down " +
                                       "(the craft is inactive or not this machine's); keeping it aboard.", this);
                    unseatFailureReported = true;
                    return;
                }

                UnwatchPilot();
                flyingPilot = null;
                if (damage > 0) NetDamage.Apply(pilot, damage);
                PilotReleased?.Invoke(pilot, IsAlive(pilot));
            }

            Retire();
        }

        private void WatchPilot(GameObject pilot)
        {
            pilotHealth = pilot.GetComponent<HealthComponent>();
            if (pilotHealth != null) pilotHealth.OnDeath += OnPilotDied;
        }

        private void UnwatchPilot()
        {
            if (pilotHealth != null) pilotHealth.OnDeath -= OnPilotDied;
            pilotHealth = null;
        }

        /// <summary>
        /// Killed in the air (D8). VesselSeats has already let the body go (it subscribed first, at Seat),
        /// so it falls from where it died; EntityLootTable,
        /// told by IAirborneCarrier, drops the pack beside it once it is down. The craft spirals in close by.
        /// </summary>
        private void OnPilotDied()
        {
            if (pilotHealth == null || pilotHealth.IsRestoring || !Network.Simulates(this)) return;

            GameObject corpse = pilotHealth.gameObject;
            UnwatchPilot();
            flyingPilot = null;
            releasedBody = corpse.transform;
            Wrecked = true;
            wreckedAtHeight = transform.position.y;
            Plan.Wreck(transform.position);
            PilotReleased?.Invoke(corpse, false);
        }

        private void Retire()
        {
            flying = false;
            flyingPilot = null;
            CraftDeployment.Retire(gameObject);
        }

        private static bool IsAlive(GameObject npc) =>
            npc != null && (!npc.TryGetComponent(out HealthComponent health) || health.Alive);

        /// <summary>
        /// Destroyed in flight with its pilot aboard — despawned from outside, a scene unload, the server
        /// shutting down. Whoever waits on the release still hears it; VesselSeats.OnDestroy lets the body go.
        /// Cached, not read off the seats: VesselSeats may have emptied them in its own OnDestroy first.
        /// </summary>
        private void OnDestroy()
        {
            GameObject pilot = flying ? flyingPilot : null;
            flying = false;
            flyingPilot = null;
            UnwatchPilot();
            if (!ReferenceEquals(pilot, null)) PilotReleased?.Invoke(pilot, IsAlive(pilot));
        }
    }
}
