// NavMesh-backed implementation of IMovementMotor used by NPCs and mounts.
// Applies MoveIntent navigation/facing commands to Unity's NavMeshAgent.
// Includes optional mounted-jump simulation via baseOffset animation.
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A movement motor that drives a standard Unity NavMeshAgent.
/// This component translates high-level MoveIntents (from an AI brain or controller)
/// into NavMeshAgent commands (SetDestination, isStopped, etc.).
///
/// Key features:
/// - Handles pathfinding movement to target positions.
/// - Supports "Stop and Face" behavior for precise rotation.
/// - Includes a "Stuck Recovery" mechanism to reset paths if the agent gets wedged.
/// - Implements IMountJumpMotor to simulate jumping by animating the agent's baseOffset.
/// </summary>
// Run before default (0) so agent.enabled=false happens before NavMeshAgent's own Awake registers it.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshAgentMotor : MonoBehaviour, IMovementMotor, IMountJumpMotor, IMountLeapMotor
{
    [Header("Navigation")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private float navMeshSnapDistance = 6f;

    [Header("Speeds")]
    [Tooltip("Fraction of the NavMeshAgent speed used when walking (not running). 1 = same as run speed.")]
    [SerializeField] [Range(0.01f, 1f)] private float walkSpeedMultiplier = 0.65f;

    [Header("Stuck Recovery")]
    [SerializeField] private float stuckVelocityThreshold = 0.05f;
    [SerializeField] private float stuckTime = 1.5f;

    [Header("Facing")]
    [SerializeField] private float faceRotateSpeed = 8f;

    [Header("Mounted Jump")]
    [SerializeField] private bool enableMountedJump = true;
    [SerializeField] private float mountedJumpHeight = 1.25f;
    [SerializeField] private float mountedJumpDuration = 0.55f;
    [SerializeField] private float mountedJumpCooldown = 0.45f;

    [Header("Mounted Leap")]
    [SerializeField] private bool enableMountedLeap = true;
    [SerializeField] private float mountedLeapCooldown = 0.6f;
    [SerializeField] private float mountedLeapSampleRadius = 6f;

    private float stuckTimer;
    private bool defaultUpdateRotation;
    private bool defaultUpdatePosition;
    private float defaultStoppingDistance;
    private float defaultSpeed;
    private float defaultBaseOffset;
    private float jumpElapsed = -1f;
    private float jumpCooldownTimer;

    private bool isLeaping;
    private float leapElapsed;
    private float leapDuration;
    private float leapVertical;
    private float leapCooldownTimer;
    private Vector3 leapStart;
    private Vector3 leapEnd;

    public Vector3 Velocity => IsAgentReady ? agent.velocity : Vector3.zero;

    public bool IsImmobile => !agent || !agent.isOnNavMesh || agent.isStopped;

    public Vector3? CurrentDestination
    {
        get
        {
            if (!IsAgentReady || agent.isStopped || !agent.hasPath)
                return null;
            return agent.destination;
        }
    }

    public bool HasReachedDestination
    {
        get
        {
            if (!IsAgentReady || agent.pathPending)
            {
                return false;
            }

            return agent.remainingDistance <= agent.stoppingDistance + 0.1f;
        }
    }

    private bool IsAgentReady => agent && agent.isActiveAndEnabled && agent.isOnNavMesh;

    private void Awake()
    {
        if (!agent)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        defaultUpdateRotation = agent.updateRotation;
        defaultUpdatePosition = agent.updatePosition;
        defaultStoppingDistance = agent.stoppingDistance;
        defaultSpeed = agent.speed;
        defaultBaseOffset = agent.baseOffset;
        agent.autoBraking = false;

        // Only disable if the NavMesh isn't ready here yet — WorldStreamer will re-enable us
        // after the chunk is baked. If there's already a NavMesh covering our spawn position
        // (pre-baked scene, test scene), stay enabled so the agent works immediately.
        if (!NavMesh.SamplePosition(transform.position, out _, navMeshSnapDistance, NavMesh.AllAreas))
            agent.enabled = false;
    }

    private void OnEnable()
    {
        stuckTimer = 0f;
        jumpCooldownTimer = 0f;
        jumpElapsed = -1f;
        leapCooldownTimer = 0f;
        isLeaping = false;
    }

    public void Tick(in MoveIntent intent, float deltaTime)
    {
        UpdateMountedJump(deltaTime);
        UpdateMountedLeap(deltaTime);

        if (!agent || !agent.isActiveAndEnabled)
        {
            return;
        }

        // During a leap we drive the transform directly; ignore new movement intents.
        if (isLeaping)
        {
            return;
        }

        if (!agent.isOnNavMesh)
        {
            TrySnapToNavMesh();
            return;
        }

        switch (intent.Type)
        {
            case AgentIntentType.MoveToPosition:
                ApplyMoveIntent(intent, deltaTime);
                HandleStuckRecovery(deltaTime);
                break;

            case AgentIntentType.StopAndFacePosition:
                StopAgentPath();
                agent.updateRotation = false;
                FacePosition(intent.FacePosition, deltaTime);
                break;

            default:
                StopAgentPath();
                break;
        }
    }

    public void ForceStop()
    {
        StopAgentPath();
    }

    public void NudgeDestination(Vector3 offset)
    {
        if (!IsAgentReady || agent.isStopped || !agent.hasPath)
            return;

        Vector3 nudged = agent.destination + offset;
        if (NavMesh.SamplePosition(nudged, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    public void SuggestDestination(Vector3 position)
    {
        if (!IsAgentReady)
            return;

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    public void RequestJump()
    {
        if (!enableMountedJump || !agent)
        {
            return;
        }

        if (jumpElapsed >= 0f || jumpCooldownTimer > 0f)
        {
            return;
        }

        jumpElapsed = 0f;
        jumpCooldownTimer = mountedJumpCooldown;
    }

    public bool IsLeapAvailable => enableMountedLeap && !isLeaping && leapCooldownTimer <= 0f;
    public bool IsLeaping => isLeaping;

    public void RequestLeap(Vector3 direction, float horizontalDistance, float verticalHeight, float duration)
    {
        if (!IsLeapAvailable || !agent)
        {
            return;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }
        direction.Normalize();

        Vector3 desired = transform.position + direction * Mathf.Max(0.1f, horizontalDistance);
        Vector3 endPoint = desired;
        if (agent.isOnNavMesh && NavMesh.SamplePosition(desired, out NavMeshHit hit, mountedLeapSampleRadius, NavMesh.AllAreas))
        {
            endPoint = hit.position;
        }

        leapStart = transform.position;
        leapEnd = endPoint;
        leapVertical = Mathf.Max(0f, verticalHeight);
        leapDuration = Mathf.Max(0.05f, duration);
        leapElapsed = 0f;
        isLeaping = true;
        leapCooldownTimer = leapDuration + mountedLeapCooldown;

        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }
        agent.updatePosition = false;
        agent.updateRotation = false;
    }

    private void ApplyMoveIntent(in MoveIntent intent, float deltaTime)
    {
        if (intent.OverrideFacingDirection)
        {
            // The brain is supplying an explicit facing direction — suppress NavMesh
            // auto-rotation so an external system (e.g. SteerModule) can own it.
            agent.updateRotation = false;
        }
        else
        {
            agent.updateRotation = defaultUpdateRotation;
        }

        agent.stoppingDistance = Mathf.Max(0.01f, intent.StopDistance);
        float baseMultiplier = intent.IsRunning ? 1f : walkSpeedMultiplier;
        agent.speed = defaultSpeed * baseMultiplier * Mathf.Max(0.01f, intent.SpeedMultiplier);

        agent.isStopped = false;

        if (!agent.hasPath || Vector3.Distance(agent.destination, intent.TargetPosition) > 0.2f)
        {
            agent.SetDestination(intent.TargetPosition);
        }

        if (HasReachedDestination)
        {
            StopAgentPath();
        }
    }

    private void StopAgentPath()
    {
        if (!agent.isOnNavMesh) return;

        agent.stoppingDistance = defaultStoppingDistance;
        agent.speed = defaultSpeed;
        agent.isStopped = true;

        if (agent.hasPath)
        {
            agent.ResetPath();
        }

        stuckTimer = 0f;
    }

    private void FacePosition(Vector3 worldPosition, float deltaTime)
    {
        Vector3 direction = worldPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, faceRotateSpeed * deltaTime);
    }

    private void HandleStuckRecovery(float deltaTime)
    {
        // Path still resolving — too early to call anything stuck.
        if (agent.pathPending)
        {
            stuckTimer = 0f;
            return;
        }

        if (HasReachedDestination)
        {
            stuckTimer = 0f;
            return;
        }

        if (agent.velocity.sqrMagnitude > stuckVelocityThreshold * stuckVelocityThreshold)
        {
            stuckTimer = 0f;
            return;
        }

        // Partial or invalid paths also count as stuck — the agent is not moving and can't reach
        // the target as-is. Periodically re-request the path in case the nav graph has changed
        // or a dynamic obstacle has moved out of the way.
        stuckTimer += deltaTime;
        if (stuckTimer < stuckTime)
        {
            return;
        }

        Vector3 destination = agent.destination;
        agent.ResetPath();
        agent.SetDestination(destination);
        stuckTimer = 0f;
    }

    private void TrySnapToNavMesh()
    {
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
        {
            return;
        }

        agent.Warp(hit.position);
        stuckTimer = 0f;
    }

    private void UpdateMountedLeap(float deltaTime)
    {
        leapCooldownTimer = Mathf.Max(0f, leapCooldownTimer - deltaTime);
        if (!isLeaping || !agent)
        {
            return;
        }

        leapElapsed += deltaTime;
        float t = Mathf.Clamp01(leapElapsed / leapDuration);
        float arc = Mathf.Sin(t * Mathf.PI);

        Vector3 flat = Vector3.Lerp(leapStart, leapEnd, t);
        Vector3 pos = new Vector3(flat.x, flat.y + arc * leapVertical, flat.z);
        transform.position = pos;

        if (t >= 1f)
        {
            isLeaping = false;
            agent.updatePosition = defaultUpdatePosition;
            agent.updateRotation = defaultUpdateRotation;
            if (NavMesh.SamplePosition(leapEnd, out NavMeshHit hit, mountedLeapSampleRadius, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else
            {
                agent.Warp(leapEnd);
            }
            agent.isStopped = false;
        }
    }

    private void UpdateMountedJump(float deltaTime)
    {
        if (!agent)
        {
            return;
        }

        jumpCooldownTimer = Mathf.Max(0f, jumpCooldownTimer - deltaTime);
        if (jumpElapsed < 0f)
        {
            return;
        }

        jumpElapsed += deltaTime;
        float t = Mathf.Clamp01(jumpElapsed / Mathf.Max(0.01f, mountedJumpDuration));
        float arc = Mathf.Sin(t * Mathf.PI);
        agent.baseOffset = defaultBaseOffset + arc * Mathf.Max(0.01f, mountedJumpHeight);

        if (t >= 1f)
        {
            jumpElapsed = -1f;
            agent.baseOffset = defaultBaseOffset;
        }
    }

    private void OnDisable()
    {
        if (agent)
        {
            agent.baseOffset = defaultBaseOffset;
            agent.updateRotation = defaultUpdateRotation;
        }
    }

    private void OnValidate()
    {
        navMeshSnapDistance = Mathf.Max(0.5f, navMeshSnapDistance);
        stuckVelocityThreshold = Mathf.Max(0.001f, stuckVelocityThreshold);
        stuckTime = Mathf.Max(0.1f, stuckTime);
        faceRotateSpeed = Mathf.Max(0.1f, faceRotateSpeed);
        mountedJumpHeight = Mathf.Max(0.05f, mountedJumpHeight);
        mountedJumpDuration = Mathf.Max(0.05f, mountedJumpDuration);
        mountedJumpCooldown = Mathf.Max(0f, mountedJumpCooldown);
        mountedLeapCooldown = Mathf.Max(0f, mountedLeapCooldown);
        mountedLeapSampleRadius = Mathf.Max(0.5f, mountedLeapSampleRadius);
    }
}
