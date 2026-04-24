// Rigidbody-backed implementation of IMovementMotor for vehicles and other physics-driven
// mounts. Translates MoveIntent commands into direct linear velocity on the Rigidbody,
// leaving gravity and collisions intact. Jump/leap are vertical/horizontal arcs driven by
// temporarily going kinematic, matching the NavMesh motor's mount feel.
using UnityEngine;

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

    public void ApplyRiderInput(in RiderInput input, float deltaTime)
    {
        riderDriveFrame = Time.frameCount;
        if (!body || arcing)
            return;

        // Tank steer: rotate body by yaw input.
        float yaw = input.Move.x * riderTurnSpeed * deltaTime;
        if (Mathf.Abs(yaw) > 1e-4f)
            transform.Rotate(0f, yaw, 0f, Space.World);

        // Throttle along own forward.
        float throttle = input.Move.y;
        float baseMultiplier = input.IsRunning ? 1f : walkSpeedMultiplier;
        Vector3 desired = transform.forward * (throttle * maxSpeed * baseMultiplier);

        Vector3 current = body.linearVelocity;
        Vector3 horizontal = new Vector3(current.x, 0f, current.z);
        float ramp = (Mathf.Abs(throttle) > 0.01f ? acceleration : deceleration) * deltaTime;
        Vector3 next = Vector3.MoveTowards(horizontal, new Vector3(desired.x, 0f, desired.z), ramp);

        current.x = next.x;
        current.z = next.z;
        body.linearVelocity = current;

        // Rider drives manually, so any AI-era destination is stale.
        currentDestination = null;
    }

    public void ForceStop()
    {
        currentDestination = null;
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
        transform.rotation = Quaternion.Slerp(transform.rotation, target, faceRotateSpeed * deltaTime);
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
        transform.position = new Vector3(flat.x, flat.y + arc * arcHeight, flat.z);

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
