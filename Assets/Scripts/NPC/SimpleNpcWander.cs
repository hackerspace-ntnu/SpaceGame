using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class SimpleNpcWander : MonoBehaviour
{
    [Header("Wander")]
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float sampleDistance = 6f;
    [SerializeField] private int maxTriesPerDestination = 10;
    [SerializeField] private float minDestinationDistance = 1.5f;
    [SerializeField] private float minWaitTime = 0.5f;
    [SerializeField] private float maxWaitTime = 2f;

    [Header("Recovery")]
    [SerializeField] private float stuckVelocityThreshold = 0.05f;
    [SerializeField] private float stuckTime = 1.5f;

    [Header("Player Reaction")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float stopAndLookRadius = 4f;
    [SerializeField] private float lookRotateSpeed = 8f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationSpeedMultiplier = 1.5f;

    private NavMeshAgent agent;
    private NavMeshPath path;
    private float waitTimer;
    private float stuckTimer;
    private bool defaultAgentUpdateRotation;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
        defaultAgentUpdateRotation = agent.updateRotation;
        if (!animator)
        {
            animator = GetComponent<Animator>();
        }
    }

    private void OnEnable()
    {
        waitTimer = 0f;
        stuckTimer = 0f;
    }

    private void Update()
    {
        if (!agent)
        {
            UpdateAnimatorParameters(Vector3.zero, true);
            return;
        }

        TryResolvePlayerTarget();
        if (ShouldStopAndLookAtPlayer())
        {
            StopAndLookAtPlayer();
            UpdateAnimatorParameters(agent.velocity, true);
            return;
        }
        RestoreAgentRotationForWander();

        if (!agent.isOnNavMesh)
        {
            TrySnapToNavMesh();
            UpdateAnimatorParameters(Vector3.zero, true);
            return;
        }

        if (agent.pathPending)
        {
            UpdateAnimatorParameters(agent.velocity, agent.isStopped);
            return;
        }

        if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.1f)
        {
            HandleStuckRecovery();
            UpdateAnimatorParameters(agent.velocity, false);
            return;
        }

        stuckTimer = 0f;

        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            UpdateAnimatorParameters(Vector3.zero, true);
            return;
        }

        if (TryGetRandomPoint(transform.position, wanderRadius, out Vector3 destination))
        {
            agent.isStopped = false;
            agent.SetDestination(destination);
            waitTimer = Random.Range(minWaitTime, maxWaitTime);
        }

        UpdateAnimatorParameters(agent.velocity, agent.isStopped || waitTimer > 0f);
    }

    private bool TryGetRandomPoint(Vector3 center, float radius, out Vector3 result)
    {
        for (int i = 0; i < maxTriesPerDestination; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * radius;
            Vector3 candidate = center + randomOffset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
            {
                continue;
            }

            if (Vector3.Distance(center, hit.position) < minDestinationDistance)
            {
                continue;
            }

            if (!agent.CalculatePath(hit.position, path))
            {
                continue;
            }

            if (path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            result = hit.position;
            return true;
        }

        result = center;
        return false;
    }

    private void HandleStuckRecovery()
    {
        if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            agent.ResetPath();
            waitTimer = 0f;
            stuckTimer = 0f;
            return;
        }

        if (agent.velocity.sqrMagnitude > stuckVelocityThreshold * stuckVelocityThreshold)
        {
            stuckTimer = 0f;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer < stuckTime)
        {
            return;
        }

        agent.ResetPath();
        waitTimer = 0f;
        stuckTimer = 0f;
    }

    private void TrySnapToNavMesh()
    {
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
        {
            return;
        }

        agent.Warp(hit.position);
        stuckTimer = 0f;
    }

    private void OnValidate()
    {
        wanderRadius = Mathf.Max(0.1f, wanderRadius);
        sampleDistance = Mathf.Max(0.5f, sampleDistance);
        maxTriesPerDestination = Mathf.Max(1, maxTriesPerDestination);
        minDestinationDistance = Mathf.Max(0.1f, minDestinationDistance);
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
        stuckVelocityThreshold = Mathf.Max(0.001f, stuckVelocityThreshold);
        stuckTime = Mathf.Max(0.1f, stuckTime);
        stopAndLookRadius = Mathf.Max(0.1f, stopAndLookRadius);
        lookRotateSpeed = Mathf.Max(0.1f, lookRotateSpeed);
        animationSpeedMultiplier = Mathf.Max(0.1f, animationSpeedMultiplier);
    }

    public void Pause(float duration)
    {
        if (!agent)
        {
            return;
        }

        waitTimer = Mathf.Max(waitTimer, duration);
        stuckTimer = 0f;
        agent.ResetPath();
    }

    private void TryResolvePlayerTarget()
    {
        if (playerTarget)
        {
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject)
        {
            playerTarget = playerObject.transform;
        }
    }

    private bool ShouldStopAndLookAtPlayer()
    {
        if (!playerTarget)
        {
            return false;
        }

        return Vector3.Distance(transform.position, playerTarget.position) <= stopAndLookRadius;
    }

    private void StopAndLookAtPlayer()
    {
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            if (agent.hasPath)
            {
                agent.ResetPath();
            }
        }

        waitTimer = 0f;
        stuckTimer = 0f;

        agent.updateRotation = false;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lookRotateSpeed * Time.deltaTime);
    }

    private void RestoreAgentRotationForWander()
    {
        agent.updateRotation = defaultAgentUpdateRotation;
    }

    private void UpdateAnimatorParameters(Vector3 velocity, bool isImmobile)
    {
        if (!animator)
        {
            return;
        }

        Vector3 localVelocity = transform.worldToLocalMatrix.MultiplyVector(velocity) * animationSpeedMultiplier;

        animator.SetFloat("SpeedX", localVelocity.x, 0.1f, Time.deltaTime);
        animator.SetFloat("SpeedY", localVelocity.z, 0.1f, Time.deltaTime);
        animator.SetFloat("FallSpeed", velocity.y, 0.1f, Time.deltaTime);
        animator.SetBool("IsGrounded", true);
        animator.SetBool("IsImmobalized", isImmobile);
    }
}
