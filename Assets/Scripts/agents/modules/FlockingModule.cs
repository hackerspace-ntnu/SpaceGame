// Classic three-rule flocking (separation, alignment, cohesion) using nearby agent data
// supplied by AgentController. Enable nearbyAgentScanRadius on the AgentController for this to work.
// Great for herds, packs, bounty hunter squads, swarm enemies.
using UnityEngine;
using UnityEngine.AI;

public class FlockingModule : BehaviourModuleBase
{
    [Header("Weights")]
    [SerializeField] private float separationWeight = 2f;
    [SerializeField] private float cohesionWeight = 1f;
    [SerializeField] private float alignmentWeight = 0.5f;

    [Header("Radii")]
    [SerializeField] private float separationRadius = 2f;
    [SerializeField] private float perceptionRadius = 8f;

    [Header("Movement")]
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float stopDistance = 0.3f;
    [SerializeField] private float navMeshSampleDistance = 3f;

    [Header("Minimum Neighbours")]
    [Tooltip("Minimum neighbours needed before flocking activates. Prevents single-entity jitter.")]
    [SerializeField] private int minNeighbours = 1;

    private void Reset() => SetPriorityDefault(ModulePriority.Social);

    public override string ModuleDescription =>
        "Classic three-rule flocking: separation (don't crowd), cohesion (stay together), alignment (match velocity). Good for robot bands, wildlife herds, hunting parties.\n\n" +
        "Requires: AgentController.nearbyAgentScanRadius > 0 and nearbyAgentLayer set.\n\n" +
        "• separationRadius — minimum distance before pushing apart\n" +
        "• perceptionRadius — range within which neighbours count for cohesion\n" +
        "• separationWeight / cohesionWeight / alignmentWeight — tune flock behaviour\n" +
        "• minNeighbours — minimum nearby agents before flocking activates";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (context.NearbyAgentPositions == null || context.NearbyAgentCount < minNeighbours)
            return null;

        Vector3 separation = Vector3.zero;
        Vector3 cohesionSum = Vector3.zero;
        Vector3 velocitySum = Vector3.zero;
        int cohesionCount = 0;

        for (int i = 0; i < context.NearbyAgentCount; i++)
        {
            Vector3 neighbourPos = context.NearbyAgentPositions[i];
            Vector3 toNeighbour = neighbourPos - context.Position;
            float dist = toNeighbour.magnitude;

            if (dist < separationRadius && dist > 0.001f)
                separation -= toNeighbour.normalized / dist;

            if (dist < perceptionRadius)
            {
                cohesionSum += neighbourPos;
                if (context.NearbyAgentVelocities != null)
                    velocitySum += context.NearbyAgentVelocities[i];
                cohesionCount++;
            }
        }

        if (cohesionCount < minNeighbours)
            return null;

        Vector3 cohesion = (cohesionSum / cohesionCount) - context.Position;
        cohesion.y = 0f;
        separation.y = 0f;

        // True alignment: steer toward the average velocity of nearby neighbours.
        Vector3 avgVelocity = velocitySum / cohesionCount;
        avgVelocity.y = 0f;
        Vector3 alignment = avgVelocity.sqrMagnitude > 0.001f ? avgVelocity.normalized : Vector3.zero;

        Vector3 desired = separation * separationWeight
                        + cohesion.normalized * cohesionWeight
                        + alignment * alignmentWeight;

        desired.y = 0f;

        if (desired.sqrMagnitude < 0.001f)
            return null;

        Vector3 candidate = context.Position + desired.normalized * perceptionRadius * 0.5f;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            return MoveIntent.MoveTo(hit.position, stopDistance, speedMultiplier);

        return null;
    }

    protected override void OnValidate()
    {
        separationWeight = Mathf.Max(0f, separationWeight);
        cohesionWeight = Mathf.Max(0f, cohesionWeight);
        alignmentWeight = Mathf.Max(0f, alignmentWeight);
        separationRadius = Mathf.Max(0.1f, separationRadius);
        perceptionRadius = Mathf.Max(separationRadius, perceptionRadius);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
        minNeighbours = Mathf.Max(1, minNeighbours);
    }
}
