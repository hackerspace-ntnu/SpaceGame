// Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs
// Flies one NPC on the craft the wing pack deploys and puts it down where it was going — the NPC-side
// counterpart of WingPackItem's flight, on NpcOrnithopter.prefab. NOT the player's energy model: the
// craft is flown by FlyingRigidbodyMotor, steered by this module through MoveIntents taken from the
// pure NpcFlightPlan (user decision 2026-10-06: NPC flight is simple; the player's flight is untouched).
//
// Seating is VesselSeats' (one marker, SEAT_Cradle): feet off, brain on, so a pilot shoots from the
// cradle (D6), and a dead pilot is dropped straight down. LandingSiteFinder picks reachable ground near
// the goal once that ground has streamed in; until then (or if there is none) the craft lands at the
// goal itself, projected onto the ground. With no ground under the goal at all (not streamed in) it
// never lands: it circles the goal at cruise until the ground appears, and after groundWaitSeconds
// of circling turns back to the last ground it flew over and lands there.
// Orders (FlightOrder): Fly = Board (seat, no order: straight on) + LandAt (the above). CruiseTo flies a
// point at cruise and circles it, never landing; Escort keeps a FlightStation on any Transform — a leader's
// craft, an airship, the Sky City (EscortSteering). Whoever owns the flight gives the next order; neither
// cruise nor escort ever lands or gives up on its own. This class owns the endings:
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
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.Vehicles
{
    /// <summary>What an NPC craft is doing with its pilot: going down somewhere, cruising to a point, keeping a station.</summary>
    public enum FlightOrder { Land, Cruise, Escort }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(VesselSeats))]
    [RequireComponent(typeof(FlyingRigidbodyMotor))]
    public class NpcAviator : BehaviourModuleBase, IAirborneCarrier, IStowsTorsoGear
    {
        private const int PilotSeat = 0;

        // MoveIntent.MoveTo's own default: the plan already slows the craft for its touchdown.
        private const float ArriveStopDistance = 0.2f;

        // A craft boarded with no order yet cruises at a point this far straight ahead, metres.
        private const float StraightOnDistance = 1000f;

        // Floor on the anchor-velocity smoothing time, seconds: never a division by zero.
        private const float MinSmoothing = 0.01f;

        [Header("Flight")]
        [SerializeField] private NpcFlightSettings flight = new NpcFlightSettings();

        [Tooltip("Seconds after launch during which a collision is ignored — the deck or ledge just left.")]
        [SerializeField, Min(0f)] private float launchGraceSeconds = 0.5f;

        [Tooltip("With no ground found below the craft, the ground counts as this far down, metres. A wreck " +
                 "that has fallen this far below where its pilot died, still over no ground, is retired.")]
        [SerializeField, Min(1f)] private float noGroundDepth = 300f;

        [Header("Escort")]
        [Tooltip("How a craft keeps a station on its anchor (Escort): catch-up, lead, separation, clearance, drift.")]
        [SerializeField] private EscortSettings escort = new EscortSettings();

        [Header("Landing")]
        [Tooltip("Half-width of the ground the craft sets down on, metres.")]
        [SerializeField, Min(0.5f)] private float footprintRadius = 6f;

        [Tooltip("Where around the goal a landing site may be: tighter rings than a vessel's, because the " +
                 "spec wants the pilot down within about 15 m of where it was going.")]
        [SerializeField] private LandingSettings landing = CraftLanding;

        [SerializeField] private PhysicsGroundProbe probe = new PhysicsGroundProbe();

        [Tooltip("Seconds between looks for a landing site while none is chosen.")]
        [SerializeField, Min(0.1f)] private float siteCheckInterval = 1f;

        [Tooltip("Seconds the craft circles a goal with no ground under it (not streamed in) before it gives " +
                 "the goal up and lands on the last ground it flew over instead.")]
        [SerializeField, Min(1f)] private float groundWaitSeconds = 60f;

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
        private Vector3 lastGround;
        private Vector3 lastVelocity;
        private float cruiseHeight;
        private float landingReach;
        private float launchedAt;
        private float nextSiteCheck;
        private float wreckedAtHeight;
        private float heldFor;
        private bool landingKnown;
        private bool onDeck;
        private bool flying;
        private bool unseatFailureReported;
        private Vector3 cruisePoint;
        private float cruiseSpeed;
        private FlightStation station;
        private Vector3 anchorLastPosition;
        private Vector3 anchorVelocity;
        private bool anchorSeen;

        // Every craft flying an escort order, for separation. Server-side, like everything here.
        private static readonly List<NpcAviator> Escorting = new();
        private static readonly List<Vector3> Neighbours = new();

        public event Action<GameObject, bool> PilotReleased;

        /// <summary>What the craft is doing: landing somewhere, cruising to a point, or keeping a station.</summary>
        public FlightOrder Order { get; private set; }

        /// <summary>Cruising, and within the plan's look-ahead of its point: circling it, waiting for the next.</summary>
        public bool ReachedCruisePoint => flying && !Wrecked && Order == FlightOrder.Cruise && Plan.Holding;

        /// <summary>Escorting an anchor that is gone: circling where it was last seen until re-anchored or landed.</summary>
        public bool AnchorLost { get; private set; }

        /// <summary>The station this craft keeps while <see cref="Order"/> is Escort.</summary>
        public FlightStation Station => station;

        /// <summary>Flying with a living pilot aboard (neither retired nor a wreck).</summary>
        public bool Aloft => flying && !Wrecked;

        /// <summary>Metres over the ground the craft cruises at, as boarded.</summary>
        public float CruiseHeight => cruiseHeight;

        // Lazy, not cached in Awake: EditMode tests run no Awake.
        private VesselSeats Seats => seats != null ? seats : seats = GetComponent<VesselSeats>();
        private FlyingRigidbodyMotor Motor => motor != null ? motor : motor = GetComponent<FlyingRigidbodyMotor>();
        private NpcFlightPlan Plan => plan ??= new NpcFlightPlan(flight);

        public GameObject Pilot => Seats.OccupantAt(PilotSeat);
        public bool HasSite { get; private set; }
        public bool Wrecked { get; private set; }
        /// <summary>This flight gave its goal up (no ground ever streamed in under it) and landed short.</summary>
        public bool GaveUp { get; private set; }
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
            if (!Board(pilot, cruiseHeight, landingSampleDistance)) return false;
            LandAt(destination);
            return true;
        }

        /// <summary>
        /// Seat <paramref name="pilot"/> with no order yet: until it gets one the craft cruises straight on.
        /// Server only. <paramref name="groundHint"/> is the ground under a craft made in mid-air, where the
        /// pilot's own position is no ground at all.
        /// </summary>
        public bool Board(GameObject pilot, float cruiseHeight, float landingSampleDistance, Vector3? groundHint = null)
        {
            if (pilot == null || flying || !IsAlive(pilot) || !Network.Simulates(this)) return false;
            Vector3 takeOff = groundHint ?? pilot.transform.position;
            if (!Seats.Seat(PilotSeat, pilot)) return false;

            probe.IgnoreHierarchy(transform);
            lastGround = probe.TryGroundBelow(transform.position, out Vector3 below) ? below : takeOff;
            this.cruiseHeight = cruiseHeight;
            landingReach = landingSampleDistance;
            Wrecked = false;
            flying = true;
            flyingPilot = pilot;
            releasedBody = null;
            unseatFailureReported = false;
            launchedAt = Time.time;
            streamer = FindFirstObjectByType<WorldStreamer>();

            WatchPilot(pilot);
            CruiseTo(transform.position + EscortSteering.YawForward(transform.forward) * StraightOnDistance, 1f);
            return true;
        }

        /// <summary>Go down at <paramref name="destination"/>: a landing site near it once its ground has streamed in.</summary>
        public void LandAt(Vector3 destination)
        {
            SetOrder(FlightOrder.Land);
            goal = destination;
            landingPoint = destination;
            landingKnown = false;
            GaveUp = false;
            HasSite = false;
            heldFor = 0f;
            nextSiteCheck = 0f;
            ProbeLanding();
            Plan.Begin();
        }

        /// <summary>
        /// Go down exactly on <paramref name="deckPoint"/>, a surface the ground probe does not count as ground — a
        /// hull's deck (anything with a body is never ground to it). No site is looked for: the owner chose the spot,
        /// and the deck's height stands in for the ground the whole way in. Approach it from at least
        /// <see cref="NpcFlightSettings.ApproachSlope"/> above, or the plan sinks toward cruise over the deck first.
        /// </summary>
        public void LandOnDeck(Vector3 deckPoint)
        {
            SetOrder(FlightOrder.Land);
            onDeck = true;
            goal = deckPoint;
            landingPoint = deckPoint;
            landingKnown = true;
            GaveUp = false;
            HasSite = true;
            heldFor = 0f;
            Plan.Begin();
        }

        /// <summary>
        /// Fly to <paramref name="point"/> at cruise height and circle it there, at <paramref name="speedFraction"/>
        /// of top speed. Never lands and never gives up: whoever ordered it gives the next order.
        /// </summary>
        public void CruiseTo(Vector3 point, float speedFraction)
        {
            SetOrder(FlightOrder.Cruise);
            cruisePoint = point;
            cruiseSpeed = Mathf.Clamp01(speedFraction);
            Plan.Begin();
        }

        /// <summary>Keep <paramref name="assigned"/> on its anchor until told otherwise. Never lands by itself.</summary>
        public void Escort(in FlightStation assigned)
        {
            SetOrder(FlightOrder.Escort);
            station = assigned;
            AnchorLost = assigned.Anchor == null;
            anchorLastPosition = assigned.Anchor != null ? assigned.Anchor.position : transform.position;
            anchorVelocity = Vector3.zero;
            anchorSeen = false;
            Plan.Begin();
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

            if (!Wrecked && Order == FlightOrder.Land && !HasSite && Time.time >= nextSiteCheck)
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

            if (grounded) lastGround = below;

            // Over ground that has not streamed in, a living flight holds cruise over the last ground it
            // saw; only a wreck sinks toward the stand-in depth (and is retired past it).
            float groundBelow = !Wrecked && Order == FlightOrder.Land && onDeck ? landingPoint.y
                              : grounded ? below.y : Wrecked ? position.y - noGroundDepth : lastGround.y;

            if (!Wrecked && Order == FlightOrder.Cruise)
                return Cruise(position, groundBelow, cruisePoint, cruiseSpeed);
            if (!Wrecked && Order == FlightOrder.Escort)
                return KeepStation(position, groundBelow, deltaTime);

            if (!Wrecked && !landingKnown) ProbeLanding();
            NpcFlightStep step = Wrecked || landingKnown
                ? Plan.Step(position, groundBelow, landingPoint, cruiseHeight)
                : Plan.Hold(position, groundBelow, goal, cruiseHeight);

            if (Plan.Holding)
            {
                heldFor += deltaTime;
                if (heldFor >= groundWaitSeconds) GiveUpGoal();
            }

            // Landed: touched down this step, or a touchdown that could not set its pilot down last tick
            // and is retried.
            if (Plan.Phase == NpcFlightPhase.Landed)
            {
                // Only a known landing lands, so down over no ground is down on the landing ground itself
                // — never at the stand-in depth, never in mid-air.
                Vector3 ground = onDeck ? landingPoint : grounded ? new Vector3(position.x, below.y, position.z) : landingPoint;
                Touchdown(ground, Mathf.Max(0f, -lastVelocity.y));
                return MoveIntent.Idle();
            }

            return MoveIntent.MoveTo(step.Target, ArriveStopDistance, step.Speed);
        }

        private MoveIntent Cruise(Vector3 position, float groundBelow, Vector3 centre, float speedFraction)
        {
            NpcFlightStep step = Plan.Hold(position, groundBelow, centre, cruiseHeight);
            return MoveIntent.MoveTo(step.Target, ArriveStopDistance, step.Speed * speedFraction);
        }

        /// <summary>
        /// The escort order: the station on a live anchor, or — the anchor gone — a circle at cruise over where
        /// it was last seen, until the owner re-anchors or lands this craft.
        /// </summary>
        private MoveIntent KeepStation(Vector3 position, float groundBelow, float deltaTime)
        {
            Transform anchor = station.Anchor;
            AnchorLost = anchor == null || !anchor.gameObject.activeInHierarchy;
            if (AnchorLost) return Cruise(position, groundBelow, anchorLastPosition, 1f);

            TrackAnchor(anchor, deltaTime);
            CollectNeighbours(position);

            var seen = new AnchorState(anchor.position, anchor.forward, anchorVelocity);
            EscortStep step = EscortSteering.Step(position, seen, station, escort, Motor.TopSpeed, groundBelow, Time.time,
                                                  Neighbours);
            MoveIntent intent = MoveIntent.MoveTo(step.Target, ArriveStopDistance, step.Speed);
            return step.HoldsHeading ? intent.WithFacing(position + step.Heading) : intent;
        }

        /// <summary>
        /// The anchor's velocity: its motor's when it has one, else its own motion low-passed over
        /// <see cref="EscortSettings.anchorVelocitySmoothing"/> (a body moves only on physics steps, so a raw
        /// per-frame difference reads zero, then double).
        /// </summary>
        private void TrackAnchor(Transform anchor, float deltaTime)
        {
            Vector3 now = anchor.position;
            if (anchor.TryGetComponent(out IMovementMotor anchorMotor))
                anchorVelocity = anchorMotor.Velocity;
            else if (anchorSeen && deltaTime > 0f)
                anchorVelocity = Vector3.Lerp(anchorVelocity, (now - anchorLastPosition) / deltaTime,
                                              1f - Mathf.Exp(-deltaTime / Mathf.Max(MinSmoothing, escort.anchorVelocitySmoothing)));

            anchorLastPosition = now;
            anchorSeen = true;
        }

        /// <summary>Every other escorting craft within separation range of this one.</summary>
        private void CollectNeighbours(Vector3 position)
        {
            Neighbours.Clear();
            float reachSqr = escort.separationRadius * escort.separationRadius;
            foreach (NpcAviator other in Escorting)
            {
                if (other == this || other == null) continue;
                Vector3 there = other.transform.position;
                if ((there - position).sqrMagnitude <= reachSqr) Neighbours.Add(there);
            }
        }

        private void SetOrder(FlightOrder next)
        {
            Order = next;
            onDeck = false;
            if (next != FlightOrder.Escort) Escorting.Remove(this);
            else if (!Escorting.Contains(this)) Escorting.Add(this);
        }

        private void TryChooseSite()
        {
            // The rule VesselPilot learned the hard way: ground that has not streamed in has no site to
            // find, and "no site" there is not an answer.
            if (streamer != null && !streamer.IsGroundLoadedAround(goal, landing.ringMax + footprintRadius)) return;

            DropSite? site = LandingSiteFinder.Find(goal, transform.position, footprintRadius, landing, probe);
            if (!site.HasValue || site.Value.Mode != DropMode.Land) return;

            HasSite = true;
            landingKnown = true;
            landingPoint = site.Value.Point;
        }

        /// <summary>The goal projected onto the ground, once there is ground under it to find.</summary>
        private void ProbeLanding()
        {
            if (!probe.TryGround(goal, out Vector3 ground, out _)) return;
            landingKnown = true;
            landingPoint = ground;
        }

        /// <summary>
        /// Circled groundWaitSeconds over a goal whose ground never streamed in: land on the last ground
        /// flown over instead (where it took off, if it has flown over none) — a real surface, unlike the goal.
        /// </summary>
        private void GiveUpGoal()
        {
            goal = lastGround;
            landingPoint = lastGround;
            landingKnown = true;
            HasSite = false;
            GaveUp = true;
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
            if (IsWorld(other)) Crash(point, normal, onDeck && other.attachedRigidbody != null);
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
        /// <param name="onHull">Coming down on a deck and touched a hull: put the pilot down there, not on the ground below it.</param>
        private void Crash(Vector3 point, Vector3 normal, bool onHull)
        {
            if (!flying || !Network.Simulates(this) || Time.time - launchedAt < launchGraceSeconds) return;

            Vector3 ground = !onHull && probe.TryGround(point, out Vector3 hit, out _) ? hit : point;
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
            Escorting.Remove(this);
            wreckedAtHeight = transform.position.y;
            Plan.Wreck(transform.position);
            PilotReleased?.Invoke(corpse, false);
        }

        private void Retire()
        {
            Escorting.Remove(this);
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
            Escorting.Remove(this);
            flying = false;
            flyingPilot = null;
            UnwatchPilot();
            if (!ReferenceEquals(pilot, null)) PilotReleased?.Invoke(pilot, IsAlive(pilot));
        }
    }
}
