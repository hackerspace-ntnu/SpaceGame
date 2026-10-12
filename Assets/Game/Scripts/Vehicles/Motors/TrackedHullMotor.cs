// Drives a big transform-driven hull -- a tracked dune barge -- for an NPC brain. AgentController (and
// so FormationModule) hands it a MoveIntent; it follows the NavMesh route there with NavPathFollower,
// turns on the spot at turnRate, eases up to a slow cruiseSpeed and brakes into the stop point
// (TrackedHullDrive), and sits on the ground under its footprint: height from a ground probe, and
// optionally the pitch/roll of that ground, smoothed.
//
// No Rigidbody physics. The pose is written straight to the transform, AND to the Rigidbody when the
// hull has a (kinematic) one, as VesselPilot places its hull: Physics.autoSyncTransforms is off
// project-wide, so a transform-only write would leave the colliders -- the deck people stand on -- a
// step behind the hull everyone sees. The step runs in FixedUpdate, before WalkerPlatformCarrier
// (order 200) measures the hull's motion on the same physics clock and carries whoever is aboard.
//
// The ground probe is PhysicsGroundProbe: it looks through the hull's own hierarchy (and so everyone
// seated in it) and never takes a collider with a Rigidbody as ground, kinematic or not. The deck
// carries people, and a hull that read its passengers as ground would climb on them into the sky.
// A miss is "not loaded yet", never a height: the hull holds the last ground it had.
//
// It is a simulation driver: NetAuthority discovers it (it is an IMovementMotor) and disables it on
// every machine that does not own the hull; the hull's ClientNetworkTransform carries the pose there.
using SpaceGame.Agents;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Vehicles.Motors
{
    [DefaultExecutionOrder(-100)]
    public sealed class TrackedHullMotor : MonoBehaviour, IMovementMotor, ITeleportAware
    {
        // Floors for OnValidate: a stop distance of nothing is never reached, a footprint needs a
        // sample at each end of both axes to have a width and a length, and past 90 degrees is upside down.
        private const float MinStopDistance = 0.1f;
        private const int MinFootprintSamples = 2;
        private const float MaxTiltLimit = 90f;

        [Tooltip("Turn, speed and braking. A barge is slow: it turns on the spot and never hurries.")]
        [SerializeField] private TrackedHullSettings settings = new TrackedHullSettings
        {
            turnRate = 12f, cruiseSpeed = 4f, acceleration = 0.5f, braking = 1f, alignAngle = 45f,
        };

        [Header("NPC route following")]
        [Tooltip("How the NavMesh route is followed. Corners are rounded wide: a 35 m hull turning on a point " +
                 "still sweeps its whole length round.")]
        [SerializeField] private NavPathFollowerSettings route = new NavPathFollowerSettings(
            repathInterval: 1f, repathTolerance: 4f, cornerArriveRadius: 10f, navMeshSampleDistance: 20f);
        [Tooltip("Stop distance (m) used when a MoveIntent does not give one.")]
        [SerializeField] private float defaultStopDistance = 8f;

        [Header("Ground")]
        [Tooltip("Height of the pivot above the ground it rests on, m.")]
        [SerializeField] private float rideHeight;
        [Tooltip("Centre of the footprint that rests on the ground, in the hull's local x/z (m).")]
        [SerializeField] private Vector2 footprintCenter;
        [Tooltip("Width (x) and length (z) of the footprint that rests on the ground, m.")]
        [SerializeField] private Vector2 footprintSize = new Vector2(12f, 30f);
        [Tooltip("Ground samples across (x) and along (z) the footprint, corners included.")]
        [SerializeField] private Vector2Int footprintSamples = new Vector2Int(3, 3);
        [Tooltip("How far above the hull's current height each ground ray starts, m. Must clear any rise the " +
                 "footprint can meet in one step; the rays look through the hull itself.")]
        [SerializeField] private float probeLift = 20f;
        [SerializeField] private PhysicsGroundProbe groundProbe = new PhysicsGroundProbe();

        [Header("Attitude")]
        [Tooltip("Pitch and roll to the ground under the footprint. Off holds the hull level, resting on the " +
                 "highest ground under it.")]
        [SerializeField] private bool followGround = true;
        [Tooltip("Largest pitch or roll the ground may give the hull, degrees.")]
        [SerializeField] private float maxTilt = 12f;
        [Tooltip("How fast the pivot height closes on the ground's, 1/s. 0 snaps.")]
        [SerializeField] private float heightSharpness = 4f;
        [Tooltip("How fast the pitch/roll closes on the ground's, 1/s. 0 snaps.")]
        [SerializeField] private float tiltSharpness = 2f;

        private readonly MotorOrders orders = new MotorOrders();

        private Rigidbody body;
        private float speed;
        private float headingDeg;
        private Quaternion tilt = Quaternion.identity;
        private float groundHeight;
        private bool hasGround;
        private Vector3[] samples;
        private bool probeBound;

        // Built on first use from the serialized fields, so the values a builder or the Inspector wrote
        // are the ones it steers by, and a Tick before Awake (AddComponent in an EditMode test raises
        // none) does not throw. OnValidate drops it so an Inspector edit is picked up.
        private NavPathFollower follower;

        private NavPathFollower Follower => follower ??= new NavPathFollower(route);

        /// <summary>Speed along the heading, m/s. Never negative: a barge does not reverse.</summary>
        public float Speed => speed;

        public Vector3 Velocity => Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward * speed;
        public float TopSpeed => settings.cruiseSpeed;
        public bool IsImmobile => false;
        public bool HasReachedDestination => orders.HasReached(transform.position, defaultStopDistance);
        public Vector3? CurrentDestination => orders.Destination;

        private void Awake() => body = GetComponent<Rigidbody>();

        // Also covers authority coming back: NetAuthority disables this motor on a machine that does
        // not own the hull, and meanwhile the replicated transform moved and turned it. State kept from
        // before would snap the hull back on the first step.
        private void OnEnable() => Rebaseline();

        public void Tick(in MoveIntent intent, float deltaTime) => orders.Take(intent);

        public void ForceStop()
        {
            speed = 0f;
            orders.Clear();
        }

        public void NudgeDestination(Vector3 offset) => orders.Nudge(offset);

        public void SuggestDestination(Vector3 position) => orders.Suggest(position);

        /// <summary>
        /// Bring the orders and the pose state through a teleport (a portal, a respawn, a save load).
        /// The route is dropped rather than rebased: its corners came from the NavMesh around the old
        /// position. The ground is re-read, so the hull settles where it landed instead of easing in
        /// from the height it left.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            orders.Rebase(move);
            follower?.Clear();
            Rebaseline();
        }

        private void FixedUpdate() => Step(Time.fixedDeltaTime);

        /// <summary>One drive step: steer, move, settle on the ground, write the pose. FixedUpdate calls it.</summary>
        public void Step(float deltaTime)
        {
            if (!(deltaTime > 0f)) return;

            Vector3 position = transform.position;
            float wanted = 0f;

            if (orders.Destination.HasValue)
            {
                float remaining = orders.Remaining(position, defaultStopDistance);
                if (remaining > 0f)
                {
                    Vector3 steerAt = Follower.SteerTarget(position, orders.Destination.Value, deltaTime);
                    float bearing = TrackedHullDrive.Bearing(position, steerAt, headingDeg);
                    wanted = TrackedHullDrive.WantedSpeed(remaining, Mathf.DeltaAngle(headingDeg, bearing),
                                                          orders.SpeedMultiplier, settings);
                    headingDeg = TrackedHullDrive.NextHeading(headingDeg, bearing, deltaTime, settings);
                }
            }
            else if (orders.FacePoint.HasValue)
            {
                float bearing = TrackedHullDrive.Bearing(position, orders.FacePoint.Value, headingDeg);
                headingDeg = TrackedHullDrive.NextHeading(headingDeg, bearing, deltaTime, settings);
            }

            speed = TrackedHullDrive.NextSpeed(speed, wanted, deltaTime, settings);

            Quaternion yaw = Quaternion.Euler(0f, headingDeg, 0f);
            position += yaw * Vector3.forward * (speed * deltaTime);

            Settle(ref position, yaw, deltaTime);
            Place(position, yaw * tilt);
        }

        // Height and attitude from the ground under the footprint at the new position. With no ground
        // at all the hull keeps the height and tilt it had.
        private void Settle(ref Vector3 position, Quaternion yaw, float deltaTime)
        {
            if (TryReadFooting(position, yaw, out HullFooting footing))
            {
                float heightStep = hasGround ? TrackedHullDrive.SmoothFactor(heightSharpness, deltaTime) : 1f;
                float tiltStep = hasGround ? TrackedHullDrive.SmoothFactor(tiltSharpness, deltaTime) : 1f;
                groundHeight = Mathf.Lerp(groundHeight, footing.Height, heightStep);
                tilt = Quaternion.Slerp(tilt, footing.Tilt, tiltStep);
                hasGround = true;
            }

            if (hasGround)
                position.y = groundHeight + rideHeight;
        }

        private bool TryReadFooting(Vector3 position, Quaternion yaw, out HullFooting footing)
        {
            if (!probeBound)
            {
                groundProbe.IgnoreHierarchy(transform);
                probeBound = true;
            }

            int columns = Mathf.Max(MinFootprintSamples, footprintSamples.x);
            int rows = Mathf.Max(MinFootprintSamples, footprintSamples.y);
            if (samples == null || samples.Length != columns * rows)
                samples = new Vector3[columns * rows];

            int found = 0;
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    var local = new Vector3(
                        footprintCenter.x + footprintSize.x * (c / (columns - 1f) - 0.5f),
                        0f,
                        footprintCenter.y + footprintSize.y * (r / (rows - 1f) - 0.5f));
                    Vector3 column = position + yaw * local;
                    column.y = position.y + probeLift;
                    if (!groundProbe.TryGroundBelow(column, out Vector3 ground)) continue;
                    samples[found++] = new Vector3(local.x, ground.y, local.z);
                }
            }

            return TrackedHullDrive.TryFooting(samples, found, followGround, maxTilt, out footing);
        }

        // Transform AND body: see the header.
        private void Place(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (body == null) return;
            body.position = position;
            body.rotation = rotation;
        }

        private void Rebaseline()
        {
            speed = 0f;
            // The heading is the nose's compass bearing, not eulerAngles.y: a pitched AND rolled hull's
            // Euler decomposition leaks some of the tilt into the yaw. Whatever is left is the tilt.
            Quaternion rotation = transform.rotation;
            headingDeg = TrackedHullDrive.Bearing(Vector3.zero, rotation * Vector3.forward, headingDeg);
            tilt = Quaternion.Inverse(Quaternion.Euler(0f, headingDeg, 0f)) * rotation;
            hasGround = false;
        }

        private void OnValidate()
        {
            settings.Validate();
            route.Validate();
            defaultStopDistance = Mathf.Max(MinStopDistance, defaultStopDistance);
            footprintSize = Vector2.Max(footprintSize, Vector2.zero);
            footprintSamples = Vector2Int.Max(footprintSamples, new Vector2Int(MinFootprintSamples, MinFootprintSamples));
            probeLift = Mathf.Max(0f, probeLift);
            maxTilt = Mathf.Clamp(maxTilt, 0f, MaxTiltLimit);
            heightSharpness = Mathf.Max(0f, heightSharpness);
            tiltSharpness = Mathf.Max(0f, tiltSharpness);

            // The follower copies the route settings when built; the next step rebuilds it.
            follower = null;
        }
    }
}
