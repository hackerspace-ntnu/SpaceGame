using UnityEngine;

/// <summary>
/// Rigidbody-based movement motor that mirrors PlayerMovement's velocity-lerp approach.
/// Instead of NavMesh pathfinding, it computes the direction to the brain's target position
/// each physics tick and blends toward that velocity — the same way the player moves.
///
/// Assign this as the motor on the mount animal's AgentController to get responsive,
/// physics-driven movement when riding. Works for NPC wandering too (straight-line, no
/// obstacle avoidance), so it's a full NavMeshAgentMotor replacement for open environments.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RigidbodyMotor : MonoBehaviour, IMovementMotor
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    // Lerp factor applied each FixedUpdate — identical role to PlayerMovement's 'control'.
    // 1.0 = instant snap (same as grounded player). 0.15-0.25 = responsive with slight weight.
    [SerializeField, Range(0.01f, 1f)] private float acceleration = 0.2f;

    [Header("Facing")]
    // Auto-rotation speed (deg/sec) used when the brain does NOT set OverrideFacingDirection.
    // For mounted movement MountController owns rotation and this is skipped automatically.
    [SerializeField] private float faceRotateSpeed = 300f;

    private Rigidbody rb;
    private MoveIntent pendingIntent;

    public Vector3 Velocity => rb ? rb.linearVelocity : Vector3.zero;

    public bool IsImmobile
    {
        get
        {
            if (!rb) return true;
            Vector3 v = rb.linearVelocity;
            // Only horizontal velocity counts — vertical (gravity) is not "movement".
            return v.x * v.x + v.z * v.z < 0.01f;
        }
    }

    public bool HasReachedDestination
    {
        get
        {
            if (pendingIntent.Type != AgentIntentType.MoveToPosition) return true;
            Vector3 delta = pendingIntent.TargetPosition - transform.position;
            delta.y = 0f;
            return delta.magnitude <= pendingIntent.StopDistance + 0.1f;
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pendingIntent = MoveIntent.Idle();
    }

    /// <summary>Called by AgentController every Update — stores the intent for FixedUpdate.</summary>
    public void Tick(in MoveIntent intent, float deltaTime)
    {
        pendingIntent = intent;
    }

    public void ForceStop()
    {
        pendingIntent = MoveIntent.Idle();
        if (!rb) return;
        Vector3 v = rb.linearVelocity;
        v.x = 0f;
        v.z = 0f;
        rb.linearVelocity = v;
    }

    private void FixedUpdate()
    {
        if (!rb) return;

        Vector3 desiredHorizontal = ComputeDesiredHorizontal();

        // Core player movement pattern: lerp current horizontal velocity toward desired.
        // Vertical velocity (gravity/jump) is preserved untouched.
        Vector3 velocity = rb.linearVelocity;
        Vector3 currentHorizontal = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 newHorizontal = Vector3.Lerp(currentHorizontal, desiredHorizontal, acceleration);
        velocity.x = newHorizontal.x;
        velocity.z = newHorizontal.z;
        rb.linearVelocity = velocity;

        // Auto-face movement direction for NPC brains (wandering, patrolling).
        // Skipped when OverrideFacingDirection is true — MountController owns rotation then.
        if (!pendingIntent.OverrideFacingDirection && faceRotateSpeed > 0f && desiredHorizontal.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(desiredHorizontal.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, faceRotateSpeed * Time.fixedDeltaTime);
        }
    }

    private Vector3 ComputeDesiredHorizontal()
    {
        if (pendingIntent.Type != AgentIntentType.MoveToPosition)
        {
            return Vector3.zero;
        }

        Vector3 toTarget = pendingIntent.TargetPosition - transform.position;
        toTarget.y = 0f;

        float stopDist = pendingIntent.StopDistance;
        if (toTarget.sqrMagnitude <= stopDist * stopDist)
        {
            return Vector3.zero;
        }

        return toTarget.normalized * (moveSpeed * Mathf.Max(0.01f, pendingIntent.SpeedMultiplier));
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        faceRotateSpeed = Mathf.Max(0f, faceRotateSpeed);
    }
}
