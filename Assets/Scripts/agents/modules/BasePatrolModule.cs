// Patrols random NavMesh points within a radius around a fixed base position.
// Pair with HerdModule at Social priority for group movement.
using UnityEngine;
using UnityEngine.AI;

public class BasePatrolModule : BehaviourModuleBase
{
    [Header("Base Patrol")]
    [SerializeField] private Transform baseTransform;
    [SerializeField] private float patrolRadius = 80f;
    [SerializeField] private float sampleDistance = 8f;
    [SerializeField] private float minDestinationDistance = 8f;

    [Header("Wait")]
    [SerializeField] private float minWaitTime = 0.4f;
    [SerializeField] private float maxWaitTime = 2.5f;

    [Header("Movement")]
    [SerializeField] private float stopDistance = 0.8f;
    [SerializeField] private float speedMultiplier = 1f;

    private enum State { Waiting, Moving }

    private bool hasSpawnAnchor;
    private Vector3 spawnAnchor;
    private State state = State.Waiting;
    private Vector3 destination;
    private float waitTimer;
    private IMovementMotor motor;

    private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

    private void Start()
    {
        var controller = GetComponent<AgentController>();
        if (controller != null)
            motor = controller.Motor;
    }

    public override string ModuleDescription =>
        "Patrols random NavMesh points around a base transform (or spawn position if none assigned).\n\n" +
        "• baseTransform — center of the patrol area; uses spawn point if empty\n" +
        "• patrolRadius — how far from the base the entity can roam\n" +
        "• minDestinationDistance — minimum pick distance from current position\n" +
        "• minWaitTime / maxWaitTime — pause duration between destinations\n\n" +
        "Pair with HerdModule at Social priority to keep a group loosely together.";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        EnsureAnchor();

        switch (state)
        {
            case State.Waiting:
                // If the herd suggested a destination while we're idle, follow it immediately.
                if (motor?.CurrentDestination is Vector3 suggested)
                {
                    destination = suggested;
                    state = State.Moving;
                    return MoveIntent.MoveTo(destination, stopDistance, speedMultiplier);
                }
                waitTimer -= deltaTime;
                if (waitTimer <= 0f)
                    TryBeginMove();
                return null;

            case State.Moving:
                if (Vector3.Distance(context.Position, destination) <= stopDistance + 0.1f)
                {
                    state = State.Waiting;
                    waitTimer = Random.Range(minWaitTime, maxWaitTime);
                    return null;
                }
                return MoveIntent.MoveTo(destination, stopDistance, speedMultiplier);
        }

        return null;
    }

    private void TryBeginMove()
    {
        if (!TryPickDestination(out Vector3 picked))
            return;

        destination = picked;
        state = State.Moving;
    }

    private void EnsureAnchor()
    {
        if (hasSpawnAnchor)
            return;

        spawnAnchor = transform.position;
        hasSpawnAnchor = true;
    }

    private bool TryPickDestination(out Vector3 picked)
    {
        Vector3 center = GetBasePosition();

        for (int i = 0; i < 16; i++)
        {
            Vector2 circle = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
                continue;

            if (!IsInsidePatrolRadius(hit.position))
                continue;

            if (Vector3.Distance(transform.position, hit.position) < minDestinationDistance)
                continue;

            picked = hit.position;
            return true;
        }

        picked = transform.position;
        return false;
    }

    private Vector3 GetBasePosition() => baseTransform ? baseTransform.position : spawnAnchor;

    private bool IsInsidePatrolRadius(Vector3 point)
    {
        Vector3 offset = point - GetBasePosition();
        offset.y = 0f;
        return offset.sqrMagnitude <= patrolRadius * patrolRadius;
    }

    protected override void OnValidate()
    {
        patrolRadius = Mathf.Max(0.1f, patrolRadius);
        sampleDistance = Mathf.Max(0.5f, sampleDistance);
        minDestinationDistance = Mathf.Max(0.1f, minDestinationDistance);
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
    }
}
