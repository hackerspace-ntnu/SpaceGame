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
                 "ship in a formation, which is what makes them land together.")]
        [SerializeField] private float descentDuration = 26f;

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

        [Tooltip("How far the hull sits above the measured ground once it has stopped.")]
        [SerializeField] private float wreckGroundClearance = 1.2f;

        [Tooltip("How long the wreck sits still, with the screen already black, before players are " +
                 "let out of their seats. Covers the release so nobody watches their own body be " +
                 "handed back its physics.")]
        [SerializeField] private float releaseDelay = 1.6f;

        /// <summary>Every hull on its way down, by team. A story world files its one under -1.</summary>
        private readonly Dictionary<int, ArrivalFlight> flights = new();

        /// <summary>How many clients have been put in a seat, for the launch gate.</summary>
        private int seatedClients;

        /// <summary>How many hulls are mid-descent, so the release waits for the last of them.</summary>
        private int descending;

        /// <summary>True once the launch gate is counting down, so it is only ever started once.</summary>
        private bool launching;

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
        // raises this wherever it seats the local player, which is the only moment that is true on
        // exactly the machines that need it.
        private void OnEnable() => SeatedRider.LocalPlayerSeated += PlayLocalCutscene;

        private void OnDisable() => SeatedRider.LocalPlayerSeated -= PlayLocalCutscene;

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
            // not used here: the seat pose is owner-authoritative and gets written by HoldSeats on
            // the owning machine, which is not necessarily this one.
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

            if (!ShipGrounding.TryResolveGround(new Vector2(impactPoint.x, impactPoint.z), probeHeight,
                                                out float groundY))
            {
                // NOT fatal. In a streamed world this means the chunk under the impact point has not
                // loaded yet, and the only correct response is to wait and ask again — the contract
                // ShipGrounding documents.
                return null;
            }

            ArrivalPath storyPath = path;
            storyPath.ImpactPosition = new Vector3(impactPoint.x, groundY + wreckGroundClearance,
                                                   impactPoint.z);

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
                StartCoroutine(FlyDescent(flight));
            }

            while (descending > 0)
                yield return null;

            // Held open until the cutscene's own blackout has covered the release, so nobody watches
            // their own body drop out of its seat and take its weight back.
            yield return new WaitForSeconds(releaseDelay);

            foreach (ArrivalFlight flight in flights.Values)
                if (flight.IsAlive)
                    flight.Seating.ReleaseAll();

            HasArrived = true;
            IsRunning = false;
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
            float elapsed = 0f;

            while (elapsed < descentDuration)
            {
                elapsed += Time.deltaTime;

                ArrivalTrajectory.Evaluate(elapsed / descentDuration, flightPath,
                                           out Vector3 position, out Quaternion rotation);

                flight.Ship.transform.SetPositionAndRotation(position, rotation);
                yield return null;
            }

            // Landed on the authored pose exactly, rather than wherever the last frame's delta time
            // happened to leave it. The wreck is persisted from here, so "close" would be a hull
            // permanently buried or hovering.
            ArrivalTrajectory.Evaluate(1f, flightPath, out Vector3 impact, out Quaternion impactRotation);
            flight.Ship.transform.SetPositionAndRotation(impact, impactRotation);

            RestoreHull(hull);

            descending--;
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

            cutscene.Configure(descentDuration);
            CutsceneDirector.Instance.Play(cutscene);
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

                state.Body.isKinematic = true;
                state.Body.interpolation = RigidbodyInterpolation.None;
                state.Body.linearVelocity = Vector3.zero;
                state.Body.angularVelocity = Vector3.zero;
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
            state.Body.linearVelocity = Vector3.zero;
            state.Body.angularVelocity = Vector3.zero;
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
