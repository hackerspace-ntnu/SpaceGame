using UnityEngine;
using UnityEngine.AI;

public enum PatrolSelectionMode
{
    SequentialLoop,
    PingPong,
    Random
}

public class WanderBehaviour : MonoBehaviour
{
    [Header("Wander")]
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float sampleDistance = 6f;
    [SerializeField] private int maxTriesPerDestination = 10;
    [SerializeField] private float minDestinationDistance = 1.5f;

    [Header("Wait")]
    [SerializeField] private float minWaitTime = 0.5f;
    [SerializeField] private float maxWaitTime = 2f;

    [Header("Patrol Points")]
    [SerializeField] private bool usePatrolPoints;
    [SerializeField] private PatrolSelectionMode patrolSelectionMode = PatrolSelectionMode.SequentialLoop;
    [SerializeField, Range(0f, 1f)] private float patrolPointChance = 1f;
    [SerializeField] private Transform[] patrolPoints;

    [Header("Leash")]
    [SerializeField] private bool useSpawnAsAnchor = true;
    [SerializeField] private Transform anchorTransform;
    [SerializeField] private float leashRadius;

    private bool hasDestination;
    private Vector3 currentDestination;
    private float waitTimer;
    private int patrolIndex;
    private int patrolDirection = 1;
    private Vector3 spawnAnchor;
    private bool hasSpawnAnchor;

    public bool Tick(Vector3 origin, bool reachedDestination, float deltaTime, out Vector3 destination)
    {
        if (!hasSpawnAnchor)
        {
            spawnAnchor = transform.position;
            hasSpawnAnchor = true;
        }

        if (hasDestination && reachedDestination)
        {
            hasDestination = false;
            waitTimer = Random.Range(minWaitTime, maxWaitTime);
        }

        if (waitTimer > 0f)
        {
            waitTimer -= deltaTime;
            destination = origin;
            return false;
        }

        if (!hasDestination)
        {
            if (!TryGetNextDestination(origin, out currentDestination))
            {
                destination = origin;
                return false;
            }

            hasDestination = true;
        }

        destination = currentDestination;
        return true;
    }

    public void ResetState()
    {
        hasDestination = false;
        waitTimer = 0f;
        patrolIndex = 0;
        patrolDirection = 1;
    }

    private bool TryGetNextDestination(Vector3 origin, out Vector3 destination)
    {
        if (ShouldUsePatrolPoints() && TryGetPatrolPoint(origin, out destination))
        {
            return true;
        }

        return TryGetRandomPoint(origin, out destination);
    }

    private bool ShouldUsePatrolPoints()
    {
        if (!usePatrolPoints || patrolPoints == null || patrolPoints.Length == 0)
        {
            return false;
        }

        return Random.value <= patrolPointChance;
    }

    private bool TryGetPatrolPoint(Vector3 origin, out Vector3 destination)
    {
        int attempts = patrolPoints.Length;
        for (int i = 0; i < attempts; i++)
        {
            int nextIndex = GetNextPatrolIndex(i == 0);
            Transform patrolPoint = patrolPoints[nextIndex];
            if (!patrolPoint)
            {
                continue;
            }

            if (!TrySampleOnNavMesh(patrolPoint.position, out Vector3 sampledPoint))
            {
                continue;
            }

            if (Vector3.Distance(origin, sampledPoint) < minDestinationDistance)
            {
                continue;
            }

            destination = sampledPoint;
            return true;
        }

        destination = origin;
        return false;
    }

    private int GetNextPatrolIndex(bool advance)
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            return 0;
        }

        if (patrolSelectionMode == PatrolSelectionMode.Random)
        {
            return Random.Range(0, patrolPoints.Length);
        }

        int current = Mathf.Clamp(patrolIndex, 0, patrolPoints.Length - 1);
        if (!advance)
        {
            return current;
        }

        if (patrolSelectionMode == PatrolSelectionMode.SequentialLoop)
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            return current;
        }

        if (patrolPoints.Length == 1)
        {
            return 0;
        }

        patrolIndex += patrolDirection;
        if (patrolIndex >= patrolPoints.Length)
        {
            patrolIndex = patrolPoints.Length - 2;
            patrolDirection = -1;
        }
        else if (patrolIndex < 0)
        {
            patrolIndex = 1;
            patrolDirection = 1;
        }

        return current;
    }

    private bool TryGetRandomPoint(Vector3 origin, out Vector3 destination)
    {
        Vector3 center = GetWanderCenter(origin);

        for (int i = 0; i < maxTriesPerDestination; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * wanderRadius;
            Vector3 candidate = center + randomOffset;

            if (!TrySampleOnNavMesh(candidate, out Vector3 sampledPoint))
            {
                continue;
            }

            if (Vector3.Distance(origin, sampledPoint) < minDestinationDistance)
            {
                continue;
            }

            destination = sampledPoint;
            return true;
        }

        destination = origin;
        return false;
    }

    private Vector3 GetWanderCenter(Vector3 fallbackOrigin)
    {
        if (leashRadius > 0f)
        {
            return GetAnchorPosition(fallbackOrigin);
        }

        return fallbackOrigin;
    }

    private Vector3 GetAnchorPosition(Vector3 fallbackOrigin)
    {
        if (anchorTransform)
        {
            return anchorTransform.position;
        }

        if (useSpawnAsAnchor)
        {
            if (!hasSpawnAnchor)
            {
                spawnAnchor = transform.position;
                hasSpawnAnchor = true;
            }

            return spawnAnchor;
        }

        return fallbackOrigin;
    }

    private bool TrySampleOnNavMesh(Vector3 candidate, out Vector3 position)
    {
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
        {
            position = candidate;
            return false;
        }

        if (leashRadius > 0f)
        {
            Vector3 anchor = GetAnchorPosition(transform.position);
            if (Vector3.Distance(anchor, hit.position) > leashRadius)
            {
                position = hit.position;
                return false;
            }
        }

        position = hit.position;
        return true;
    }

    private void OnValidate()
    {
        wanderRadius = Mathf.Max(0.1f, wanderRadius);
        sampleDistance = Mathf.Max(0.5f, sampleDistance);
        maxTriesPerDestination = Mathf.Max(1, maxTriesPerDestination);
        minDestinationDistance = Mathf.Max(0.1f, minDestinationDistance);
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
        patrolPointChance = Mathf.Clamp01(patrolPointChance);
        leashRadius = Mathf.Max(0f, leashRadius);
    }
}
