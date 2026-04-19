// Walks toward a target and stops at conversationDistance. Faces target once arrived.
// Good for friendly NPCs, bounty hunters greeting the player, vendors walking over, etc.
using UnityEngine;

public class ApproachModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Neutral;

    [Header("Range")]
    [SerializeField] private float detectRadius = 6f;
    [SerializeField] private float conversationDistance = 1.6f;

    [Header("Movement")]
    [SerializeField] private float speedMultiplier = 1.1f;

    private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

    public override string ModuleDescription =>
        "Walks toward a target and stops at conversationDistance. Faces the target once arrived. Good for friendly NPCs or vendors.\n\n" +
        "• detectRadius — how close the target must be to trigger approach\n" +
        "• conversationDistance — how far away to stop\n" +
        "• speedMultiplier — walk speed while approaching";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);
        if (distance > detectRadius)
            return null;

        if (distance <= conversationDistance)
            return MoveIntent.StopAndFace(target.position);

        return MoveIntent.MoveTo(target.position, conversationDistance, speedMultiplier);
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
        detectRadius = Mathf.Max(0.1f, detectRadius);
        conversationDistance = Mathf.Max(0.1f, conversationDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
    }
}
