using UnityEngine;
using UnityEngine.AI;

public class WanderBehaviour : MonoBehaviour
{
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float sampleDistance = 6f;
    [SerializeField] private int maxTriesPerDestination = 10;
    [SerializeField] private float minDestinationDistance = 1.5f;
    [SerializeField] private float minWaitTime = 0.5f;
    [SerializeField] private float maxWaitTime = 2f;

    private bool hasDestination;
    private Vector3 currentDestination;
    private float waitTimer;

    public bool Tick(Vector3 origin, bool reachedDestination, float deltaTime, out Vector3 destination)
    {
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
            if (!TryGetRandomPoint(origin, out currentDestination))
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
    }

    private bool TryGetRandomPoint(Vector3 origin, out Vector3 destination)
    {
        for (int i = 0; i < maxTriesPerDestination; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * wanderRadius;
            Vector3 candidate = origin + randomOffset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
            {
                continue;
            }

            if (Vector3.Distance(origin, hit.position) < minDestinationDistance)
            {
                continue;
            }

            destination = hit.position;
            return true;
        }

        destination = origin;
        return false;
    }

    private void OnValidate()
    {
        wanderRadius = Mathf.Max(0.1f, wanderRadius);
        sampleDistance = Mathf.Max(0.5f, sampleDistance);
        maxTriesPerDestination = Mathf.Max(1, maxTriesPerDestination);
        minDestinationDistance = Mathf.Max(0.1f, minDestinationDistance);
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
    }
}
