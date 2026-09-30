// The Rigidbody shell that drives a monowheel. Two channels feed it the same two numbers --
// throttle and steer -- and MonowheelDrive turns those into speed, heading and grip:
//   • a rider through SteerModule (IRiderControllable), stick straight to throttle/steer;
//   • an NPC brain through AgentController (IMovementMotor), whose destination is followed along a
//     NavMesh route by NavPathFollower and turned into throttle/steer by MonowheelDrive.NpcInput.
// So a player and a Strider scout ride exactly the same vehicle, and nothing here is NPC-only
// physics.
//
// The shape is RigidbodyMotor's: input is latched on the render loop and consumed in FixedUpdate on
// the physics clock, the rider's frame blocks the AI's (the IRiderControllable contract), yaw goes
// through body.MoveRotation and never transform.rotation, and speed is this motor's own number
// rather than read back from the body, because ground friction drains linearVelocity between steps.
//
// It is a simulation driver: NetAuthority discovers it (it is an IMovementMotor) and disables it on
// every machine that does not own the monowheel, freezing the body there. The lean the art shows
// is MonowheelLean's, which runs on every machine from the replicated transform.
using SpaceGame.Agents;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MonowheelMotor : MonoBehaviour, IMovementMotor, IRiderControllable, ITeleportAware
    {
        [Tooltip("The handling numbers. MonowheelLean reads the same set for the chassis roll. " +
                 "Placeholder values until the drive spike tunes them.")]
        [SerializeField] private MonowheelDriveSettings settings = new MonowheelDriveSettings
        {
            topSpeed = 20f, reverseSpeed = 4f, acceleration = 6f, braking = 12f, coastDrag = 2f,
            turnRate = 90f, turnRateAtTop = 25f, lateralGrip = 4f,
            maxLean = 25f, leanPerLateralAccel = 2.5f,
            cornerSlowAngle = 90f, cornerMinSpeedFraction = 0.3f, alignAngle = 90f,
        };

        [Header("NPC route following")]
        [Tooltip("How the NavMesh route is followed. Corners are rounded wide: a wheel at speed cannot turn on a point.")]
        [SerializeField] private NavPathFollowerSettings route = new NavPathFollowerSettings(
            repathInterval: 0.5f, repathTolerance: 3f, cornerArriveRadius: 8f, navMeshSampleDistance: 20f);
        [Tooltip("Stop distance (m) used when a MoveIntent does not give one.")]
        [SerializeField] private float defaultStopDistance = 6f;

        private Rigidbody body;
        private float speed;
        private float headingDeg;

        private readonly MotorOrders orders = new MotorOrders();

        // Built on first use from the serialized fields, so the values a builder or the Inspector
        // wrote are the ones it steers by, and a Tick before Awake (AddComponent in an EditMode test
        // raises none) does not throw. OnValidate drops it so an Inspector edit is picked up.
        private NavPathFollower follower;

        private NavPathFollower Follower => follower ??= new NavPathFollower(route);

        // Stamped inside ApplyRiderInput so the same frame's Tick skips the MoveIntent -- the
        // IRiderControllable contract that keeps the AI channel from fighting the rider.
        private int riderDriveFrame = -1;

        // Latest rider input, latched at render rate and consumed in FixedUpdate. Latched, not
        // cleared: the rider's stick is a held state, and Update may run several times between
        // physics steps or not at all.
        private RiderInput pendingRiderInput;
        private bool hasPendingRiderInput;

        /// <summary>Signed speed along the heading, m/s. Negative is reversing.</summary>
        public float Speed => speed;

        /// <summary>The handling numbers, shared with MonowheelLean.</summary>
        public MonowheelDriveSettings Settings => settings;

        public Vector3 Velocity => body ? body.linearVelocity : Vector3.zero;
        public float TopSpeed => settings.topSpeed;
        public bool IsImmobile => false;

        public bool HasReachedDestination => orders.HasReached(transform.position, defaultStopDistance);

        public Vector3? CurrentDestination => orders.Destination;

        private void Awake() => body = GetComponent<Rigidbody>();

        // Also covers authority coming back: NetAuthority disables this motor on a machine that
        // does not own the monowheel, and while it was off the replicated transform turned it. A
        // heading kept from before would snap the vehicle back on the first step.
        private void OnEnable()
        {
            headingDeg = transform.eulerAngles.y;
            speed = 0f;
        }

        public void Tick(in MoveIntent intent, float deltaTime)
        {
            if (riderDriveFrame == Time.frameCount)
                return;

            // The rider let go: drop the latch, or FixedUpdate would keep driving the last stick.
            hasPendingRiderInput = false;
            orders.Take(intent);
        }

        // Rider input is only latched here; the body is written in FixedUpdate. See RigidbodyMotor
        // for why writing a Rigidbody from Update shakes a follow camera.
        public void ApplyRiderInput(in RiderInput input, float deltaTime)
        {
            riderDriveFrame = Time.frameCount;
            pendingRiderInput = input;
            hasPendingRiderInput = true;
        }

        private void FixedUpdate()
        {
            // A kinematic body is being posed by something else (a remote copy, a seat, a load) and
            // cannot take a velocity.
            if (!body || body.isKinematic)
                return;

            float dt = Time.fixedDeltaTime;
            (float throttle, float steer) = ChooseInput(dt);

            speed = MonowheelDrive.NextSpeed(speed, throttle, dt, settings);
            headingDeg = MonowheelDrive.NextHeading(headingDeg, steer, speed, dt, settings);

            // Upright + yaw, so a chassis bumped into a tilt sheds it instead of keeping it forever.
            // MoveRotation, never transform.rotation: assigning a non-kinematic body's transform
            // discards the pose interpolation blends from (RigidbodyMotor's "choppy vehicle" bug).
            Quaternion facing = Quaternion.Euler(0f, headingDeg, 0f);
            body.MoveRotation(facing);

            // Drive along the heading just asked for, not transform.forward, which is a step behind
            // until MoveRotation lands.
            body.linearVelocity = MonowheelDrive.GripVelocity(body.linearVelocity, facing * Vector3.forward,
                                                              speed, dt, settings);
        }

        private (float throttle, float steer) ChooseInput(float dt)
        {
            if (hasPendingRiderInput)
            {
                // A rider drives manually, so any destination from before they took over is stale.
                orders.Clear();
                return (pendingRiderInput.Move.y, pendingRiderInput.Move.x);
            }

            Vector3 position = body.position;

            if (orders.Destination.HasValue)
            {
                Vector3 steerAt = Follower.SteerTarget(position, orders.Destination.Value, dt);
                float turnAhead = Follower.TryGetCornerAfter(position, out Vector3 after)
                    ? Vector3.Angle(Flat(steerAt - position), Flat(after - steerAt))
                    : 0f;
                float remaining = orders.Remaining(position, defaultStopDistance);
                float wanted = MonowheelDrive.WantedSpeed(remaining, turnAhead, orders.SpeedMultiplier, settings);
                return MonowheelDrive.NpcInput(headingDeg, speed, position, steerAt, wanted, settings);
            }

            // Wanting zero speed while aiming at the face point brakes and turns toward it -- a wheel
            // cannot strafe, so turning on the spot is all facing means here.
            if (orders.FacePoint.HasValue)
                return MonowheelDrive.NpcInput(headingDeg, speed, position, orders.FacePoint.Value, 0f, settings);

            return (speed > 0f ? -1f : 0f, 0f);
        }

        public void ForceStop()
        {
            speed = 0f;
            orders.Clear();
            // The latch too, or the next physics step re-applies the last stick and undoes the stop.
            hasPendingRiderInput = false;
            // A kinematic body has no velocity to clear, and Unity warns on every write: that is a
            // remote copy (NetAuthority freezes it), where a wreck's replicated death lands too.
            if (!body || body.isKinematic)
                return;
            Vector3 v = body.linearVelocity;
            v.x = 0f;
            v.z = 0f;
            body.linearVelocity = v;
            body.angularVelocity = Vector3.zero;
        }

        public void NudgeDestination(Vector3 offset) => orders.Nudge(offset);

        // A suggestion carries no stop distance; the last order's would not fit it.
        public void SuggestDestination(Vector3 position) => orders.Suggest(position);

        /// <summary>
        /// Bring the destination and heading through a teleport (a portal, a respawn, a save load).
        /// The route is dropped rather than rebased: its corners came from the NavMesh around the
        /// old position, and the next step asks for a fresh one.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            orders.Rebase(move);
            follower?.Clear();
            headingDeg = transform.eulerAngles.y;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void OnValidate()
        {
            route.Validate();
            defaultStopDistance = Mathf.Max(0.1f, defaultStopDistance);

            // The follower copies the route settings when built; the next step rebuilds it.
            follower = null;
        }
    }
}
