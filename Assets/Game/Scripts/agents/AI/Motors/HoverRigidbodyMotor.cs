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

        [Header("Parked")]
        [Tooltip("When nobody is riding and the AI has no standing order, hand the hull to " +
                 "physics: gravity on, servo off, so the craft settles onto its own colliders " +
                 "and sits there as dead weight. Off = classic behaviour: an empty craft keeps " +
                 "hovering at ride height.")]
        [SerializeField] private bool restWhenParked;

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

        // Whether the parked handover to physics is currently in effect. Tracked so the gravity
        // flag is only written on the transition, not fought over every step.
        private bool isParked;

        // The constraints the body was authored with, captured once so parking can add its own and
        // un-parking can hand back exactly what it found.
        private RigidbodyConstraints authoredConstraints;

        public Vector3 Velocity => body ? body.linearVelocity : Vector3.zero;

        /// <summary>Ground clearance the craft is holding, in metres.</summary>
        public float RideHeight
        {
            get => rideHeight;
            set => rideHeight = Mathf.Max(0f, value);
        }

        /// <summary>True while the craft is over ground it can actually measure.</summary>
        public bool HasGround => groundSensor.HasGround;

        /// <summary>
        /// Half-extents of the ground this craft rides over, in its own axes.
        ///
        /// <para>
        /// Exposed so whoever puts the craft DOWN measures the same patch of ground the servo will
        /// hold it over once it wakes up. A landing grounded against a different footprint is a
        /// landing the craft corrects on its first physics step, which reads as a ship that touches
        /// down and then floats back up.
        /// </para>
        /// </summary>
        public Vector2 FootprintExtents => groundSensor.FootprintExtents;

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

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // Two fields, and neither of them the obvious one.
        //
        // The HEADING is owned outright and rewritten every physics step, and it is only ever seeded
        // from the body once, on the first step. Left unsaved it re-seeds from the restored
        // rotation, which is nearly right — but "nearly" is the whole problem on a hull that flies
        // level by design, because the seed happens before the first MoveRotation and a craft
        // restored with any residual tilt takes its yaw from a rotation that has pitch and roll in
        // it. Saving it makes the answer exact.
        //
        // The DESTINATION comes along so anything that asks this motor where it was going — the
        // driver's HasReachedDestination, a module deciding whether to re-issue an order — gets the
        // right answer on the frames before the brain has ticked. It is short-lived by design: the
        // AI channel rewrites it every FixedUpdate, so this is a correct FIRST answer, not a
        // standing order.
        //
        // `pendingIntent` is deliberately NOT saved, and that is the interesting omission: it is the
        // last order from the AI channel, and the AI channel re-issues an order every single frame
        // it is ticked. A restored one is therefore live for exactly one frame before it is
        // overwritten — one frame of a stale order flying the craft, which is strictly worse than
        // the idle it would otherwise start from.
        public float Heading => heading;
        public bool HeadingValid => headingValid;
        public float StopDistance => stopDistance;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreHeading(float restoredHeading)
        {
            heading = restoredHeading;
            headingValid = true;
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreDestination(Vector3? destination, float stop)
        {
            currentDestination = destination;
            stopDistance = Mathf.Max(0.1f, stop);
        }

        private void Awake()
        {
            if (!body)
                body = GetComponent<Rigidbody>();

            // The servo owns the vertical axis outright, so gravity would only be a force to cancel
            // every step. By default there is no parked state where letting go is right — a
            // hovercraft with nobody aboard still hovers. restWhenParked is the opt-out for hulls
            // that are meant to stand on the ground as dead weight between flights.
            if (body)
            {
                authoredConstraints = body.constraints;
                body.useGravity = restWhenParked;
                isParked = restWhenParked;
                if (isParked) body.constraints = ParkedConstraints(authoredConstraints);
            }

            groundSensor.Initialize(transform);
        }

        /// <summary>
        /// Tell the hull it is carrying <paramref name="body"/>, so its own height probe stops
        /// reading them as the ground and climbing on them. See
        /// <see cref="HoverGroundSensor.Carry"/> for the failure this prevents.
        /// </summary>
        public void Carry(GameObject body) => groundSensor.Carry(body);

        /// <summary>Counterpart to <see cref="Carry"/>.</summary>
        public void StopCarrying(GameObject body) => groundSensor.StopCarrying(body);

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

            // A kinematic body has no velocity to zero, and Unity warns on the write. The arrival
            // parks the hull while it is still kinematic from the descent, which is how this used
            // to log twice per landing.
            if (!body || body.isKinematic)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// What a parked hull may do: everything it was authored to, minus horizontal travel.
        ///
        /// <para>
        /// Pure so the one fact worth asserting — parking pins X and Z and ONLY X and Z — can be
        /// asserted without a physics scene. Y stays free deliberately: gravity is what seats the
        /// parked hull on the ground, and freezing it would leave a craft parked mid-hover hanging
        /// where it stopped.
        /// </para>
        /// </summary>
        public static RigidbodyConstraints ParkedConstraints(RigidbodyConstraints authored) =>
            authored | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;

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

            // Parked = nobody riding and no standing AI order. With restWhenParked on, this is the
            // one state where the motor deliberately lets go: gravity is on and nothing writes the
            // velocity or the attitude, so the hull rests on its own colliders. Its horizontal
            // position is FROZEN while it does — "moves as far as its mass allows" was the first
            // version, and mass is no defence here: agents and mounts walk on KINEMATIC bodies,
            // and a kinematic collider depenetrates a dynamic one with infinite authority, so a
            // strolling NPC could shove a 60-tonne wreck around by leaning on it. The vertical
            // axis stays free so gravity can still settle the hull onto the ground it was parked
            // over. The moment anything drives again, the constraints are handed back and the
            // servo re-seeds its heading from wherever physics left the hull pointing.
            bool parked = restWhenParked && !hasPendingRiderInput
                          && pendingIntent.Type == AgentIntentType.Idle;
            if (parked != isParked)
            {
                isParked = parked;
                body.useGravity = parked;
                body.constraints = parked ? ParkedConstraints(authoredConstraints) : authoredConstraints;
                if (!parked)
                    headingValid = false;
            }
            if (parked)
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
