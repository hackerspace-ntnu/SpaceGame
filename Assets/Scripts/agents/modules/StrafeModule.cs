// Sidesteps around a target while within engagement range.
// Makes ranged enemies feel dynamic instead of standing still while shooting.
// Pair with RangedAttackModule and KeepDistanceModule.
using UnityEngine;
using UnityEngine.AI;

public class StrafeModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Strafe")]
    [SerializeField] private float engageRange = 12f;
    [SerializeField] private float strafeRadius = 5f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float stopDistance = 0.3f;
    [Tooltip("Stop strafing when target is closer than this — lets ChaseModule take over for melee. Set to match ChaseModule.attackRange.")]
    [SerializeField] private float minStrafeDistance = 2f;
    [SerializeField] private float directionChangeInterval = 2f;
    [SerializeField] private float navMeshSampleDistance = 3f;

    private float directionTimer;
    private float strafeAngle;
    private int strafeDir = 1;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

    private void OnEnable()
    {
        directionTimer = 0f;
        strafeAngle = Random.Range(0f, 360f);
        strafeDir = Random.value > 0.5f ? 1 : -1;
    }

    public override string ModuleDescription =>
        "Orbits a target at a fixed radius while within engageRange. Periodically reverses direction. Makes ranged enemies feel dynamic.\n\n" +
        "• engageRange — only strafes when target is within this distance\n" +
        "• minStrafeDistance — yields below this range so ChaseModule can stop-and-face for melee. Match to ChaseModule.attackRange.\n" +
        "• strafeRadius — orbit distance around the target\n" +
        "• directionChangeInterval — seconds between direction reversals\n" +
        "• Pair with RangedAttackModule and KeepDistanceModule for a full ranged fighter";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance > engageRange || distance < minStrafeDistance)
            return null;

        directionTimer -= deltaTime;
        if (directionTimer <= 0f)
        {
            directionTimer = directionChangeInterval;
            strafeDir = -strafeDir;
        }

        // Orbit around the target at strafeRadius
        strafeAngle += strafeDir * 40f * deltaTime;
        Vector3 offset = new Vector3(Mathf.Sin(strafeAngle * Mathf.Deg2Rad), 0f, Mathf.Cos(strafeAngle * Mathf.Deg2Rad));
        Vector3 candidate = target.position + offset * strafeRadius;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            return MoveIntent.MoveTo(hit.position, stopDistance, speedMultiplier);

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
        engageRange = Mathf.Max(0.1f, engageRange);
        minStrafeDistance = Mathf.Max(0f, minStrafeDistance);
        strafeRadius = Mathf.Max(0.1f, strafeRadius);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        directionChangeInterval = Mathf.Max(0.1f, directionChangeInterval);
        navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
    }
}
