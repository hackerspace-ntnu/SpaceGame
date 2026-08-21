// Rigidbody-backed implementation of IMovementMotor for vehicles and other physics-driven
// mounts. Translates MoveIntent commands into direct linear velocity on the Rigidbody,
// leaving gravity and collisions intact. Jump/leap are vertical/horizontal arcs driven by
// temporarily going kinematic, matching the NavMesh motor's mount feel.
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public class RigidbodyMotor : MonoBehaviour, IMovementMotor, IMountJumpMotor, IMountLeapMotor, IRiderControllable
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;

        [Header("Speeds")]
        [SerializeField] private float maxSpeed = 8f;
        [Tooltip("Fraction of maxSpeed used when the intent is not 'running'.")]
        [SerializeField] [Range(0.01f, 1f)] private float walkSpeedMultiplier = 0.65f;
        [SerializeField] private float acceleration = 12f;
        [SerializeField] private float deceleration = 16f;

        [Header("Facing")]
        [SerializeField] private float faceRotateSpeed = 8f;

        [Header("Rider Steering")]
        [Tooltip("Tank-steer yaw rate in degrees/sec when rider is driving.")]
        [SerializeField] private float riderTurnSpeed = 120f;

        [Header("Jump")]
        [SerializeField] private bool enableJump = true;
        [SerializeField] private float jumpHeight = 1.25f;
        [SerializeField] private float jumpDuration = 0.55f;
        [SerializeField] private float jumpCooldown = 0.45f;

        [Header("Leap")]
        [SerializeField] private bool enableLeap = true;
        [SerializeField] private float leapCooldown = 0.6f;

        private Vector3? currentDestination;
        private float stopDistance = 0.2f;

        private bool arcing;
        private float arcElapsed;
        private float arcDuration;
        private float arcHeight;
        private Vector3 arcStart;
        private Vector3 arcEnd;
        private bool arcWasKinematic;
        private float arcCooldownTimer;

        // Set to Time.frameCount inside ApplyRiderInput so Tick() the same frame skips the
        // MoveIntent-interpretation path (which would decelerate and fight the rider).
        private int riderDriveFrame = -1;

        // Latest rider input, latched in Update and consumed in FixedUpdate. See ApplyRiderInput.
        private RiderInput pendingRiderInput;
        private bool hasPendingRiderInput;

        // Track rider throttle along forward in our own state. If we read this back from
        // body.linearVelocity, ground friction between FixedUpdates eats it and the rider
        // feels stuck — see RigidbodyMotor.ApplyRiderInput for the full reasoning.
        private float riderForwardSpeed;

        // Same reasoning for yaw. MoveRotation defers the actual rotation to the next physics step,
        // so transform.eulerAngles still reads the old value for the rest of this frame; accumulating
        // off it would silently drop every steering increment made between physics steps.
        private float riderYaw;
        private bool riderYawValid;

        public Vector3 Velocity => body ? body.linearVelocity : Vector3.zero;

        public bool IsImmobile
        {
            get
            {
                if (!body)
                    return true;
                Vector3 v = body.linearVelocity;
                v.y = 0f;
                return v.sqrMagnitude <= 0.01f;
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

        public bool IsLeapAvailable => enableLeap && !arcing && arcCooldownTimer <= 0f;
        public bool IsLeaping => arcing;

        private void Awake()
        {
            if (!body)
                body = GetComponent<Rigidbody>();
        }

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // <c>arcWasKinematic</c> is the reason this motor needed a saver at all, and it is the one
        // piece of state here whose loss is not cosmetic.
        //
        // An arc runs by forcing the body kinematic and remembering what it was before, so that
        // UpdateArc can put it back on the frame the arc lands. That memory lives in ONE place and
        // in no other, so a save taken mid-arc records a kinematic body and loses the only record of
        // what it should stop being. Reload, and either the arc never resumes and the body stays
        // kinematic — unpushable, weightless, permanently — or it resumes and lands the body on
        // whatever `arcWasKinematic` happened to default to. Neither is recoverable in play.
        //
        // So the resting flag is captured EVERY time, not only mid-arc, and re-asserted on restore.
        // That is not a violation of "savers do not store engine flags": this motor is the component
        // that asserts isKinematic on this body, and the flag is exactly what it is here to own.
        //
        // The rider channel (`riderForwardSpeed`, `pendingRiderInput`) is deliberately not saved. A
        // rider's held stick is an input, and there is nobody holding it on the frame a world loads;
        // restoring one would drive the vehicle off under a stick nobody is touching.
        public float StopDistance => stopDistance;
        public bool Arcing => arcing;
        public float ArcElapsed => arcElapsed;
        public float ArcDuration => arcDuration;
        public float ArcHeight => arcHeight;
        public Vector3 ArcStart => arcStart;
        public Vector3 ArcEnd => arcEnd;
        public float ArcCooldownTimer => arcCooldownTimer;

        /// <summary>
        /// What <c>isKinematic</c> means for this body when nothing is arcing: the live flag
        /// normally, and the remembered one while an arc has temporarily overridden it.
        /// </summary>
        public bool RestingKinematic => arcing ? arcWasKinematic : body != null && body.isKinematic;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreDestination(Vector3? destination, float stop)
        {
            currentDestination = destination;
            stopDistance = Mathf.Max(0.01f, stop);
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <paramref name="restingKinematic"/> is applied on BOTH paths. Resuming an arc needs it so
        /// the landing has something correct to restore to; not resuming one needs it so a body that
        /// arrived kinematic — because that is what it was mid-arc when the save was written — is
        /// handed back its weight instead of hovering for the rest of the session.
        /// </summary>
        public void RestoreArc(bool wasArcing, float elapsed, float duration, float height,
                               Vector3 start, Vector3 end, bool restingKinematic, float cooldown)
        {
            arcCooldownTimer = Mathf.Max(0f, cooldown);
            arcWasKinematic = restingKinematic;

            if (!wasArcing)
            {
                arcing = false;
                if (body != null && body.isKinematic != restingKinematic)
                    body.isKinematic = restingKinematic;
                return;
            }

            arcing = true;
            arcElapsed = Mathf.Max(0f, elapsed);
            arcDuration = Mathf.Max(0.05f, duration);
            arcHeight = Mathf.Max(0f, height);
            arcStart = start;
            arcEnd = end;

            if (body == null) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        public void Tick(in MoveIntent intent, float deltaTime)
        {
            arcCooldownTimer = Mathf.Max(0f, arcCooldownTimer - deltaTime);

            if (arcing)
            {
                UpdateArc(deltaTime);
                return;
            }

            if (!body)
                return;

            // Rider owns the motor this frame — don't interpret the MoveIntent (which would fight
            // the rider's direct velocity/rotation writes).
            if (riderDriveFrame == Time.frameCount)
                return;

            // Rider released — drop tracked rider speed so AI handoff doesn't snap back to it
            // if the rider re-mounts later mid-motion. The latched input goes too, otherwise
            // FixedUpdate would keep driving the last throttle indefinitely.
            riderForwardSpeed = 0f;
            riderYawValid = false;
            hasPendingRiderInput = false;

            switch (intent.Type)
            {
                case AgentIntentType.MoveToPosition:
                    ApplyMoveIntent(intent, deltaTime);
                    break;

                case AgentIntentType.StopAndFacePosition:
                    DecelerateHorizontal(deltaTime);
                    FacePosition(intent.FacePosition, deltaTime);
                    break;

                default:
                    DecelerateHorizontal(deltaTime);
                    currentDestination = null;
                    break;
            }
        }

        // Rider input is latched on the render loop and consumed on the physics loop below.
        //
        // Writing the body directly from here drives a Rigidbody with the wrong clock: MoveRotation
        // and linearVelocity only mean anything per physics step, but Update runs at render rate, so
        // above 50 Hz several calls land between steps and all but the last are discarded, below it
        // steps get none, and the increment is scaled by Time.deltaTime while being integrated over
        // Time.fixedDeltaTime. Average motion still looks right to a static observer, but the
        // per-step advance is uneven — which a follow camera converts directly into shake.
        public void ApplyRiderInput(in RiderInput input, float deltaTime)
        {
            riderDriveFrame = Time.frameCount;
            pendingRiderInput = input;
            hasPendingRiderInput = true;
        }

        private void FixedUpdate()
        {
            if (!hasPendingRiderInput)
                return;

            // Latched, not cleared: Update may run several times between physics steps or not at all,
            // and the rider's intent is a held state rather than an event.
            DriveFromRider(pendingRiderInput, Time.fixedDeltaTime);
        }

        // Called once per physics step with the physics clock — the only place the body is written.
        private void DriveFromRider(in RiderInput input, float deltaTime)
        {
            if (!body || arcing)
                return;

            body.WakeUp();

            // Tank steer: rotate body by yaw input. Rebuild rotation as upright + yaw so any
            // initial tilt (vehicle spawned on a slope) is shed instead of preserved forever
            // by additive rotation around world Y.
            //
            // Written through MoveRotation, never transform.rotation: assigning the transform of a
            // non-kinematic Rigidbody teleports it and discards the pose interpolation blends from,
            // so the body renders at the raw physics rate. At 50 Hz physics and 90 fps that froze
            // roughly half of all rendered frames — the "choppy vehicle" bug.
            if (!riderYawValid)
            {
                riderYaw = body.rotation.eulerAngles.y;
                riderYawValid = true;
            }

            riderYaw += input.Move.x * riderTurnSpeed * deltaTime;
            Quaternion facing = Quaternion.Euler(0f, riderYaw, 0f);
            body.MoveRotation(facing);

            // Throttle along the yaw we just asked for rather than transform.forward, which is still
            // a physics step behind until MoveRotation lands.
            float throttle = input.Move.y;
            float baseMultiplier = input.IsRunning ? 1f : walkSpeedMultiplier;
            Vector3 forward = facing * Vector3.forward;

            // Track speed in our own state so ground friction between FixedUpdates can't drain it.
            float targetSpeed = throttle * maxSpeed * baseMultiplier;
            float ramp = (Mathf.Abs(throttle) > 0.01f ? acceleration : deceleration) * deltaTime;
            riderForwardSpeed = Mathf.MoveTowards(riderForwardSpeed, targetSpeed, ramp);

            Vector3 current = body.linearVelocity;
            Vector3 horizontal = forward * riderForwardSpeed;
            current.x = horizontal.x;
            current.z = horizontal.z;
            body.linearVelocity = current;

            // Rider drives manually, so any AI-era destination is stale.
            currentDestination = null;
        }

        public void ForceStop()
        {
            currentDestination = null;
            riderForwardSpeed = 0f;
            // Drop the latch as well — otherwise the next physics step re-applies the last throttle
            // and the stop is undone before it is ever rendered.
            hasPendingRiderInput = false;
            if (!body)
                return;
            Vector3 v = body.linearVelocity;
            v.x = 0f;
            v.z = 0f;
            body.linearVelocity = v;
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
            currentDestination = position;
        }

        public void RequestJump()
        {
            if (!enableJump || arcing || arcCooldownTimer > 0f)
                return;
            BeginArc(Vector3.zero, 0f, jumpHeight, jumpDuration);
            arcCooldownTimer = jumpDuration + jumpCooldown;
        }

        public void RequestLeap(Vector3 direction, float horizontalDistance, float verticalHeight, float duration)
        {
            if (!IsLeapAvailable)
                return;
            BeginArc(direction, horizontalDistance, verticalHeight, duration);
            arcCooldownTimer = duration + leapCooldown;
        }

        private void ApplyMoveIntent(in MoveIntent intent, float deltaTime)
        {
            currentDestination = intent.TargetPosition;
            stopDistance = Mathf.Max(0.01f, intent.StopDistance);

            Vector3 toTarget = intent.TargetPosition - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= stopDistance)
            {
                DecelerateHorizontal(deltaTime);
                return;
            }

            Vector3 moveDir = toTarget / distance;
            float baseMultiplier = intent.IsRunning ? 1f : walkSpeedMultiplier;
            float targetSpeed = maxSpeed * baseMultiplier * Mathf.Max(0.01f, intent.SpeedMultiplier);

            Vector3 current = body.linearVelocity;
            Vector3 horizontal = new Vector3(current.x, 0f, current.z);
            Vector3 desired = moveDir * targetSpeed;
            Vector3 next = Vector3.MoveTowards(horizontal, desired, acceleration * deltaTime);

            current.x = next.x;
            current.z = next.z;
            body.linearVelocity = current;

            if (intent.OverrideFacingDirection && intent.FacingDirection.sqrMagnitude > 1e-4f)
            {
                Vector3 face = intent.FacingDirection;
                face.y = 0f;
                RotateToward(face, deltaTime);
            }
            else
            {
                RotateToward(moveDir, deltaTime);
            }
        }

        private void DecelerateHorizontal(float deltaTime)
        {
            if (!body)
                return;
            Vector3 v = body.linearVelocity;
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, deceleration * deltaTime);
            v.x = horizontal.x;
            v.z = horizontal.z;
            body.linearVelocity = v;

            // Also bleed off angular velocity so stray bumps / spins decay instead of spinning forever.
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, deceleration * deltaTime);
        }

        private void FacePosition(Vector3 worldPosition, float deltaTime)
        {
            Vector3 direction = worldPosition - transform.position;
            direction.y = 0f;
            RotateToward(direction, deltaTime);
        }

        private void RotateToward(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude <= 1e-4f)
                return;
            Quaternion target = Quaternion.LookRotation(direction.normalized);
            Quaternion blended = Quaternion.Slerp(body.rotation, target, faceRotateSpeed * deltaTime);
            // Strip residual pitch/roll so a vehicle that spawned tilted (or got bumped) levels
            // itself as it drives, instead of slerping forever toward upright. MoveRotation rather
            // than transform.rotation for the interpolation reason in ApplyRiderInput.
            body.MoveRotation(Quaternion.Euler(0f, blended.eulerAngles.y, 0f));
        }

        private void BeginArc(Vector3 direction, float horizontalDistance, float height, float duration)
        {
            arcing = true;
            arcElapsed = 0f;
            arcDuration = Mathf.Max(0.05f, duration);
            arcHeight = Mathf.Max(0f, height);
            arcStart = transform.position;

            Vector3 horizontal = direction;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude < 1e-4f || horizontalDistance <= 0f)
            {
                arcEnd = arcStart;
            }
            else
            {
                horizontal.Normalize();
                arcEnd = arcStart + horizontal * horizontalDistance;
            }

            arcWasKinematic = body.isKinematic;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        private void UpdateArc(float deltaTime)
        {
            arcElapsed += deltaTime;
            float t = Mathf.Clamp01(arcElapsed / arcDuration);
            float arc = Mathf.Sin(t * Mathf.PI);

            Vector3 flat = Vector3.Lerp(arcStart, arcEnd, t);
            // MovePosition, not transform.position — same interpolation reason as the rotation
            // writes, and it applies to the kinematic body an arc runs on too.
            body.MovePosition(new Vector3(flat.x, flat.y + arc * arcHeight, flat.z));

            if (t >= 1f)
            {
                arcing = false;
                body.isKinematic = arcWasKinematic;
            }
        }

        private void OnValidate()
        {
            maxSpeed = Mathf.Max(0.01f, maxSpeed);
            acceleration = Mathf.Max(0.1f, acceleration);
            deceleration = Mathf.Max(0.1f, deceleration);
            faceRotateSpeed = Mathf.Max(0.1f, faceRotateSpeed);
            riderTurnSpeed = Mathf.Max(1f, riderTurnSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            jumpDuration = Mathf.Max(0.05f, jumpDuration);
            jumpCooldown = Mathf.Max(0f, jumpCooldown);
            leapCooldown = Mathf.Max(0f, leapCooldown);
        }
    }
}
