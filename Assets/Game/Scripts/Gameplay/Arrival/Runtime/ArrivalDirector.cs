using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Flies the crash landing, once per world, on the server.
    ///
    /// <para>
    /// A story world arrives in one ship. A versus match arrives in one per team, launched on the
    /// same frame and landing on the same frame, each on its own arc and its own authored point —
    /// that half lives in <c>ArrivalDirector.Versus.cs</c>. Everything below is what the two share:
    /// making a hull, seating people in it, holding the launch until the crew is aboard, and walking
    /// the hulls down.
    /// </para>
    ///
    /// <para>
    /// A plain <see cref="MonoBehaviour"/>, deliberately. It owns no replicated state and sends no
    /// messages of its own: the ships it makes are networked by <see cref="IWorldService"/> and
    /// replicated by that prefab's own <c>ClientNetworkTransform</c>, and the seating belongs to
    /// <see cref="SeatedRider"/>. Being a <c>NetworkBehaviour</c> would buy nothing and cost a
    /// scene-placed <c>NetworkObject</c>, whose id has to survive being authored into a scene to
    /// work at all — the same reasoning <c>VersusShipSpawner</c> records.
    /// </para>
    ///
    /// <para>
    /// Everything it does that matters happens on the server. Its callers are
    /// <c>NetworkGameManager</c>'s spawn coroutines, which already run nowhere else.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public partial class ArrivalDirector : MonoBehaviour
    {
        public static ArrivalDirector Instance { get; private set; }

        /// <summary>
        /// Whether the arrival took a client off the caller's hands, answered by the coroutine that
        /// tried.
        ///
        /// <para>
        /// A per-call object rather than a field on the director, and that is not fussiness: one of
        /// these coroutines runs per connecting client, concurrently, and a shared "did the last one
        /// work" flag would be read by the wrong client's flow. It is also why the answer cannot be
        /// <see cref="HasArrived"/> — that flips the moment the formation lands, which can happen
        /// while somebody else's spawn is still in flight.
        /// </para>
        /// </summary>
        public class Attempt
        {
            /// <summary>
            /// True once a body has been made for this client, whether or not it then reached a
            /// seat. Deliberately NOT "was seated": the body is spawned before the seating can be
            /// attempted, so a caller reading the narrower answer would spawn a second body for
            /// anybody whose seating failed on the last step.
            /// </summary>
            public bool Handled { get; set; }
        }

        [Tooltip("The ship the crew arrives in. It MUST be registered in the network prefab list, " +
                 "or it spawns for the host and for nobody else. Story worlds only — a versus match " +
                 "takes its hulls from VersusShipSpawner, so every team gets the same ship the " +
                 "arena is configured with.")]
        [SerializeField] private GameObject shipPrefab;

        [Tooltip("The cutscene each machine plays for its own player. Presentation only.")]
        [SerializeField] private ArrivalCutscene cutscene;

        [Tooltip("The descent, as numbers. ImpactPosition is overwritten at runtime from the point " +
                 "the world was actually streamed around; a versus match also rewrites the bearing " +
                 "so each team lands facing the way its spawn point asks.")]
        [SerializeField] private ArrivalPath path = ArrivalPath.Default;

        [Tooltip("How long the descent takes, in seconds. The cutscene is told this value rather " +
                 "than carrying its own, so the beats cannot drift from the hull. Shared by every " +
                 "ship in a formation, which is what makes them land together. Everything hung off " +
                 "it is a FRACTION of it — the shake curve, the burn envelope, the tumble — so " +
                 "retuning this number retimes the whole sequence and needs nothing else changed. " +
                 "It is the opening of the game and the player has no controls during it, so it is " +
                 "kept as short as the arc still reads at (GDC-L1-UX-0007).")]
        [SerializeField] private float descentDuration = 18.2f;

        [Tooltip("How long the hull is held at the attitude it hit the ground in, before it topples " +
                 "onto its belly. A beat, not a pause: it is what makes the contact read as a blow " +
                 "landing rather than as one continuous movement.")]
        [SerializeField] private float settleHold = 0.2f;

        [Tooltip("How long the wreck takes to drop off its nose and slam level. This is the crash " +
                 "itself, and its end pose is what the world keeps — the descent is deliberately " +
                 "committed nose-down, so this is the only thing that levels the ship at all.")]
        [SerializeField] private float settleDuration = 1.4f;

        [Tooltip("How long to wait for the ship to report its seats before giving up and spawning " +
                 "everyone the ordinary way.")]
        [SerializeField] private float seatResolveTimeout = 20f;

        [Tooltip("How long to hold the launch after the first player sits down, waiting for the " +
                 "rest of the connected crew to be seated too. Bounded, because a client that never " +
                 "finishes streaming must not keep everybody else at the top of the arc forever.")]
        [SerializeField] private float crewGatherTimeout = 12f;

        [Tooltip("How far apart the team descents are pulled, as a fraction of the authored arc. " +
                 "Zero flies every team the same shape from a different bearing; higher values also " +
                 "stagger how far out and how high each one starts.")]
        [Range(0f, ArrivalFormation.MaxSpread)]
        [SerializeField] private float formationSpread = 0.3f;

        [Tooltip("Height the ground probe drops from when measuring the impact site.")]
        [SerializeField] private float probeHeight = 600f;

        [Tooltip("Gap left between the hull's LOWEST point and the ground it comes to rest on. The " +
                 "belly depth itself is measured off the prefab, so this is the visible gap under " +
                 "the wreck and nothing else.")]
        [SerializeField] private float wreckGroundClearance = 0.05f;

        [Tooltip("How far the low corner of the hull may hang before the impact site is rejected as " +
                 "too steep. A ship freezes its rotation and the settle leaves it level, so it " +
                 "always rests level on the highest ground it spans — the rest of it is in the air.")]
        [SerializeField] private float maxGroundSpread = 1f;

        [Tooltip("How far from the impact point a flatter place to come down may be looked for. " +
                 "Zero crashes on the authored point whatever the ground does there.")]
        [SerializeField] private float landingSearchRadius = 60f;

        [Tooltip("Spacing of the search rings. Smaller finds tighter shelves and costs more probes.")]
        [SerializeField] private float landingSearchStep = 12f;

        [Tooltip("How far off the ground a landed hull may be before it is put down by hand and the " +
                 "miss is logged. The wreck is persisted where the descent leaves it, so a hull " +
                 "that stopped in mid-air stays there for the life of the world.")]
        [SerializeField] private float landingTolerance = 0.25f;

        [Tooltip("The largest correction SetDown will believe. The arc was PLANNED to end with the " +
                 "hull on measured ground, so a probe that answers hundreds of metres is not " +
                 "measuring the landing site — it is measuring through a hole where unloaded chunk " +
                 "colliders should be, down to whatever static geometry lies buried below. In a " +
                 "versus match that put both ships 500+ m under the desert and left the terrain " +
                 "guard to fish them back out.")]
        [SerializeField] private float maxTrustedCorrection = 30f;

        [Tooltip("How long the wreck sits still after landing before the crew are told they may " +
                 "get up. Long enough for the cutscene's blackout to lift, so the prompt is not " +
                 "offered to somebody still looking at a black screen.")]
        [SerializeField] private float releaseDelay = 1.6f;

        [Tooltip("Backstop. Anyone still sitting in a landed hull this many seconds after being " +
                 "told they may leave is turfed out. Only ever reached by a player who CANNOT " +
                 "get up — a lost binding, a prompt that never drew — never by one taking their " +
                 "time, which is why it is minutes rather than seconds.")]
        [SerializeField] private float strandedSeatTimeout = 180f;

        /// <summary>Every hull on its way down, by team. A story world files its one under -1.</summary>
        private readonly Dictionary<int, ArrivalFlight> flights = new();

        /// <summary>How many clients have been put in a seat, for the launch gate.</summary>
        private int seatedClients;

        /// <summary>How many hulls are mid-descent, so the release waits for the last of them.</summary>
        private int descending;

        /// <summary>True once the launch gate is counting down, so it is only ever started once.</summary>
        private bool launching;

        /// <summary>
        /// How long a descent takes. The authority for it, which is why <c>ArrivalCutscene</c> is
        /// told rather than authoring its own.
        ///
        /// <para>
        /// Readable on every machine, not just the server: this component is a plain
        /// <c>MonoBehaviour</c> that exists everywhere and only ACTS on the server, so the number is
        /// present on a client that will never fly anything. <c>EntryBurn</c> reads it there — a
        /// hull's own presentation has to know how long the arc it is riding takes, and a second
        /// copy of the figure serialised on the ship would drift the moment this one was retuned.
        /// </para>
        /// </summary>
        public float DescentDuration => descentDuration;

        /// <summary>True once the crash has finished, or once a save said it already had.</summary>
        public bool HasArrived { get; private set; }

        /// <summary>True while any hull is actually flying its arc.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Should the next player to spawn be flown down in a ship rather than placed on the ground?
        ///
        /// <para>
        /// False once the crash is done, so everybody who joins afterwards spawns normally — the
        /// arrival is something that happened to this world once, not something that happens to
        /// each player. Clearing <see cref="shipPrefab"/> is also how a world says it has no
        /// arrival at all, in versus as well as in a story world.
        /// </para>
        /// </summary>
        public bool IsPending => !HasArrived && shipPrefab != null;

        /// <summary>How level the ground has to be under a wreck, and how far to look for it.</summary>
        private LandingTolerance Landing =>
            new(maxGroundSpread, landingSearchRadius, landingSearchStep, wreckGroundClearance);

        /// <summary>
        /// Which way a descent leaves the hull pointing. Read off the path rather than tracked,
        /// because the footprint the landing is measured against turns with the wreck.
        /// </summary>
        private static float LandingYawOf(in ArrivalPath forPath) =>
            ArrivalFormation.LandingYawForBearing(forPath.StartBearing, forPath.SweepDegrees);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        // Subscribed on EVERY machine, which is the point. The descent is flown by the server, so a
        // presentation started from that coroutine plays for the host and for nobody else — the
        // clients ride the same hull down with their controls live and no letterbox. SeatedRider
        // raises these wherever it seats the local player, which is the only moment that is true on
        // exactly the machines that need it.
        //
        // Two events rather than one, because they answer different questions. Sitting down is
        // per-machine and happens whenever THAT machine finishes streaming: it puts the screen to
        // black and nothing else. The launch is one server decision announced to everybody at once,
        // and it is what actually starts the timed beats — which is what makes them start together.
        private void OnEnable()
        {
            SeatedRider.LocalPlayerSeated += PlayLocalCutscene;
            SeatedRider.LocalCrewLaunched += LaunchLocalCutscene;

            // A save taken during the descent records "arrived" (see ArrivalSaveable), so the pose
            // it captures is the pose the reloaded world keeps — and a reload never re-flies the
            // crash. Grounding first is what stops a quit halfway down the arc reloading as a hull
            // frozen nose-down in the sky at the angle it flew in at.
            SpaceGame.Core.Persistence.SaveManager.Capturing += GroundUnfinishedFlights;
        }

        private void OnDisable()
        {
            SeatedRider.LocalPlayerSeated -= PlayLocalCutscene;
            SeatedRider.LocalCrewLaunched -= LaunchLocalCutscene;
            SpaceGame.Core.Persistence.SaveManager.Capturing -= GroundUnfinishedFlights;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Restore-only. Called by <see cref="ArrivalSaveable"/>; do not call from gameplay.</summary>
        public void RestoreArrived(bool arrived) => HasArrived = arrived;

        /// <summary>
        /// Puts one client into a seat in the world's single arrival ship.
        ///
        /// <para>
        /// Server-only. <paramref name="impactPoint"/> is the position <c>NetworkGameManager</c> has
        /// already resolved and streamed the world around; reusing it rather than resolving a second
        /// one matters for the reason that class documents at length — a second resolve returns a
        /// different point from the one the terrain was prepared for, and that is how players end up
        /// under the ground.
        /// </para>
        /// </summary>
        public IEnumerator SpawnIntoArrival(ulong clientId, Vector3 impactPoint)
        {
            if (!Network.Server)
            {
                Debug.LogError("[Arrival] SpawnIntoArrival called off the server.", this);
                yield break;
            }

            ArrivalFlight flight = null;
            float deadline = Time.time + seatResolveTimeout;

            // "Not yet" and "never" are different answers. A missing chunk resolves itself if we
            // wait; a missing prefab never will, and waiting out the timeout for it would strand the
            // player for twenty seconds before doing the same thing.
            while (flight == null)
            {
                flight = EnsureStoryFlight(impactPoint, out bool fatal);

                if (flight != null) break;

                if (fatal)
                {
                    SpawnNormally(clientId, impactPoint);
                    yield break;
                }

                if (Time.time >= deadline)
                {
                    Debug.LogError($"[Arrival] No ground under the impact point after " +
                                   $"{seatResolveTimeout}s, so there is nowhere to crash. Spawning " +
                                   "client " + clientId + " the ordinary way.", this);
                    SpawnNormally(clientId, impactPoint);
                    yield break;
                }

                yield return null;
            }

            var attempt = new Attempt();
            yield return SeatIntoFlight(clientId, flight, attempt);

            if (!attempt.Handled) SpawnNormally(clientId, impactPoint);
        }

        /// <summary>Frees a seat when its occupant disconnects mid-descent.</summary>
        public void ReleaseClient(ulong clientId)
        {
            GameObject player = ResolvePlayer(clientId);
            if (player == null) return;

            foreach (ArrivalFlight flight in flights.Values)
                if (flight.IsAlive)
                    flight.Seating.Release(player);
        }

        /// <summary>
        /// Spawns a body for this client and sits it down, then opens the launch gate.
        ///
        /// <para>
        /// Shared by both routes in. <paramref name="attempt"/> is how the caller learns whether it
        /// still has a player to place — see <see cref="Attempt"/> for why that cannot be a field.
        /// </para>
        /// </summary>
        private IEnumerator SeatIntoFlight(ulong clientId, ArrivalFlight flight, Attempt attempt)
        {
            if (flight.Seating.SeatCount == 0)
            {
                Debug.LogError($"[Arrival] '{flight.Ship.name}' has no ShipSeat markers, so nobody " +
                               "can be seated in it.", this);
                yield break;
            }

            int seatIndex = SeatOrdering.SeatFor(flight.Claimed, flight.Seating.SeatCount);
            flight.Claimed++;

            // Spawned on the hull rather than at the world origin, so the body exists somewhere
            // sensible for the one frame before SeatedRider takes over placing it. The exact seat is
            // not used here: while the flight is live every machine stamps every occupant onto its
            // own copy of the seat from the replicated seat index (SeatedRider.HoldSeats), so
            // nothing about this spawn pose survives past the frame.
            Transform hull = flight.Seating.transform;
            SpawnManager.Instance.SpawnPlayerForClient(clientId, hull.position, hull.rotation);
            attempt.Handled = true;

            // The body is created by the call above, but its NetworkObject is not addressable until
            // the next frame — and SeatedRider addresses players by NetworkObjectId.
            yield return null;

            GameObject player = ResolvePlayer(clientId);
            if (player == null)
            {
                Debug.LogError($"[Arrival] Client {clientId} has no player object after spawning, so " +
                               "they cannot be seated.", this);
                yield break;
            }

            flight.Seating.Seat(player, seatIndex);

            seatedClients++;

            if (!launching && !HasArrived)
            {
                launching = true;
                StartCoroutine(FlyFormation());
            }
        }

        /// <summary>
        /// The fallback everywhere above leads to. Marks the arrival done, because a world that
        /// spawned somebody on the ground has had whatever arrival it is going to get — leaving it
        /// pending would put the NEXT player into a ship the first one never rode.
        /// </summary>
        private void SpawnNormally(ulong clientId, Vector3 position)
        {
            HasArrived = true;
            SpawnManager.Instance.SpawnPlayerForClient(clientId, position);
        }

        /// <summary>
        /// The world's single arrival ship, spawned at the top of its arc once.
        /// <paramref name="fatal"/> distinguishes "not yet, ask again" from "this will never work",
        /// which the caller must treat differently.
        /// </summary>
        private ArrivalFlight EnsureStoryFlight(Vector3 impactPoint, out bool fatal)
        {
            fatal = false;

            if (flights.TryGetValue(ArrivalFlight.NoTeam, out ArrivalFlight existing) && existing.IsAlive)
                return existing;

            if (!CanFly(out fatal)) return null;

            // The heading is not known until the path is known, and the path's bearing is authored,
            // so the hull's footprint is measured at the yaw the descent will actually leave it on.
            float landingYaw = LandingYawOf(path);

            if (!ShipGrounding.TryResolveHullLanding(new Vector2(impactPoint.x, impactPoint.z),
                                                     landingYaw, shipPrefab, probeHeight,
                                                     Landing, out Vector3 impact))
            {
                // NOT fatal. In a streamed world this means the chunks under the impact site have
                // not loaded yet, and the only correct response is to wait and ask again — the
                // contract ShipGrounding documents.
                return null;
            }

            ArrivalPath storyPath = path;
            storyPath.ImpactPosition = impact;

            ArrivalTrajectory.Evaluate(0f, storyPath, out Vector3 start, out Quaternion startRotation);

            GameObject ship = GameServices.World.Spawn(shipPrefab, start, startRotation);

            if (ship == null)
            {
                Debug.LogError("[Arrival] Spawning the arrival ship returned nothing. Is it " +
                               "registered in the network prefab list?", this);
                fatal = true;
                return null;
            }

            ship.name = shipPrefab.name + " (Arrival)";

            return Register(ArrivalFlight.NoTeam, ship, storyPath, out fatal);
        }

        /// <summary>
        /// Whether an arrival can be flown at all, before any ground is measured. The checks that
        /// can never come good, so a caller does not spend its whole timeout discovering one.
        /// </summary>
        private bool CanFly(out bool fatal)
        {
            fatal = true;

            if (shipPrefab == null)
            {
                Debug.LogError("[Arrival] No ship prefab assigned — there is nothing to arrive in.", this);
                return false;
            }

            if (path.LateralBudget <= 0f)
            {
                Debug.LogError("[Arrival] Lateral budget must be positive; a zero-radius descent has " +
                               "no heading to fly.", this);
                return false;
            }

            fatal = false;
            return true;
        }

        /// <summary>
        /// Files a spawned hull as a flight, or refuses it — <paramref name="fatal"/>, because a
        /// ship prefab with no <see cref="SeatedRider"/> is a prefab problem and no amount of
        /// waiting fixes it.
        /// </summary>
        private ArrivalFlight Register(int team, GameObject ship, in ArrivalPath flightPath, out bool fatal)
        {
            fatal = false;

            var seating = ship.GetComponent<SeatedRider>();

            if (seating == null)
            {
                Debug.LogError($"[Arrival] '{ship.name}' has no SeatedRider, so nobody can be seated " +
                               "in it.", ship);
                fatal = true;
                return null;
            }

            var flight = new ArrivalFlight(team, ship, seating, flightPath);
            flights[team] = flight;
            return flight;
        }

        /// <summary>
        /// Holds the launch until the crew is aboard, then sends every hull down at once.
        ///
        /// <para>
        /// The gate is what makes a versus start a start: ships that launched as each team's first
        /// player happened to finish streaming would descend out of step and land seconds apart,
        /// which is not a countdown, it is a queue. It earns its place in a story world too — a
        /// second player still loading used to join a crash already in progress.
        /// </para>
        ///
        /// <para>
        /// Bounded, for the reason every wait in the spawn flow is bounded: a client that never
        /// finishes must not hold everybody else at the top of the arc forever. Giving up costs
        /// that player the opening and is still better than nobody landing.
        /// </para>
        /// </summary>
        private IEnumerator FlyFormation()
        {
            yield return WaitForCrewAboard();

            IsRunning = true;

            foreach (ArrivalFlight flight in flights.Values)
            {
                if (flight.Launched || !flight.IsAlive) continue;

                flight.Launched = true;
                descending++;

                // Announced BEFORE the descent starts moving the hull, on the same frame the gate
                // opens. This is the instant every machine's presentation is timed from, and it is
                // the only thing that makes the fade to black land on the impact for a client as
                // well as for the host.
                flight.Seating.AnnounceLaunch();

                StartCoroutine(FlyDescent(flight));
            }

            // Bounded, because the settle is the ONLY thing that levels the ship: a descent
            // coroutine that dies for any reason leaves its wreck standing on its nose at the
            // dive angle forever, releasable never set and the crew sealed in their chairs. The
            // deadline is the whole sequence plus a generous grace, so it is only ever reached by
            // a flight that has genuinely stalled.
            float landingDeadline = Time.time + descentDuration + Mathf.Max(0f, settleHold)
                                    + Mathf.Max(0.01f, settleDuration) + LandingWatchdogGrace;

            while (descending > 0 && Time.time < landingDeadline)
                yield return null;

            if (descending > 0)
            {
                RecoverStalledDescents();
                descending = 0;
            }

            // Held open until the cutscene's own blackout has lifted, so the prompt is not offered
            // to somebody still looking at a black screen.
            yield return new WaitForSeconds(releaseDelay);

            // Unlocked rather than emptied. The crew stay in their chairs and get up when they
            // press the key — landing in a wreck and then being teleported out of your own seat
            // reads as the game taking the controls back at the exact moment it hands them over.
            foreach (ArrivalFlight flight in flights.Values)
                if (flight.IsAlive)
                    flight.Seating.AllowRelease();

            HasArrived = true;
            IsRunning = false;

            StartCoroutine(EmptySeatsEventually());
        }

        /// <summary>
        /// The backstop: turfs out anybody still sitting in a landed hull long after they were told
        /// they could leave.
        ///
        /// <para>
        /// Here because "get up when you like" and "cannot get up at all" look identical from the
        /// outside, and one of them is a player stuck in a chair for the rest of the session — a
        /// dropped input binding, a prompt that never drew, someone who walked away mid-landing.
        /// The timeout is long enough that nobody who is simply taking their time will ever meet
        /// it.
        /// </para>
        /// </summary>
        private IEnumerator EmptySeatsEventually()
        {
            yield return new WaitForSeconds(strandedSeatTimeout);

            foreach (ArrivalFlight flight in flights.Values)
                if (flight.IsAlive)
                    flight.Seating.ReleaseAll();
        }

        /// <summary>
        /// Waits for every connected client to be sitting down, or for the gather to time out.
        ///
        /// Counting seatings rather than asking the hulls who is in them: a client that failed to be
        /// seated is never coming, and a gate that waited for a seat count would then always run to
        /// the timeout.
        /// </summary>
        private IEnumerator WaitForCrewAboard()
        {
            float deadline = Time.time + Mathf.Max(0f, crewGatherTimeout);

            while (seatedClients < ConnectedClients)
            {
                if (Time.time >= deadline)
                {
                    Debug.LogWarning($"[Arrival] Only {seatedClients} of {ConnectedClients} connected " +
                                     "players were aboard after " + crewGatherTimeout + "s. Launching " +
                                     "without the rest — they will join the descent already in " +
                                     "progress.", this);
                    yield break;
                }

                yield return null;
            }
        }

        private static int ConnectedClients =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;

        /// <summary>
        /// Walks one hull down its arc. The transform is written on the server alone and reaches
        /// everyone else through the ship prefab's own ClientNetworkTransform.
        /// </summary>
        private IEnumerator FlyDescent(ArrivalFlight flight)
        {
            QuietHull(flight.Ship, out HullState hull);

            // Deliberately NOT starting the cutscene here. This coroutine runs on the server alone,
            // and the presentation has to happen on every machine — it is started by SeatedRider
            // raising LocalPlayerSeated, which fires wherever a local player sits down.

            ArrivalPath flightPath = flight.Path;

            // Measured here rather than authored on the path, and measured on the hull that is
            // actually flying: a versus formation takes its ships from the arena's spawner, not
            // from this component's prefab, and a lift taken off the wrong hull points the nose
            // either through the ground or at nothing. It shifts the whole arc, so the hull steps
            // up by it on the launch frame — a few metres, two kilometres up, before it has moved
            // at all, while every crew camera is still fading up from black.
            flightPath.TouchdownLift = MeasureTouchdownLift(flight.Ship, flightPath);

            float elapsed = 0f;

            while (elapsed < descentDuration)
            {
                // Checked every frame, not hoped: a hull that goes away mid-arc (a torn-down
                // session, an errant despawn) used to kill this coroutine with a
                // MissingReferenceException, which left `descending` stuck above zero, releasable
                // never set, and — on any machine still watching — a ship frozen at the dive
                // angle. The watchdog in FlyFormation is the second half of the same guarantee.
                if (!flight.IsAlive)
                {
                    AbandonDescent(flight);
                    yield break;
                }

                elapsed += Time.deltaTime;

                ArrivalTrajectory.Evaluate(elapsed / descentDuration, flightPath,
                                           out Vector3 position, out Quaternion rotation);

                flight.Ship.transform.SetPositionAndRotation(position, rotation);
                yield return null;
            }

            if (!flight.IsAlive)
            {
                AbandonDescent(flight);
                yield break;
            }

            // Contact, on the authored pose exactly rather than wherever the last frame's delta
            // time happened to leave it — the settle is measured from here, and the settle is what
            // the wreck is persisted as.
            ArrivalTrajectory.Evaluate(1f, flightPath, out Vector3 touchdown, out Quaternion touchdownRotation);
            flight.Ship.transform.SetPositionAndRotation(touchdown, touchdownRotation);

            // Held at the attitude it came in at before anything moves. A collision resolved inside
            // one frame is perceptually thin; holding the moment of contact is what sells the force
            // of it, and it costs the player nothing here because they have no controls yet
            // (GDC-L1-FEEL-0005).
            yield return new WaitForSeconds(Mathf.Max(0f, settleHold));

            yield return Settle(flight.Ship, flightPath);

            if (!flight.IsAlive)
            {
                AbandonDescent(flight);
                yield break;
            }

            SetDown(flight.Ship, ArrivalTrajectory.RestRotation(flightPath).eulerAngles.y);

            RestoreHull(hull);

            // The hull's own motor is awake again the moment RestoreHull runs, and a hover servo
            // holds altitude rather than resting: left with a standing order it lifts the wreck
            // back off the ground it was just set on and keeps it there. Parking it hands the hull
            // to physics, which is what restWhenParked exists for — see HoverRigidbodyMotor.
            ParkHull(flight.Ship);

            flight.Landed = true;
            descending--;
        }

        /// <summary>A descent whose hull went away. The books still have to balance.</summary>
        private void AbandonDescent(ArrivalFlight flight)
        {
            Debug.LogError($"[Arrival] The {(flight.Team == ArrivalFlight.NoTeam ? "arrival" : VersusRules.TeamName(flight.Team))} " +
                           "hull was destroyed mid-descent. Its crew will be released by the " +
                           "formation's own flow.", this);
            descending--;
        }

        /// <summary>How long past the authored sequence a descent may run before it is declared stalled.</summary>
        private const float LandingWatchdogGrace = 10f;

        /// <summary>
        /// The backstop for a descent that never finished: any launched hull still unlanded when
        /// the watchdog fires is put on its landing point by hand, woken and parked.
        ///
        /// <para>
        /// This trades the rest of the crash presentation for the invariant that actually matters:
        /// the settle is the only thing that levels the ship, the levelled pose is what the save
        /// keeps, and a wreck left at the dive angle is the shape of the world forever. Loud,
        /// because reaching this means a descent coroutine died and that cause is worth finding.
        /// </para>
        /// </summary>
        private void RecoverStalledDescents()
        {
            foreach (ArrivalFlight flight in flights.Values)
            {
                if (!flight.IsAlive || !flight.Launched || flight.Landed) continue;

                Debug.LogError($"[Arrival] '{flight.Ship.name}' never finished its descent — " +
                               "grounding it by hand at its landing point.", flight.Ship);

                WakeHull(flight.Ship);
                GroundFlightAtRest(flight);
                flight.Landed = true;
            }
        }

        /// <summary>
        /// Grounds every unlanded flight NOW, called by the save system immediately before it
        /// captures the world.
        ///
        /// <para>
        /// <c>ArrivalSaveable</c> records a mid-descent world as "arrived", so whatever pose this
        /// capture takes is the pose the reloaded world opens with — and a reload never re-flies
        /// the crash to fix it. Snapping the hull to the exact pose the settle would have ended on
        /// makes the file indistinguishable from a landing that finished. A descent that is still
        /// running afterwards simply rewrites the transform on its next frame and lands normally,
        /// which is why nothing here is marked <see cref="ArrivalFlight.Landed"/>.
        /// </para>
        /// </summary>
        private void GroundUnfinishedFlights()
        {
            foreach (ArrivalFlight flight in flights.Values)
            {
                if (!flight.IsAlive || flight.Landed) continue;

                GroundFlightAtRest(flight);

                Debug.LogWarning($"[Arrival] '{flight.Ship.name}' was saved mid-arrival, so it was " +
                                 "captured at its landing point rather than in the air.", flight.Ship);
            }
        }

        /// <summary>
        /// Puts one hull on the pose its settle would have ended on: the impact point, yaw only,
        /// set down against the measured ground and parked. The shared tail of every path that has
        /// to finish a landing without flying it.
        /// </summary>
        private void GroundFlightAtRest(ArrivalFlight flight)
        {
            Quaternion rest = ArrivalTrajectory.RestRotation(flight.Path);

            flight.Ship.transform.SetPositionAndRotation(flight.Path.ImpactPosition, rest);

            // Before SetDown, not after: it measures the hull's colliders, and collider bounds
            // live in the physics scene rather than on the transform just written.
            Physics.SyncTransforms();

            SetDown(flight.Ship, rest.eulerAngles.y);
            ParkHull(flight.Ship);
        }

        /// <summary>
        /// Re-enables what <see cref="QuietHull"/> silenced, for a recovery that no longer has the
        /// coroutine's captured state. Restores the prefab's intent rather than a snapshot: this
        /// hull is a working vehicle, and every one of these is enabled on the asset.
        /// </summary>
        private static void WakeHull(GameObject ship)
        {
            var body = ship.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = false;

            foreach (Behaviour b in new Behaviour[]
                     {
                         ship.GetComponent<SpaceGame.Agents.AgentController>(),
                         ship.GetComponent<SpaceGame.Agents.HoverRigidbodyMotor>(),
                         ship.GetComponent<SpaceGame.World.Safety.UnderTerrainGuard>(),
                     })
                if (b != null) b.enabled = true;
        }

        /// <summary>Is this hull one the director is currently flying, or holding ready to fly?</summary>
        public bool IsFlightHull(GameObject ship)
        {
            foreach (ArrivalFlight flight in flights.Values)
                if (flight.IsAlive && flight.Ship == ship)
                    return true;

            return false;
        }

        /// <summary>
        /// Drops the hull off the nose it speared in on, onto the belly it rests on.
        ///
        /// <para>
        /// Its own beat rather than the end of the descent, because the two want opposite things
        /// and used to be one curve that could only satisfy one of them: what the player watches
        /// has to stay pointed at the ground all the way into it, and what the world KEEPS has to
        /// be a level hull they can walk around in. The old flare bought the second by giving up
        /// the first, and levelled the ship out over the last five seconds of the dive.
        /// </para>
        /// <para>
        /// Ends on the settled pose exactly, for the same reason the descent ends on the touchdown
        /// pose exactly: the wreck is persisted from here and is measured against the assumption
        /// that it differs from its prefab by yaw alone.
        /// </para>
        /// </summary>
        private IEnumerator Settle(GameObject ship, ArrivalPath flightPath)
        {
            float duration = Mathf.Max(0.01f, settleDuration);
            float settling = 0f;

            while (settling < duration)
            {
                if (ship == null) yield break; // the caller's IsAlive check reports it

                settling += Time.deltaTime;

                ArrivalTrajectory.EvaluateSettle(settling / duration, flightPath,
                                                 out Vector3 position, out Quaternion rotation);

                ship.transform.SetPositionAndRotation(position, rotation);
                yield return null;
            }

            if (ship == null) yield break;

            ArrivalTrajectory.EvaluateSettle(1f, flightPath, out Vector3 rest, out Quaternion restRotation);
            ship.transform.SetPositionAndRotation(rest, restRotation);
        }

        /// <summary>
        /// How far above its resting height the hull has to arrive for the part of it that reaches
        /// the ground to be its nose.
        ///
        /// <para>
        /// Not cosmetic. The cockpit is the highest, most forward thing on this hull and the crew
        /// are sitting in it, so a nose-down hull whose ORIGIN lands on the ground puts the camera
        /// several metres inside the terrain on the one frame the whole sequence is built around.
        /// Measured off the hull rather than derived from a length, because the belly is the only
        /// thing that knows where the pivot is.
        /// </para>
        /// <para>
        /// Never negative: a hull that somehow hangs less deep when pitched is a hull that needs no
        /// lifting, and lowering it into the ground to honour the arithmetic would be worse than
        /// the shape being slightly wrong.
        /// </para>
        /// </summary>
        private static float MeasureTouchdownLift(GameObject ship, in ArrivalPath flightPath)
        {
            ArrivalTrajectory.Evaluate(1f, flightPath, out Vector3 _, out Quaternion touchdownRotation);

            float pitched = ShipHull.BellyDropAt(ship, touchdownRotation);
            float resting = ShipHull.BellyDropAt(ship, ArrivalTrajectory.RestRotation(flightPath));

            return Mathf.Max(0f, pitched - resting);
        }

        /// <summary>
        /// Puts a landed hull on the ground, having measured whether it actually is.
        ///
        /// <para>
        /// Every height above this is arithmetic done BEFORE the descent flies, against terrain that
        /// streams in and out over the twenty-six seconds it takes — so the ground under the impact
        /// site can genuinely be a different answer by the time the hull arrives, and in the streamed
        /// world it frequently was. The wreck is persisted exactly here, so a miss is not a glitch
        /// that settles on the next frame; it is the shape of the world from now on.
        /// </para>
        /// <para>
        /// Corrected in place and reported, rather than asserted: a world that opens with its ship
        /// a metre out is worth a warning, and a world that refuses to open is not.
        /// </para>
        /// </summary>
        private void SetDown(GameObject ship, float landingYaw)
        {
            // Measured on the landed hull rather than on the prefab, and it is exact: the settle
            // ends level and the bank has unwound to zero, so the wreck differs from the prefab by
            // yaw alone — which an axis-aligned bounds measurement is blind to. It also means a versus
            // hull, which comes from the arena's spawner and not from this component's own prefab,
            // is measured as itself.
            float bellyDrop = ShipHull.BellyDrop(ship);

            // Collision first, heightmap only as the fallback — the opposite order to everything
            // that PLANNED this landing, and the whole point of this method. See
            // ShipGrounding.TryResolveCollisionGround: the plan is arithmetic over the heightmap,
            // so a landing checked against the heightmap is a plan checking itself and agrees with
            // itself even when the ship is left in the sky. The reach covers the descent's own
            // start altitude, because a hull that finished its arc a kilometre up is precisely the
            // one that has to be found and put down.
            bool measured = ShipGrounding.TryMeasureLandingAgainstCollision(
                ship.transform.position, landingYaw, ship, path.StartAltitude + probeHeight,
                bellyDrop, out float airGap);

            // An answer no real landing can produce is a probe with no data, not a correction. The
            // deep probe exists to find a hull stranded a kilometre up, so its reach also lets it
            // fall through a hole where unloaded chunk colliders should be and report whatever
            // static geometry lies buried far below — measured in versus as both team ships set
            // down 500+ m under the desert. The plan put the hull on ground it measured; a wildly
            // different answer here means the VERIFIER is blind, and blind means "fall through to
            // the heightmap", never "obey".
            if (measured && Mathf.Abs(airGap) > maxTrustedCorrection)
            {
                Debug.LogError($"[Arrival] Collision claims '{ship.name}' is {airGap:F1} m off the " +
                               "ground — farther than any real landing can miss by, so the " +
                               "colliders under the site are missing or belong to something " +
                               "buried. Ignoring physics here and asking the heightmap.", ship);
                measured = false;
            }

            if (measured) WarnIfHeightmapDisagrees(ship, landingYaw, bellyDrop, airGap);

            // The heightmap keeps the scenes physics cannot answer for: the arena and the test
            // scenes have colliders but an interior may have none under the hull at all, and a
            // measurement is still better than leaving the wreck on a guess.
            if (!measured && !ShipGrounding.TryMeasureLanding(ship.transform.position, landingYaw,
                                                              ship, probeHeight, bellyDrop, out airGap))
            {
                Debug.LogWarning($"[Arrival] '{ship.name}' came down where no ground could be " +
                                 "measured, so it is left on the pose the descent ended at.", ship);
                return;
            }

            // Same rule for the fallback: TryResolveGround raycasts where the heightmap has no
            // answer, and that ray can find the same buried geometry. The planned pose beats an
            // implausible measurement from either source.
            if (Mathf.Abs(airGap) > maxTrustedCorrection)
            {
                Debug.LogError($"[Arrival] Every ground source under '{ship.name}' answers " +
                               $"{airGap:F1} m — implausible for a planned landing, so the hull is " +
                               "left on the pose the descent ended at.", ship);
                return;
            }

            if (Mathf.Abs(airGap) <= landingTolerance) return;

            Vector3 corrected = ship.transform.position;
            corrected.y -= airGap - wreckGroundClearance;
            ship.transform.position = corrected;

            // Physics has moved, so anything measuring the hull on this same frame — the hover
            // motor's ground sensor the moment RestoreHull wakes it — reads the pose it was set
            // down in rather than the one it finished its descent in.
            Physics.SyncTransforms();

            Debug.LogWarning($"[Arrival] '{ship.name}' finished its descent {airGap:F2} m off the " +
                             $"ground — the ground under the impact site is not what it was when " +
                             "the arc was planned. Set down at y=" + corrected.y.ToString("F2") + ".",
                             ship);
        }

        /// <summary>
        /// Says so when the terrain heightmap and the colliders disagree about where the ground is.
        ///
        /// <para>
        /// Diagnostic, and deliberately permanent. The landing itself is now decided by collision,
        /// so a disagreement no longer strands the ship — but it means the world has two answers for
        /// where its own surface is, and every other consumer of
        /// <c>ShipGrounding.TryResolveGround</c> is still believing the other one: the arc this ship
        /// just flew, the versus spawner's landing pose, the spawn points. A silent correction here
        /// would fix the ship and hide that.
        /// </para>
        /// </summary>
        private void WarnIfHeightmapDisagrees(GameObject ship, float landingYaw, float bellyDrop,
                                              float collisionGap)
        {
            if (!ShipGrounding.TryMeasureLanding(ship.transform.position, landingYaw, ship,
                                                 probeHeight, bellyDrop, out float heightmapGap))
                return;

            float disagreement = Mathf.Abs(collisionGap - heightmapGap);
            if (disagreement <= landingTolerance) return;

            Debug.LogError($"[Arrival] The heightmap and the colliders disagree by " +
                           $"{disagreement:F2} m about the ground under '{ship.name}'. Collision " +
                           $"says the hull is {collisionGap:F2} m off it, the heightmap says " +
                           $"{heightmapGap:F2} m. The descent was planned against the heightmap, so " +
                           "every arrival height in this world is out by that much.", ship);
        }

        /// <summary>
        /// Stops the hull flying itself, so gravity holds it on the ground the descent put it on.
        ///
        /// <para>
        /// Not the same as <see cref="RestoreHull"/>, which only hands the components back. A hover
        /// motor with any standing order at all runs its height servo, and that servo holds the
        /// craft a ride height above the HIGHEST ground under its whole footprint — which on a slope
        /// is metres above the ground under the wreck itself. Measured in the shipped world, an
        /// arrival hull left that way sat 2.4 m up, at zero velocity, indefinitely.
        /// </para>
        /// </summary>
        private static void ParkHull(GameObject ship)
        {
            ship.GetComponent<SpaceGame.Agents.IMovementMotor>()?.ForceStop();
        }

        /// <summary>
        /// Starts the presentation on this machine, for this machine's player. Raised by
        /// <see cref="SeatedRider.LocalPlayerSeated"/>, which only ever fires for the arrival —
        /// ordinary seats are the mount system's, not this one's.
        ///
        /// <para>
        /// A cutscene is a per-machine thing; routing it through the wire would be replicating a
        /// camera, and the shared parts of the arrival — the hull's motion and who is in which seat
        /// — already travel on their own channels.
        /// </para>
        /// <para>
        /// Sitting down only takes the controls away and puts the screen to black. The cutscene
        /// then holds there until <see cref="LaunchLocalCutscene"/>, because the crew are seated
        /// seconds apart and the descent is not.
        /// </para>
        /// </summary>
        private void PlayLocalCutscene()
        {
            if (cutscene == null)
            {
                Debug.LogWarning("[Arrival] No cutscene assigned; the descent will fly with no " +
                                 "presentation and the player will keep their controls.", this);
                return;
            }

            if (CutsceneDirector.Instance == null)
            {
                Debug.LogWarning("[Arrival] No CutsceneDirector in the scene; the descent will fly " +
                                 "with no presentation.", this);
                return;
            }

            cutscene.Configure(descentDuration, settleHold + settleDuration);

            // TEMPORARY DIAGNOSTIC (2026-09-02) — remove once the missing arrival blackout is
            // diagnosed. Play() returning false is currently the only non-silent failure here, and
            // it is indistinguishable from this method never having been called at all.
            bool started = CutsceneDirector.Instance.Play(cutscene);
            Debug.Log($"[Arrival:DIAG] PlayLocalCutscene started={started} " +
                      $"descent={descentDuration} settle={settleHold + settleDuration}", this);
        }

        /// <summary>
        /// Releases the held presentation, on the one announcement every machine gets.
        ///
        /// <para>
        /// <paramref name="secondsAgo"/> is zero for everyone who was aboard when the formation
        /// launched, and the age of the launch for somebody seated into a descent already under
        /// way. Passed through rather than resolved here because the machine that knows is the one
        /// holding the ship's replicated launch instant, and that is <see cref="SeatedRider"/>.
        /// </para>
        /// </summary>
        private void LaunchLocalCutscene(float secondsAgo)
        {
            if (cutscene == null) return;

            cutscene.Launch(secondsAgo);
        }

        /// <summary>What the hull was doing for itself before the arrival took the wheel.</summary>
        private struct HullState
        {
            public Rigidbody Body;
            public bool WasKinematic;
            public RigidbodyInterpolation Interpolation;
            public Behaviour[] Silenced;
        }

        /// <summary>
        /// Stops a ship driving itself, so the descent is the only thing moving it.
        ///
        /// <para>
        /// <b>This is what the glitching was.</b> <c>PlayerShip</c> is a working vehicle: it carries
        /// a <c>HoverRigidbodyMotor</c>, an <c>AgentController</c> ticking its modules, and an
        /// <c>UnderTerrainGuard</c> whose whole job is to shove a hull it thinks is buried. Leaving
        /// those live while this coroutine teleports the transform every frame is two systems
        /// writing the same 60-tonne Rigidbody in the same frame, forever — which does not read as a
        /// drifting ship, it reads as the screen coming apart.
        /// </para>
        /// <para>
        /// Interpolation goes off for the same reason it does on a seated rider: it renders the hull
        /// from where physics had it a step ago, and a step ago is a long way back when the thing is
        /// being teleported down an arc.
        /// </para>
        /// <para>
        /// Everything is captured and handed back by <see cref="RestoreHull"/>, because the wreck is
        /// a real vehicle afterwards — it has to be able to hover, be mounted and be shoved out of
        /// terrain again once it is sitting on the ground.
        /// </para>
        /// </summary>
        private static void QuietHull(GameObject ship, out HullState state)
        {
            state = default;
            state.Body = ship.GetComponent<Rigidbody>();

            if (state.Body != null)
            {
                state.WasKinematic = state.Body.isKinematic;
                state.Interpolation = state.Body.interpolation;

                // Velocity first, kinematic second — a kinematic body has no velocity to write and
                // Unity warns on the attempt, once per hull per arrival.
                if (!state.Body.isKinematic)
                {
                    state.Body.linearVelocity = Vector3.zero;
                    state.Body.angularVelocity = Vector3.zero;
                }

                state.Body.isKinematic = true;
                state.Body.interpolation = RigidbodyInterpolation.None;
            }

            var silenced = new List<Behaviour>();

            // Named types rather than "disable every Behaviour": the ship also carries its savers,
            // its net relay and its articulated parts, and switching those off would be a different
            // and much worse bug than the one being fixed.
            AddIfPresent<SpaceGame.Agents.AgentController>(ship, silenced);
            AddIfPresent<SpaceGame.Agents.HoverRigidbodyMotor>(ship, silenced);
            AddIfPresent<SpaceGame.World.Safety.UnderTerrainGuard>(ship, silenced);

            foreach (Behaviour b in silenced) b.enabled = false;

            state.Silenced = silenced.ToArray();
        }

        private static void AddIfPresent<T>(GameObject ship, List<Behaviour> into) where T : Behaviour
        {
            var component = ship.GetComponent<T>();
            if (component != null && component.enabled) into.Add(component);
        }

        /// <summary>Gives the hull back everything <see cref="QuietHull"/> took.</summary>
        private static void RestoreHull(in HullState state)
        {
            if (state.Silenced != null)
                foreach (Behaviour b in state.Silenced)
                    if (b != null) b.enabled = true;

            if (state.Body == null) return;

            state.Body.isKinematic = state.WasKinematic;
            state.Body.interpolation = state.Interpolation;

            if (!state.Body.isKinematic)
            {
                state.Body.linearVelocity = Vector3.zero;
                state.Body.angularVelocity = Vector3.zero;
            }
        }

        private static GameObject ResolvePlayer(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;

            if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return null;

            return client.PlayerObject != null ? client.PlayerObject.gameObject : null;
        }
    }
}
