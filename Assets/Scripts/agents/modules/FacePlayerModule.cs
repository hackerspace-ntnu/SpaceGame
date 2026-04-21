// Stops movement and faces the player when they enter triggerRadius.
// Yields to higher-priority modules (combat, chase) automatically.
using UnityEngine;

public class FacePlayerModule : BehaviourModuleBase
{
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private float triggerRadius = 6f;

    private Transform target;

    private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

    public override string ModuleDescription =>
        "Stops and faces the player when they step within triggerRadius. " +
        "Yields to any higher-priority module (chase, combat, etc.) automatically.\n\n" +
        "• triggerRadius — distance at which the entity turns to face the player";

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
        target = EntityTargetRegistry.Resolve(targetTag, transform.position);
    }

    protected override void OnValidate()
    {
        triggerRadius = Mathf.Max(0.1f, triggerRadius);
    }
}
