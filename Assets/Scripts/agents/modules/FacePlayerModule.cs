// Stops movement and faces the nearest entity matching a faction relationship when inside triggerRadius.
// Yields to higher-priority modules (combat, chase) automatically.
using UnityEngine;

public class FacePlayerModule : BehaviourModuleBase
{
    [Tooltip("Faction relationship the nearest candidate must have. Requires EntityFaction on both entities.")]
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Allied;
    [SerializeField] private float triggerRadius = 6f;

    private Transform target;
    private EntityFaction selfFaction;

    private void Awake() => selfFaction = GetComponent<EntityFaction>();
    private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

    public override string ModuleDescription =>
        "Stops and faces the nearest entity of the configured faction relationship when it steps within triggerRadius. " +
        "Yields to any higher-priority module (chase, combat, etc.) automatically.\n\n" +
        "• triggerRadius — distance at which the entity turns to face the target\n" +
        "• requiredRelationship — faction relationship the nearest candidate must have (default: Allied)";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();

        if (!target)
            return null;

        if (Vector3.Distance(context.Position, target.position) > triggerRadius)
            return null;

        return MoveIntent.StopAndFace(target.position);
    }

    private void TryResolveTarget()
    {
        if (target) return;
        target = EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship, transform.position);
    }

    protected override void OnValidate()
    {
        triggerRadius = Mathf.Max(0.1f, triggerRadius);
    }
}
