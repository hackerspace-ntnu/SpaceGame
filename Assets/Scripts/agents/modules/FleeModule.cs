// Runs away from a threat (tag or direct reference) when it comes within triggerRadius.
// Deactivates itself once the threat is beyond safeRadius (hysteresis).
using UnityEngine;
using UnityEngine.AI;

public class FleeModule : BehaviourModuleBase
{
    [Header("Threat")]
    [SerializeField] private Transform threat;
    [SerializeField] private string threatTag = "Player";
    [SerializeField] private FactionRelationship fleeFromRelationship = FactionRelationship.Hostile;

    [Header("Ranges")]
    [SerializeField] private float triggerRadius = 6f;
    [SerializeField] private float safeRadius = 10f;

    [Header("Movement")]
    [SerializeField] private float fleeSpeedMultiplier = 1.4f;
    [SerializeField] private float stopDistance = 0.2f;
    [SerializeField] private float navMeshSampleDistance = 4f;

    private bool fleeing;

    private void Reset() => SetPriorityDefault(ModulePriority.Override);
    private void OnEnable() => fleeing = false;

    public override string ModuleDescription =>
        "Runs away from a threat when it comes within triggerRadius. Stops fleeing once beyond safeRadius.\n\n" +
        "• triggerRadius — threat must enter this range to start fleeing\n" +
        "• safeRadius — entity stops fleeing once threat is this far away\n" +
        "• fleeSpeedMultiplier — movement speed boost while fleeing\n" +
        "• threatTag — tag of the object to flee from (default: Player)";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveThreat();
        if (!threat)
            return null;

        float distance = Vector3.Distance(context.Position, threat.position);

        if (!fleeing && distance <= triggerRadius)
            fleeing = true;
        else if (fleeing && distance > safeRadius)
            fleeing = false;

        if (!fleeing)
            return null;

        if (TryGetFleeDestination(context.Position, threat.position, out Vector3 dest))
            return MoveIntent.MoveTo(dest, stopDistance, fleeSpeedMultiplier);

        return MoveIntent.StopAndFace(threat.position);
    }

    private bool TryGetFleeDestination(Vector3 self, Vector3 threatPos, out Vector3 destination)
    {
        Vector3 away = self - threatPos;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = Random.insideUnitSphere;
            away.y = 0f;
        }

        Vector3 candidate = self + away.normalized * safeRadius;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            destination = hit.position;
            return true;
        }

        destination = self;
        return false;
    }

    private void TryResolveThreat()
    {
        if (threat)
            return;
        Transform candidate = EntityTargetRegistry.Resolve(threatTag, transform.position);
        if (candidate && EntityFaction.IsValidTarget(transform, candidate, fleeFromRelationship))
            threat = candidate;
    }

    protected override void OnValidate()
    {
        triggerRadius = Mathf.Max(0.1f, triggerRadius);
        safeRadius = Mathf.Max(triggerRadius, safeRadius);
        fleeSpeedMultiplier = Mathf.Max(0.01f, fleeSpeedMultiplier);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
    }
}
