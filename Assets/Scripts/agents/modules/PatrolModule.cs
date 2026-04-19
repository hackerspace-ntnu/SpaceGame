// Moves between a list of patrol points in sequence, ping-pong, or random order.
// Waits at each point for a configurable duration before moving on.
// Does not need a WanderBehaviour — all logic is self-contained.
using UnityEngine;
using UnityEngine.AI;

public class PatrolModule : BehaviourModuleBase
{
    [Header("Patrol Points")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private PatrolSelectionMode selectionMode = PatrolSelectionMode.SequentialLoop;

    [Header("Timing")]
    [SerializeField] private float minWaitTime = 1f;
    [SerializeField] private float maxWaitTime = 3f;

    [Header("Movement")]
    [SerializeField] private float stopDistance = 0.4f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float navMeshSampleDistance = 3f;

    private int patrolIndex;
    private int patrolDirection = 1;
    private float waitTimer;
    private Vector3? currentDestination;

    private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

    private void OnEnable()
    {
        patrolIndex = 0;
        patrolDirection = 1;
        waitTimer = 0f;
        currentDestination = null;
    }

    public override string ModuleDescription =>
        "Moves between a list of assigned patrol point Transforms. Waits at each point before moving on.\n\n" +
        "• patrolPoints — assign the Transforms to visit\n" +
        "• selectionMode — SequentialLoop, PingPong, or Random order\n" +
        "• minWaitTime / maxWaitTime — how long to pause at each point";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return null;

        if (waitTimer > 0f)
        {
            waitTimer -= deltaTime;
            return MoveIntent.Idle(); // Hold the frame — don't let lower modules wander during the pause.
        }

        if (!currentDestination.HasValue)
            currentDestination = GetNextPoint();

        if (!currentDestination.HasValue)
            return null;

        if (context.HasReachedDestination)
        {
            currentDestination = null;
            waitTimer = Random.Range(minWaitTime, maxWaitTime);
            return MoveIntent.Idle();
        }

        return MoveIntent.MoveTo(currentDestination.Value, stopDistance, speedMultiplier);
    }

    private Vector3? GetNextPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return null;

        int index = AdvanceIndex();
        Transform point = patrolPoints[index];
        if (!point)
            return null;

        if (NavMesh.SamplePosition(point.position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            return hit.position;

        return null;
    }

    private int AdvanceIndex()
    {
        if (selectionMode == PatrolSelectionMode.Random)
            return Random.Range(0, patrolPoints.Length);

        int current = Mathf.Clamp(patrolIndex, 0, patrolPoints.Length - 1);

        if (selectionMode == PatrolSelectionMode.SequentialLoop)
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            return current;
        }

        // PingPong
        if (patrolPoints.Length == 1)
            return 0;

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

    protected override void OnValidate()
    {
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
    }
}
