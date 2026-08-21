// Rigidbody-backed motor for ground-effect craft: a hull that rides a fixed clearance over whatever
// is below it and cannot leave it.
//
// This is deliberately not FlyingRigidbodyMotor with a low ceiling. A flying motor gives the pilot
// the vertical axis and lets the craft go wherever it is pointed; here the vertical axis does not
// exist at all. The pilot gets throttle and steering, and the height is a servo onto the ground the
// craft is passing over — climb it, hold it, never leave it. So there is no altitude hold, no
// gravity mode, and no vertical input to interpret.
//
// The one thing that stops it being a flying machine by the back door is maxClimbRate: the servo can
// only pull the hull up so fast, so a dune is followed and a wall is not. The craft meets the wall
// and the collision stands, rather than levitating over it.
//
// Every write to the body happens in FixedUpdate. Rigidbody velocity and MoveRotation are only
// meaningful per physics step; driving them from the render loop makes the per-step advance uneven,
// which a follow camera turns straight into shake. Tick() therefore only records what the AI channel
// asked for — see FlyingRigidbodyMotor.ApplyRiderInput for the long-form version of that reasoning.
using UnityEngine;

namespace SpaceGame.Agents
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public class HoverRigidbodyMotor : MonoBehaviour, IMovementMotor, IRiderControllable
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;

        [Header("Speeds")]
        [SerializeField, Min(0.01f)] private float maxSpeed = 20f;
        [SerializeField, Min(0.1f)] private float acceleration = 18f;
        [SerializeField, Min(0.1f)] private float deceleration = 14f;

        [Header("Facing")]
        [Tooltip("Degrees/sec the craft rotates to face its direction of travel while the AI is driving.")]
        [SerializeField, Min(0.01f)] private float faceRotateSpeed = 2.5f;
        [Tooltip("Yaw rate in degrees/sec while the rider is steering.")]
        [SerializeField, Min(1f)] private float riderTurnSpeed = 90f;

        [Header("Hover")]
        [Tooltip("How far above the ground this transform's origin rides, in metres. Measure it so " +
                 "the LOWEST part of the craft still clears — on a machine that drops its engine " +
                 "pods when deployed, that is the pods, not the belly.")]
        [SerializeField, Min(0f)] private float rideHeight = 0.5f;

        [Tooltip("How hard the craft pulls back to its ride height: vertical m/s per metre of error. " +
                 "Higher sticks to the ground more tightly and rides more harshly.")]
        [SerializeField, Min(0.1f)] private float heightGain = 4f;

        [Tooltip("Steepest slope the craft can follow, in degrees. This is what keeps it from " +
                 "flying: a gentler slope is climbed, and anything steeper outruns the lift so the " +
                 "hull meets it instead.")]
        [SerializeField, Range(1f, 80f)] private float maxFollowGrade = 35f;

        [Tooltip("Climb rate available at a standstill, m/s. The grade above is a ratio, so without " +
                 "this a stationary craft could never rise off ground that came up underneath it.")]
        [SerializeField, Min(0.1f)] private float minClimbRate = 3f;

        [Tooltip("Ceiling on the descent rate, m/s, when the ground drops away underneath.")]
        [SerializeField, Min(0.1f)] private float maxSinkRate = 8f;

        [SerializeField] private HoverGroundSensor groundSensor = new HoverGroundSensor();

        // What the AI channel last asked for. Applied on the physics clock, not when it arrives.
        private MoveIntent pendingIntent = MoveIntent.Idle();
        private Vector3? currentDestination;
        private float stopDistance = 0.5f;

        // Latest rider input, latched in Update and consumed in FixedUpdate. Latched rather than
        // cleared: Update may run several times between physics steps or not at all, and a held
        // stick is a state, not an event.
        private RiderInput pendingRiderInput;
        private bool hasPendingRiderInput;
        private int riderDriveFrame = -1;

        // The craft's heading, owned outright and rewritten every physics step. Not read back off
        // the transform: MoveRotation defers the rotation to the next step, so the transform stays
        // stale for the rest of the frame and steering increments made in between would be dropped.
        //
        // Holding it every step, rather than only on the steps something is steering, is what keeps
        // the hull straight and level. A craft left alone with a contact against a dune picks up
        // rotation from somewhere every time, and there is no frame on which that is wanted: this
        // hull is walked around inside and flies level by design.
        private float heading;
        private bool headingValid;

        // Throttle is tracked here too, and as a scalar rather than a velocity vector. Reading it
        // back off the body feeds the body's damping into the next ramp step as if the craft had
        // genuinely slowed, so acceleration only has to out-run drag rather than reach maxSpeed.
        private float riderForwardSpeed;
        private bool riderSpeedValid;

        public Vector3 Velocity => body ? body.linearVelocity : Vector3.zero;

        /// <summary>Ground clearance the craft is holding, in metres.</summary>
        public float RideHeight
        {
            get => rideHeight;
            set => rideHeight = Mathf.Max(0f, value);
        }

        /// <summary>True while the craft is over ground it can actually measure.</summary>
        public bool HasGround => groundSensor.HasGround;

        public bool IsImmobile
        {
            get
            {
                if (!body)
                    return true;
                Vector3 v = body.linearVelocity;
                v.y = 0f; // the hover servo is always doing something; it is not motion
                return v.sqrMagnitude <= 0.04f;
            }
        }

        public bool HasReachedDestination
        {
            get
            {
                if (!currentDestination.HasValue)
                    return true;
                Vector3 diff = currentDestination.Value - transform.position;
                diff.y = 0f;
                return diff.sqrMagnitude <= stopDistance * stopDistance;
            }
        }

        public Vector3? CurrentDestination => currentDestination;

        private void Awake()
        {
            if (!body)
                body = GetComponent<Rigidbody>();

            // The servo owns the vertical axis outright, so gravity would only be a force to cancel
            // every step. Unlike a flying craft there is no parked state where letting go is right:
            // a hovercraft with nobody aboard still hovers.
            if (body)
                body.useGravity = false;

            groundSensor.Initialize(transform);
        }

        // ─────────── AI channel ───────────
        public void Tick(in MoveIntent intent, float deltaTime)
        {
            // Rider owns the craft this frame; their input is already latched.
            if (riderDriveFrame == Time.frameCount)
                return;

            // Rider released — re-seed the throttle from the body next time someone takes the
            // controls, so AI movement in between is not snapped away on re-mount, and drop the
            // latch or FixedUpdate would keep flying the last stick position for ever. The heading
            // is deliberately not reset: it is the same heading whoever is steering.
            riderSpeedValid = false;
            hasPendingRiderInput = false;

            pendingIntent = intent;
        }

        public void ForceStop()
        {
            pendingIntent = MoveIntent.Idle();
            currentDestination = null;
            riderForwardSpeed = 0f;
            riderSpeedValid = false;
            hasPendingRiderInput = false;

            if (!body)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        public void NudgeDestination(Vector3 offset)
        {
            if (!currentDestination.HasValue)
                return;
            currentDestination = currentDestination.Value + offset;
        }

        public void SuggestDestination(Vector3 position)
        {
            pendingIntent = MoveIntent.MoveTo(position, stopDistance);
        }

        // ─────────── Rider channel ───────────
        public void ApplyRiderInput(in RiderInput input, float deltaTime)
        {
            riderDriveFrame = Time.frameCount;
            pendingRiderInput = input;
            hasPendingRiderInput = true;
            // input.Vertical is deliberately ignored — this craft has no vertical axis.
        }

        // ─────────── Physics ───────────
        private void FixedUpdate()
        {
            if (!body)
                return;

            float deltaTime = Time.fixedDeltaTime;

            if (!headingValid)
            {
                heading = body.rotation.eulerAngles.y;
                headingValid = true;
            }

            Vector3 horizontal = hasPendingRiderInput
                ? DriveFromRider(pendingRiderInput, deltaTime)
                : DriveFromIntent(pendingIntent, deltaTime);

            body.linearVelocity = horizontal + Vector3.up * HoverSpeed(deltaTime);

            // Attitude is written unconditionally, whether or not anything steered this step: yaw
            // from the heading above, pitch and roll flat.
            body.MoveRotation(Quaternion.Euler(0f, heading, 0f));
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, deceleration * deltaTime);
        }

        private Vector3 DriveFromRider(in RiderInput input, float deltaTime)
        {
            currentDestination = null;
            heading += input.Move.x * riderTurnSpeed * deltaTime;

            // Thrust along the heading just asked for, not transform.forward — that is still a
            // physics step behind until MoveRotation lands. Steering then redirects thrust rather
            // than scrubbing speed off it.
            Vector3 forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;

            if (!riderSpeedValid)
            {
                riderForwardSpeed = Vector3.Dot(body.linearVelocity, forward);
                riderSpeedValid = true;
            }

            float throttle = input.Move.y;
            float ramp = (Mathf.Abs(throttle) > 0.01f ? acceleration : deceleration) * deltaTime;
            riderForwardSpeed = Mathf.MoveTowards(riderForwardSpeed, throttle * maxSpeed, ramp);

            return forward * riderForwardSpeed;
        }

        private Vector3 DriveFromIntent(in MoveIntent intent, float deltaTime)
        {
            if (intent.Type == AgentIntentType.StopAndFacePosition)
            {
                currentDestination = null;
                FaceDirection(intent.FacePosition - body.position, deltaTime);
                return Decelerated(deltaTime);
            }

            if (intent.Type != AgentIntentType.MoveToPosition)
            {
                currentDestination = null;
                return Decelerated(deltaTime);
            }

            currentDestination = intent.TargetPosition;
            stopDistance = Mathf.Max(0.1f, intent.StopDistance);

            // Flattened: the destination's height is the servo's business, not the throttle's. An AI
            // handed a target on a clifftop drives at the cliff, exactly as a pilot would.
            Vector3 toTarget = intent.TargetPosition - body.position;
            toTarget.y = 0f;

            float distance = toTarget.magnitude;
            if (distance <= stopDistance)
                return Decelerated(deltaTime);

            Vector3 moveDirection = toTarget / distance;
            Vector3 desired = moveDirection * (maxSpeed * Mathf.Max(0.01f, intent.SpeedMultiplier));

            FaceDirection(intent.OverrideFacingDirection ? intent.FacingDirection : moveDirection, deltaTime);

            return Vector3.MoveTowards(Horizontal(body.linearVelocity), desired, acceleration * deltaTime);
        }

        private Vector3 Decelerated(float deltaTime)
        {
            return Vector3.MoveTowards(Horizontal(body.linearVelocity), Vector3.zero, deceleration * deltaTime);
        }

        /// <summary>Vertical speed that carries the craft toward its ride height this step.</summary>
        private float HoverSpeed(float deltaTime)
        {
            if (!groundSensor.TrySampleGroundY(body.position, deltaTime, out float groundY))
                return 0f; // nothing anywhere — a chunk still streaming in. Hold, do not drop.

            // Match the slope first, then correct what is left over. Without the feed-forward term
            // the servo can only climb by being below where it wants to be, so a long hull drives
            // its own nose into every hill it tries to go up — measured at three metres per second
            // up a twenty degree slope it should have taken at full throttle.
            float error = groundY + rideHeight - body.position.y;
            return Mathf.Clamp(groundSensor.GroundRiseRate + error * heightGain, -maxSinkRate, ClimbCap());
        }

        // The climb ceiling is a gradient, not a fixed rate: what a craft has to out-climb to follow
        // rising ground is its own forward speed times the slope, so a cap in m/s alone silently
        // means "any slope at a crawl, almost nothing at speed" — at 45 m/s a 5 m/s cap is a six
        // degree hill. Expressing it as a grade keeps the same limit at every throttle setting.
        private float ClimbCap()
        {
            float horizontalSpeed = Horizontal(body.linearVelocity).magnitude;
            return Mathf.Max(minClimbRate, horizontalSpeed * Mathf.Tan(maxFollowGrade * Mathf.Deg2Rad));
        }

        /// <summary>Ease the heading toward a world direction. The write itself is in FixedUpdate.</summary>
        private void FaceDirection(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 1e-4f)
                return;

            float target = Quaternion.LookRotation(direction.normalized).eulerAngles.y;
            heading = Mathf.LerpAngle(heading, target, Mathf.Clamp01(faceRotateSpeed * deltaTime));
        }

        private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void OnDrawGizmosSelected()
        {
            Vector2 extents = groundSensor.FootprintExtents;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawLine(Vector3.zero, Vector3.down * rideHeight);
            Gizmos.DrawWireCube(Vector3.down * rideHeight, new Vector3(extents.x * 2f, 0.02f, extents.y * 2f));
        }
    }
}
