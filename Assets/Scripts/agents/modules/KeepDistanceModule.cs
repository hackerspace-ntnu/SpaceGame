// Maintains a comfortable radius from a threat. Backs away if too close, faces otherwise.
// Good for skittish creatures, cowardly NPCs, ranged enemies that kite.
using UnityEngine;
using UnityEngine.AI;

public class KeepDistanceModule : BehaviourModuleBase
{
    [Header("Target")]
    [Tooltip("Faction relationship the target must have. Ignored when the agent has an " +
             "AgentTargeting component — kiting the entity the agent is fighting is the only " +
             "sensible reading, and a separate query here is how agents ended up backing away " +
             "from one enemy while shooting a different one.")]
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Ranges")]
    [SerializeField] private float detectRadius = 8f;
    [SerializeField] private float preferredDistance = 4f;

    [Header("Movement")]
    [SerializeField] private float speedMultiplier = 1.2f;
    [SerializeField] private float stopDistance = 0.2f;
    [SerializeField] private float navMeshSampleDistance = 4f;

    private EntityFaction selfFaction;
    private Transform target;
    private float retargetTimer;

    private void Awake() => selfFaction = GetComponent<EntityFaction>();
    private void Reset() => SetPriorityDefault(ModulePriority.Ambient);
    private void OnEnable() { target = null; retargetTimer = 0f; }

    public override string ModuleDescription =>
        "Maintains a preferred distance from a target. Backs away if too close, faces the target otherwise. Good for ranged enemies that kite.\n\n" +
        "• detectRadius — range at which the module activates\n" +
        "• preferredDistance — desired gap between entity and target\n" +
        "• Pair with AgentRangedCombatModule to shoot while backing away";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        AgentTargeting targeting = context.Targeting;
        float distance;

        if (targeting != null)
        {
            if (!targeting.HasTarget)
                return null;
            target = targeting.Target;
            distance = targeting.DistanceToTarget;
        }
        else
        {
            target = TargetResolution.Refresh(target, ref retargetTimer, 0.5f, deltaTime,
                                              selfFaction, requiredRelationship, context.Position);
            if (target == null)
                return null;
            distance = Vector3.Distance(context.Position, target.position);
        }

        if (distance > detectRadius)
            return null;

        if (distance < preferredDistance)
        {
            if (TryGetPositionAwayFrom(context.Position, target.position, out Vector3 dest))
                return MoveIntent.MoveTo(dest, stopDistance, speedMultiplier);
        }

        return MoveIntent.StopAndFace(target.position);
    }

    private bool TryGetPositionAwayFrom(Vector3 self, Vector3 threatPos, out Vector3 destination)
    {
        Vector3 away = self - threatPos;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = Random.insideUnitSphere;
            away.y = 0f;
        }
        Vector3 candidate = self + away.normalized * preferredDistance;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            destination = hit.position;
            return true;
        }
        destination = self;
        return false;
    }

    protected override void OnValidate()
    {
        detectRadius = Mathf.Max(0.1f, detectRadius);
        preferredDistance = Mathf.Max(0.1f, preferredDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
    }
}
