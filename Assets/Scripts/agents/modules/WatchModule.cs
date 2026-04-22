// Stops and faces a target when it comes within detectRadius. Does not move.
// Good for curious NPCs, security cameras, sentry turret bases, etc.
using UnityEngine;

public class WatchModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("Faction relationship the nearest candidate must have. Requires EntityFaction on both entities.")]
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Neutral;

    [Header("Range")]
    [SerializeField] private float detectRadius = 5f;

    private EntityFaction selfFaction;

    private void Awake() => selfFaction = GetComponent<EntityFaction>();
    private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

    public override string ModuleDescription =>
        "Stops and faces a nearby target without moving. Good for curious NPCs, sentry turrets, or anything that should track a target with its gaze.\n\n" +
        "• detectRadius — range within which the entity turns to look\n" +
        "• requiredRelationship — faction relationship the nearest candidate must have (default: Neutral)";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        if (Vector3.Distance(context.Position, target.position) > detectRadius)
            return null;

        return MoveIntent.StopAndFace(target.position);
    }

    private void TryResolveTarget()
    {
        if (target)
            return;
        target = EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship, transform.position);
    }

    protected override void OnValidate()
    {
        detectRadius = Mathf.Max(0.1f, detectRadius);
    }
}
