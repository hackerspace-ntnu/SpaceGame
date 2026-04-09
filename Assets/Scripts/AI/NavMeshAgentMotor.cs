using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshAgentMotor : MonoBehaviour, IMovementMotor
{
    [Header("Navigation")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private float navMeshSnapDistance = 6f;

    [Header("Stuck Recovery")]
    [SerializeField] private float stuckVelocityThreshold = 0.05f;
    [SerializeField] private float stuckTime = 1.5f;

    [Header("Facing")]
    [SerializeField] private float faceRotateSpeed = 8f;

    private float stuckTimer;
    private bool defaultUpdateRotation;
    private float defaultStoppingDistance;
    private float defaultSpeed;

    public Vector3 Velocity => agent ? agent.velocity : Vector3.zero;

    public bool IsImmobile => !agent || !agent.isOnNavMesh || agent.isStopped;

    public bool HasReachedDestination
    {
        get
        {
            if (!agent || !agent.isActiveAndEnabled || !agent.isOnNavMesh || agent.pathPending)
            {
                return false;
            }

            return agent.remainingDistance <= agent.stoppingDistance + 0.1f;
        }
    }

    private void Awake()
    {
        if (!agent)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        defaultUpdateRotation = agent.updateRotation;
        defaultStoppingDistance = agent.stoppingDistance;
        defaultSpeed = agent.speed;
        agent.autoBraking = false;
    }

    private void OnEnable()
    {
        stuckTimer = 0f;
    }

    public void Tick(in MoveIntent intent, float deltaTime)
    {
        if (!agent)
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
                ApplyMoveIntent(intent);
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

    private void ApplyMoveIntent(in MoveIntent intent)
    {
        agent.updateRotation = defaultUpdateRotation;
        agent.stoppingDistance = Mathf.Max(0.01f, intent.StopDistance);
        agent.speed = defaultSpeed * Mathf.Max(0.01f, intent.SpeedMultiplier);
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
        if (agent.pathPending || agent.pathStatus != NavMeshPathStatus.PathComplete)
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

    private void OnValidate()
    {
        navMeshSnapDistance = Mathf.Max(0.5f, navMeshSnapDistance);
        stuckVelocityThreshold = Mathf.Max(0.001f, stuckVelocityThreshold);
        stuckTime = Mathf.Max(0.1f, stuckTime);
        faceRotateSpeed = Mathf.Max(0.1f, faceRotateSpeed);
    }
}
