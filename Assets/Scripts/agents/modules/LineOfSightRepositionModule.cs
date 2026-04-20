// Steers the agent to a position with clear line of sight to its target when the
// current position is obstructed. Runs at Ambient priority — only kicks in when
// no higher-priority module (chase, strafe) is already moving the agent.
// Pair with RangedAttackModule so blocked agents reposition before firing.
using UnityEngine;
using UnityEngine.AI;

public class LineOfSightRepositionModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Line of Sight")]
    [Tooltip("Layer mask for obstacles that block line of sight.")]
    [SerializeField] private LayerMask obstacleMask = Physics.DefaultRaycastLayers;
    [Tooltip("How far from the agent's position to search for a clear spot.")]
    [SerializeField] private float searchRadius = 5f;
    [Tooltip("How many candidate positions to try per search.")]
    [SerializeField] private int searchAttempts = 8;
    [Tooltip("Only reposition if target is within this range.")]
    [SerializeField] private float maxRange = 20f;

    [Header("Movement")]
    [SerializeField] private float speedMultiplier = 1.2f;
    [SerializeField] private float stopDistance = 0.5f;

    private Vector3? repositionTarget;
    private ChaseModule chaseModule;

    private void Reset() => SetPriorityDefault(ModulePriority.Ambient);
    private void Awake() => chaseModule = GetComponent<ChaseModule>();
    private void OnEnable() => repositionTarget = null;

    public override string ModuleDescription =>
        "Moves the agent to a nearby position with clear line of sight when the target is obstructed. " +
        "Only activates when no higher-priority module is moving the agent.\n\n" +
        "• obstacleMask — layers considered as LoS blockers\n" +
        "• searchRadius — how far to search for a clear position\n" +
        "• searchAttempts — candidate positions tried per search\n" +
        "• maxRange — only repositions when target is within this distance\n" +
        "• Pair with RangedAttackModule so blocked agents move before firing";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (chaseModule != null && !chaseModule.HasTarget)
        {
            repositionTarget = null;
            return null;
        }

        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance > maxRange)
        {
            repositionTarget = null;
            return null;
        }

        // Aim ray from eye level.
        Vector3 eyePos = context.Position + Vector3.up * 1.5f;
        Vector3 targetPos = target.position + Vector3.up * 1f;

        bool hasLoS = !Physics.Linecast(eyePos, targetPos, obstacleMask);

        if (hasLoS)
        {
            repositionTarget = null;
            return null;
        }

        // Already moving to a reposition point — check if it's still valid.
        if (repositionTarget.HasValue)
        {
            Vector3 repEye = repositionTarget.Value + Vector3.up * 1.5f;
            bool repHasLoS = !Physics.Linecast(repEye, targetPos, obstacleMask);
            bool arrived = Vector3.Distance(context.Position, repositionTarget.Value) <= stopDistance + 0.1f;

            if (arrived || !repHasLoS)
                repositionTarget = null;
            else
                return MoveIntent.MoveTo(repositionTarget.Value, stopDistance, speedMultiplier);
        }

        // Search for a nearby NavMesh point with clear LoS.
        for (int i = 0; i < searchAttempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = context.Position + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                continue;

            Vector3 candidateEye = hit.position + Vector3.up * 1.5f;
            if (Physics.Linecast(candidateEye, targetPos, obstacleMask))
                continue;

            repositionTarget = hit.position;
            return MoveIntent.MoveTo(repositionTarget.Value, stopDistance, speedMultiplier);
        }

        return null;
    }

    private void TryResolveTarget()
    {
        if (target)
            return;
        Transform candidate = EntityTargetRegistry.Resolve(targetTag, transform.position);
        if (candidate && EntityFaction.IsValidTarget(transform, candidate, requiredRelationship))
            target = candidate;
    }

    protected override void OnValidate()
    {
        searchRadius   = Mathf.Max(0.5f, searchRadius);
        searchAttempts = Mathf.Max(1, searchAttempts);
        maxRange       = Mathf.Max(0.1f, maxRange);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        stopDistance   = Mathf.Max(0.01f, stopDistance);
    }
}
