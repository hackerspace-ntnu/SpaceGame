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

    public void ApplyRiderInput(in RiderInput input, float deltaTime)
    {
        riderDriveFrame = Time.frameCount;
        if (!body)
            return;

        // Yaw from Move.x.
        float yaw = input.Move.x * riderTurnSpeed * deltaTime;
        if (Mathf.Abs(yaw) > 1e-4f)
            transform.Rotate(0f, yaw, 0f, Space.World);

        // Throttle along own forward (ignore pitch — blimp stays level).
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 1e-4f)
            forward.Normalize();

        float throttle = input.Move.y;
        Vector3 desired = forward * (throttle * maxSpeed);
        desired.y = input.Vertical * maxVerticalSpeed;

        bool hasInput = Mathf.Abs(throttle) > 0.01f || Mathf.Abs(input.Vertical) > 0.01f;
        float ramp = (hasInput ? acceleration : deceleration) * deltaTime;
        body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, desired, ramp);

        currentDestination = null;
    }

    public void ForceStop()
    {
        currentDestination = null;
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
        transform.rotation = Quaternion.Slerp(transform.rotation, target, rotateSpeed * deltaTime);
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
