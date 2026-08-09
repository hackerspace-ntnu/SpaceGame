// Rigidbody-backed motor for airborne vehicles (blimps, gliders, drones, ...). Like RigidbodyMotor
// but moves in full 3D — no NavMesh, no ground-plane lock, no jump/leap. Works with both the AI
// channel (Tick(MoveIntent) → fly toward 3D target) and the rider channel
// (ApplyRiderInput → throttle/yaw/vertical).
//
// Expects a Rigidbody with useGravity = false and reasonable linear/angular damping so the blimp
// doesn't drift forever. Gravity compensation is not done here — configure the Rigidbody itself.
using UnityEngine;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public class FlyingRigidbodyMotor : MonoBehaviour, IMovementMotor, IRiderControllable
{
    [Header("References")]
    [SerializeField] private Rigidbody body;

    [Header("Speeds")]
    [SerializeField] private float maxSpeed = 6f;
    [Tooltip("Vertical climb/descent rate in m/s at full vertical input.")]
    [SerializeField] private float maxVerticalSpeed = 3f;
    [SerializeField] private float acceleration = 4f;
    [SerializeField] private float deceleration = 3f;

    [Header("Facing")]
    [Tooltip("Degrees/sec the blimp rotates to face its movement (AI) or throttle (rider).")]
    [SerializeField] private float faceRotateSpeed = 2.5f;
    [Tooltip("Tank-steer yaw rate in degrees/sec while rider is driving.")]
    [SerializeField] private float riderTurnSpeed = 45f;

    [Header("Altitude Hold")]
    [Tooltip("When idle (no rider, no AI destination), drift back toward this world Y.")]
    [SerializeField] private bool altitudeHold = true;
    [SerializeField] private float cruiseAltitude = 40f;
    [Tooltip("How hard the blimp pulls toward cruiseAltitude (m/s per m of error, capped).")]
    [SerializeField] private float altitudeHoldGain = 0.5f;

    private Vector3? currentDestination;
    private float stopDistance = 0.5f;
    private int riderDriveFrame = -1;

    // Yaw is tracked here rather than read back off the transform: MoveRotation defers the
    // rotation to the next physics step, so transform.eulerAngles stays stale for the rest of
    // the frame and accumulating off it would drop steering increments between steps.
    private float riderYaw;
    private bool riderYawValid;

    // Rider speed is tracked here rather than read back off the Rigidbody, and as scalars rather
    // than a velocity vector. Two separate reasons:
    //
    //  • Reading back from the body feeds its linear damping into the next ramp step as if the
    //    vehicle had genuinely slowed, so acceleration only has to out-run drag rather than reach
    //    maxSpeed. The vehicle then settles far below it (ShipRV: 8 m/s against a configured 26).
    //  • Ramping a velocity *vector* toward a rotating target coples turning to speed: holding
    //    45 m/s through a 100 deg/s turn needs ~78 m/s² of lateral acceleration, so anything less
    //    bleeds the magnitude away instead (measured 45 -> 17 m/s mid-turn). Steering should
    //    re-point the thrust, not scrub it.
    //
    // Same approach as RigidbodyMotor.riderForwardSpeed.
    private float riderForwardSpeed;
    private float riderVerticalSpeed;
    private bool riderVelocityValid;

    // Latest rider input, latched in Update and consumed in FixedUpdate. See ApplyRiderInput.
    private RiderInput pendingRiderInput;
    private bool hasPendingRiderInput;

    public Vector3 Velocity => body ? body.linearVelocity : Vector3.zero;

    public bool IsImmobile
    {
        get
        {
            if (!body)
                return true;
            return body.linearVelocity.sqrMagnitude <= 0.04f;
        }
    }

    public bool HasReachedDestination
    {
        get
        {
            if (!currentDestination.HasValue)
                return true;
            return (currentDestination.Value - transform.position).sqrMagnitude <= stopDistance * stopDistance;
        }
    }

    public Vector3? CurrentDestination => currentDestination;

    private void Awake()
    {
        if (!body)
            body = GetComponent<Rigidbody>();
        if (body)
            body.useGravity = false;
    }

    public void Tick(in MoveIntent intent, float deltaTime)
    {
        if (!body)
            return;

        // Rider owns the motor this frame.
        if (riderDriveFrame == Time.frameCount)
            return;

        // Rider released — re-seed yaw and velocity from the body next time someone takes the
        // controls, so AI movement in between isn't snapped away on re-mount. The latched input
        // goes too, otherwise FixedUpdate would keep flying the last throttle indefinitely.
        riderYawValid = false;
        riderVelocityValid = false;
        hasPendingRiderInput = false;

        switch (intent.Type)
        {
            case AgentIntentType.MoveToPosition:
                ApplyMoveIntent(intent, deltaTime);
                break;

            case AgentIntentType.StopAndFacePosition:
                DecelerateAll(deltaTime);
                FaceDirection(intent.FacePosition - transform.position, faceRotateSpeed, deltaTime);
                break;

            default:
                currentDestination = null;
                IdleHover(deltaTime);
                break;
        }
    }

    // Rider input is latched on the render loop and consumed on the physics loop below.
    //
    // Writing the body directly from here — which is what this did — drives a Rigidbody with the
    // wrong clock. MoveRotation and linearVelocity are only meaningful per physics step, but
    // Update runs at render rate: above 50 Hz several calls land between steps and all but the
    // last are discarded, below it steps get none, and either way the increment is scaled by
    // Time.deltaTime while being integrated over Time.fixedDeltaTime. The body still travels the
    // right *average* distance, so it looks correct to a static observer, but the per-step
    // advance is uneven — and a follow camera that subtracts that pose every frame turns the
    // unevenness straight into shake.
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

        // Input is latched, not cleared: Update may run several times between physics steps (or
        // not at all), and the rider's intent is a held state rather than an event. Clearing it
        // here would drop steering on any frame the two loops did not line up.
        DriveFromRider(pendingRiderInput, Time.fixedDeltaTime);
    }

    // Called once per physics step with the physics clock — the only place the body is written.
    private void DriveFromRider(in RiderInput input, float deltaTime)
    {
        if (!body)
            return;

        // Yaw from Move.x, written through MoveRotation rather than the transform: rotating the
        // transform of a non-kinematic Rigidbody teleports it and discards the pose interpolation
        // blends from, so the body renders at the raw physics rate. At 50 Hz physics and 90 fps
        // that froze roughly half of all rendered frames whenever the pilot was steering — the
        // "choppy vehicle" bug.
        if (!riderYawValid)
        {
            riderYaw = body.rotation.eulerAngles.y;
            riderYawValid = true;
        }

        riderYaw += input.Move.x * riderTurnSpeed * deltaTime;
        Quaternion facing = Quaternion.Euler(0f, riderYaw, 0f);
        body.MoveRotation(facing);

        // Throttle along the yaw we just asked for — transform.forward is still a physics step
        // behind until MoveRotation lands. Level by construction, so no pitch to strip.
        Vector3 forward = facing * Vector3.forward;

        if (!riderVelocityValid)
        {
            Vector3 v = body.linearVelocity;
            riderForwardSpeed = Vector3.Dot(v, forward);
            riderVerticalSpeed = v.y;
            riderVelocityValid = true;
        }

        float throttle = input.Move.y;
        bool hasInput = Mathf.Abs(throttle) > 0.01f || Mathf.Abs(input.Vertical) > 0.01f;
        float ramp = (hasInput ? acceleration : deceleration) * deltaTime;

        riderForwardSpeed = Mathf.MoveTowards(riderForwardSpeed, throttle * maxSpeed, ramp);
        riderVerticalSpeed = Mathf.MoveTowards(riderVerticalSpeed, input.Vertical * maxVerticalSpeed, ramp);

        // Thrust always points along the current facing, so steering redirects it immediately.
        body.linearVelocity = forward * riderForwardSpeed + Vector3.up * riderVerticalSpeed;

        currentDestination = null;
    }

    public void ForceStop()
    {
        currentDestination = null;
        riderForwardSpeed = 0f;
        riderVerticalSpeed = 0f;
        riderVelocityValid = false;
        // Drop the latch as well — otherwise the next physics step re-applies the last throttle
        // and the stop is undone before it is ever rendered.
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
        currentDestination = position;
    }

    private void ApplyMoveIntent(in MoveIntent intent, float deltaTime)
    {
        currentDestination = intent.TargetPosition;
        stopDistance = Mathf.Max(0.1f, intent.StopDistance);

        Vector3 toTarget = intent.TargetPosition - transform.position;
        float distance = toTarget.magnitude;

        if (distance <= stopDistance)
        {
            DecelerateAll(deltaTime);
            return;
        }

        Vector3 moveDir = toTarget / distance;
        float targetSpeed = maxSpeed * Mathf.Max(0.01f, intent.SpeedMultiplier);
        Vector3 desired = moveDir * targetSpeed;

        body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, desired, acceleration * deltaTime);

        // Face horizontal direction of travel.
        Vector3 flatDir = moveDir;
        flatDir.y = 0f;
        FaceDirection(flatDir, faceRotateSpeed, deltaTime);
    }

    private void IdleHover(float deltaTime)
    {
        Vector3 v = body.linearVelocity;

        // Bleed horizontal velocity.
        Vector3 horizontal = new Vector3(v.x, 0f, v.z);
        horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, deceleration * deltaTime);
        v.x = horizontal.x;
        v.z = horizontal.z;

        // Altitude hold: ease Y velocity toward correction needed to reach cruiseAltitude.
        if (altitudeHold)
        {
            float altitudeError = cruiseAltitude - transform.position.y;
            float targetVy = Mathf.Clamp(altitudeError * altitudeHoldGain, -maxVerticalSpeed, maxVerticalSpeed);
            v.y = Mathf.MoveTowards(v.y, targetVy, acceleration * deltaTime);
        }
        else
        {
            v.y = Mathf.MoveTowards(v.y, 0f, deceleration * deltaTime);
        }

        body.linearVelocity = v;
        body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, deceleration * deltaTime);
    }

    private void DecelerateAll(float deltaTime)
    {
        if (!body) return;
        body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, Vector3.zero, deceleration * deltaTime);
        body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, deceleration * deltaTime);
    }

    private void FaceDirection(Vector3 direction, float rotateSpeed, float deltaTime)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 1e-4f)
            return;
        Quaternion target = Quaternion.LookRotation(direction.normalized);
        // MoveRotation rather than transform.rotation for the interpolation reason in ApplyRiderInput.
        body.MoveRotation(Quaternion.Slerp(body.rotation, target, rotateSpeed * deltaTime));
    }

    private void OnValidate()
    {
        maxSpeed = Mathf.Max(0.01f, maxSpeed);
        maxVerticalSpeed = Mathf.Max(0.01f, maxVerticalSpeed);
        acceleration = Mathf.Max(0.1f, acceleration);
        deceleration = Mathf.Max(0.1f, deceleration);
        faceRotateSpeed = Mathf.Max(0.01f, faceRotateSpeed);
        riderTurnSpeed = Mathf.Max(1f, riderTurnSpeed);
        altitudeHoldGain = Mathf.Max(0f, altitudeHoldGain);
    }
}
