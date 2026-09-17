// Flies an NPC sky transport: out to its quarry, down onto a landing site (or into a hover over
// open ground), unloads the party, climbs away and goes home.
//
// The rules are pure and tested elsewhere — VesselMission decides what state the run is in,
// VesselFlightMath how the hull moves, LandingSiteFinder where it sets down. This component is the
// scene half: it reads the sensors, ticks the mission, moves the kinematic hull and does the
// unseating.
//
// Server only. The hull's NetworkTransform is server-authoritative, so a client's copy is moved by
// the wire and this does nothing there; seated passengers ride along through the parenting
// VesselSeats set up. Nothing here is saved: the war party that launched the vessel rebuilds it.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VesselSeats))]
    public class VesselPilot : NetworkBehaviour
    {
        [Header("Flight")]
        [SerializeField] private float cruiseSpeed = 28f;
        [SerializeField] private float acceleration = 6f;
        [Tooltip("Yaw rate, and the rate the hull tilts to meet sloped ground, in degrees per second.")]
        [SerializeField] private float turnRate = 45f;
        [Tooltip("Height held above the higher of the ground below and the ground ahead.")]
        [SerializeField] private float cruiseClearance = 45f;
        [Tooltip("The least clearance ever held, whatever cruiseClearance is set to.")]
        [SerializeField] private float minimumClearance = 30f;
        [Tooltip("How far ahead, toward the destination, the ground is read for the cruise altitude.")]
        [SerializeField] private float groundLookAhead = 80f;

        [Header("Hull")]
        [Tooltip("Radius of ground the hull needs to set down on.")]
        [SerializeField] private float footprintRadius = 12f;
        [Tooltip("Where a landed party walks off. Turned to face the quarry on the way down.")]
        [SerializeField] private Transform ramp;
        [Tooltip("Below the hull: where a hovering vessel drops its party.")]
        [SerializeField] private Transform drop;
        [Tooltip("Height above a sloped landing site at which the hull starts tilting to lie along it.")]
        [SerializeField] private float landingTiltHeight = 12f;

        [Header("Drop-off")]
        [SerializeField] private LandingSettings landing = LandingSettings.Default;
        [SerializeField] private VesselMissionSettings missionSettings = VesselMissionSettings.Default;
        [Tooltip("Seconds between checks that the chosen site is still clear while flying to it.")]
        [SerializeField] private float siteCheckInterval = 1f;
        [SerializeField] private PhysicsGroundProbe probe = new PhysicsGroundProbe();

        [Header("Wreck")]
        [Tooltip("How fast a destroyed vessel falls, in m/s².")]
        [SerializeField] private float wreckFallAcceleration = 9.81f;
        [Tooltip("Seconds a destroyed vessel lingers before it is despawned.")]
        [SerializeField] private float wreckDespawnDelay = 10f;

        /// <summary>A passenger just stepped off. Server only.</summary>
        public event Action<GameObject> PassengerUnloaded;

        /// <summary>
        /// The run is over: it reached <see cref="VesselMissionState.Done"/>, or the vessel was shot
        /// down (<see cref="IsWrecked"/>; everyone aboard has already been unloaded). Server only;
        /// raised once per run.
        /// </summary>
        public event Action Finished;

        private VesselSeats seats;
        private HealthComponent health;
        private Rigidbody body;

        private VesselMission mission;
        private Func<Vector3> quarry;
        private Vector3 velocity;
        private Quaternion heading = Quaternion.identity;
        private Quaternion tilt = Quaternion.identity;
        private Vector3 landingNormal = Vector3.up;
        private float siteCheckTimer;
        private bool finished;
        private bool wrecked;
        private float wreckTimer;
        private bool wreckGrounded;

        /// <summary>The run's state; <see cref="VesselMissionState.Done"/> before <see cref="Begin"/> and once finished.</summary>
        public VesselMissionState State => mission?.State ?? VesselMissionState.Done;

        public VesselSeats Seats => seats;

        /// <summary>Shot down: falling, and despawning itself after <see cref="wreckDespawnDelay"/>.</summary>
        public bool IsWrecked => wrecked;

        private void Awake()
        {
            seats = GetComponent<VesselSeats>();
            health = GetComponent<HealthComponent>();
            body = GetComponent<Rigidbody>();
            probe.IgnoreHierarchy(transform);
        }

        private void OnEnable()
        {
            if (health != null) health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDeath -= HandleDeath;
        }

        /// <summary>
        /// Start a drop-off run: seat <paramref name="passengers"/> (anyone already seated here is kept)
        /// and fly to wherever <paramref name="quarry"/> says, returning to <paramref name="home"/>.
        /// The run is done on the way home once every player is beyond <paramref name="despawnDistance"/>
        /// — the caller's own despawn range, so the two never disagree about what is out of sight.
        /// Server only — a client call does nothing.
        /// </summary>
        public void Begin(Vector3 home, Func<Vector3> quarry, IReadOnlyList<GameObject> passengers,
                          float despawnDistance)
        {
            if (!Network.Simulates(this)) return;

            this.quarry = quarry ?? throw new ArgumentNullException(nameof(quarry));
            VesselMissionSettings run = missionSettings;
            run.despawnDistance = despawnDistance;
            mission = new VesselMission(home, run);
            velocity = Vector3.zero;
            heading = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            tilt = Quaternion.identity;
            landingNormal = Vector3.up;
            siteCheckTimer = 0f;
            finished = false;

            if (passengers == null) return;
            foreach (GameObject passenger in passengers)
            {
                if (passenger == null || IsAboard(passenger)) continue;
                if (seats.Seat(passenger) < 0)
                    Debug.LogError($"[VesselPilot] '{name}' has {seats.Capacity} seats and '{passenger.name}' " +
                                   "did not get one. Choose the vessel by its capacity.", this);
            }
        }

        /// <summary>
        /// Put the hull at the height it cruises at toward <paramref name="destination"/>, before it is
        /// network-spawned, so no machine ever sees it climb out of the ground it was placed on.
        /// </summary>
        public void RiseToCruise(Vector3 destination)
        {
            Vector3 position = transform.position;
            position.y = CruiseAltitude(position, destination);
            Place(position, transform.rotation);
        }

        private bool IsAboard(GameObject passenger)
        {
            for (int i = 0; i < seats.Capacity; i++)
                if (seats.OccupantAt(i) == passenger) return true;
            return false;
        }

        private void Update()
        {
            if (!Network.Simulates(this)) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (wrecked)
            {
                TickWreck(dt);
                return;
            }

            if (mission == null || finished) return;

            Vector3 position = transform.position;
            Vector3 quarryPoint = quarry();
            MaintainSite(quarryPoint, position, dt);

            Vector3 destination = DestinationPoint(quarryPoint);
            float cruise = CruiseAltitude(position, destination);
            var sensors = new VesselSensors(FlatDistance(position, destination),
                                            position.y - mission.TargetAltitude(cruise),
                                            seats.Occupied, NearestPlayerDistance(position));

            int release = mission.Tick(sensors, dt);
            for (int i = 0; i < release; i++) UnloadNext();

            if (mission.State == VesselMissionState.Done)
            {
                velocity = Vector3.zero;
                Finish();
                return;
            }

            destination = DestinationPoint(quarryPoint);
            var target = new Vector3(destination.x, mission.TargetAltitude(cruise), destination.z);

            // Over the site before going down to it, so the hull never slants in across the ground.
            if (mission.State == VesselMissionState.Descend &&
                FlatDistance(position, destination) > missionSettings.arrivalTolerance)
                target.y = Mathf.Max(target.y, position.y);
            (Vector3 next, Vector3 nextVelocity) =
                VesselFlightMath.Step(position, velocity, target, cruiseSpeed, acceleration, dt);
            velocity = nextVelocity;

            Steer(next, quarryPoint, dt);
            Place(next, tilt * heading);
        }

        // ── The drop site ───────────────────────────────────────────────────────

        /// <summary>
        /// Choose a site on reaching Approach, and keep checking it is still clear until the hull is
        /// down: another vessel, a camp or a crowd may have moved in since. No site anywhere, first
        /// time or on a re-check, gives up the drop and goes home with the party aboard.
        /// </summary>
        private void MaintainSite(Vector3 quarryPoint, Vector3 position, float dt)
        {
            VesselMissionState state = mission.State;
            if (state == VesselMissionState.Approach && !mission.Site.HasValue)
            {
                ChooseSite(quarryPoint, position);
                return;
            }

            if ((state != VesselMissionState.Approach && state != VesselMissionState.Descend) || !mission.Site.HasValue)
                return;

            siteCheckTimer += dt;
            if (siteCheckTimer < siteCheckInterval) return;
            siteCheckTimer = 0f;

            if (!LandingSiteFinder.IsStillClear(mission.Site.Value, quarryPoint, footprintRadius, landing, probe))
                ChooseSite(quarryPoint, position);
        }

        private void ChooseSite(Vector3 quarryPoint, Vector3 position)
        {
            DropSite? site = LandingSiteFinder.Find(quarryPoint, position, footprintRadius, landing, probe);
            if (!site.HasValue)
            {
                mission.Abort();
                return;
            }

            mission.SetSite(site.Value);
            landingNormal = site.Value.Mode == DropMode.Land
                ? LandingSiteFinder.GroundNormal(site.Value.Point, footprintRadius, probe)
                : Vector3.up;
        }

        private Vector3 DestinationPoint(Vector3 quarryPoint)
        {
            switch (mission.Destination)
            {
                case VesselDestination.Site: return mission.Site.Value.Point;
                case VesselDestination.Home: return mission.Home;
                default: return quarryPoint;
            }
        }

        // ── Flying ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Clearance above the higher of the ground below and the ground ahead. Where neither can be
        /// read (terrain not streamed in under the hull), the current altitude is held.
        /// </summary>
        private float CruiseAltitude(Vector3 position, Vector3 destination)
        {
            Vector3 toward = destination - position;
            toward.y = 0f;
            Vector3 ahead = toward.sqrMagnitude > Vector3.kEpsilon
                ? position + toward.normalized * Mathf.Min(groundLookAhead, toward.magnitude)
                : position;

            bool hasBelow = probe.TryGround(position, out Vector3 below, out _);
            bool hasAhead = probe.TryGround(ahead, out Vector3 aheadGround, out _);
            if (!hasBelow && !hasAhead) return position.y;

            float belowY = hasBelow ? below.y : aheadGround.y;
            float aheadY = hasAhead ? aheadGround.y : below.y;
            return VesselFlightMath.CruiseAltitude(belowY, aheadY, cruiseClearance, minimumClearance);
        }

        /// <summary>
        /// Face the way the hull is going; once going down onto the site, turn the ramp to the quarry
        /// so the party walks off toward the fight, and near a sloped site lie along the ground.
        /// </summary>
        private void Steer(Vector3 next, Vector3 quarryPoint, float dt)
        {
            VesselMissionState state = mission.State;
            bool settling = state == VesselMissionState.Descend || state == VesselMissionState.Unload;

            Vector3 facing = settling ? RampFacing(quarryPoint - next) : velocity;
            heading = VesselFlightMath.TurnToward(heading, facing, turnRate, dt);

            Quaternion tiltTarget = Quaternion.identity;
            if (settling && mission.Site.HasValue && mission.Site.Value.Mode == DropMode.Land)
            {
                float above = next.y - mission.Site.Value.Point.y;
                float blend = 1f - Mathf.Clamp01(above / Mathf.Max(landingTiltHeight, Vector3.kEpsilon));
                Quaternion lieAlong = Quaternion.FromToRotation(Vector3.up, landingNormal);
                tiltTarget = Quaternion.Slerp(Quaternion.identity, lieAlong, blend);
            }
            tilt = Quaternion.RotateTowards(tilt, tiltTarget, turnRate * dt);
        }

        /// <summary>The hull forward that points the ramp along <paramref name="toQuarry"/>.</summary>
        private Vector3 RampFacing(Vector3 toQuarry)
        {
            Vector3 rampLocal = ramp != null ? ramp.localPosition : Vector3.forward;
            rampLocal.y = 0f;
            float rampYaw = Vector3.SignedAngle(Vector3.forward, rampLocal, Vector3.up);
            return Quaternion.AngleAxis(-rampYaw, Vector3.up) * toQuarry;
        }

        // Transform AND body: Physics.autoSyncTransforms is off project-wide, so a transform-only
        // write leaves the colliders a step behind the hull everyone can see.
        private void Place(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (body == null) return;
            body.position = position;
            body.rotation = rotation;
        }

        private static float NearestPlayerDistance(Vector3 position)
        {
            float best = float.PositiveInfinity;
            foreach (PlayerIdentity player in PlayerIdentity.All)
                if (player != null) best = Mathf.Min(best, FlatDistance(position, player.transform.position));
            return best;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ── Unloading ───────────────────────────────────────────────────────────

        private void UnloadNext()
        {
            int seat = seats.FirstOccupiedSeat();
            if (seat < 0) return;

            GameObject passenger = seats.Unseat(seat, UnloadPoint());
            if (passenger != null) PassengerUnloaded?.Invoke(passenger);
        }

        /// <summary>
        /// Where the next passenger stands: on the NavMesh at the ramp's foot when landed, on the
        /// NavMesh under the drop point when hovering. The site's own unload point, found when the site
        /// was chosen, stands in when the hull's actual pose finds no NavMesh — the hull is over that
        /// site whenever it unloads normally. A wreck may be anywhere, so it only ever drops its
        /// passengers straight down (VesselSeats puts them on the nearest NavMesh from there): the
        /// site could be a kilometre away.
        /// </summary>
        private Vector3 UnloadPoint()
        {
            DropSite? site = wrecked ? null : mission?.Site;
            bool landed = site.HasValue && site.Value.Mode == DropMode.Land;

            if (landed && ramp != null && probe.TryNavMesh(ramp.position, landing.navMeshReach, out Vector3 rampFoot))
                return rampFoot;

            Vector3 dropPoint = drop != null ? drop.position : transform.position;
            bool grounded = probe.TryGround(dropPoint, out Vector3 ground, out _);
            if (grounded && probe.TryNavMesh(ground, landing.navMeshReach, out Vector3 below)) return below;
            if (site.HasValue) return site.Value.UnloadPoint;

            Debug.LogWarning($"[VesselPilot] '{name}' found no NavMesh within {landing.navMeshReach} m under its " +
                             "drop point; the passenger is put down off the NavMesh and may not be able to walk.", this);
            return grounded ? ground : dropPoint;
        }

        /// <summary>
        /// Everyone off at once, straight down (see <see cref="UnloadPoint"/>). Bounded by the seat
        /// count: an unseat that is refused — a hull already deactivating — must not loop forever.
        /// </summary>
        private void DropEveryone()
        {
            for (int i = 0; i < seats.Capacity && seats.FirstOccupiedSeat() >= 0; i++)
                UnloadNext();
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            Finished?.Invoke();
        }

        /// <summary>
        /// About to be despawned — by the sim folding a war party, by its own wreck timer, by the
        /// session ending. Netcode lifts seated passengers to the scene root on the way out; unseating
        /// them first puts them on the ground as working NPCs instead of leaving them cargo nobody
        /// carries. Where the hull can no longer reparent anybody, VesselSeats abandons them in place.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            if (!Network.Simulates(this)) return;

            if (gameObject.activeInHierarchy)
            {
                wrecked = true;
                DropEveryone();
            }
            seats.AbandonAll();
        }

        // ── Wreck ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Shot down: the pilot is gone, everyone aboard drops to the ground below at once, and the
        /// hull falls and is cleared away after <see cref="wreckDespawnDelay"/>.
        /// </summary>
        private void HandleDeath()
        {
            if (wrecked || health.IsRestoring || !Network.Simulates(this)) return;

            wrecked = true;
            wreckTimer = 0f;
            wreckGrounded = false;
            velocity = new Vector3(0f, Mathf.Min(0f, velocity.y), 0f);

            DropEveryone();
            Finish();
        }

        private void TickWreck(float dt)
        {
            wreckTimer += dt;
            if (wreckTimer >= wreckDespawnDelay)
            {
                Despawn();
                return;
            }

            if (wreckGrounded) return;

            // Only ground BELOW the hull: a wreck under the sky city would otherwise find the city
            // overhead and hang in mid-air at its keel.
            Vector3 position = transform.position;
            velocity.y -= wreckFallAcceleration * dt;
            Vector3 next = position + velocity * dt;
            if (probe.TryGroundBelow(position, out Vector3 ground) && next.y <= ground.y)
            {
                next.y = ground.y;
                wreckGrounded = true;
            }
            Place(next, transform.rotation);
        }

        private void Despawn()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(destroy: true);
            else
                Destroy(gameObject);
        }

        private void OnValidate()
        {
            cruiseSpeed = Mathf.Max(0.1f, cruiseSpeed);
            acceleration = Mathf.Max(0.1f, acceleration);
            minimumClearance = Mathf.Max(0f, minimumClearance);
            footprintRadius = Mathf.Max(0.5f, footprintRadius);
            siteCheckInterval = Mathf.Max(0.05f, siteCheckInterval);
        }
    }
}
