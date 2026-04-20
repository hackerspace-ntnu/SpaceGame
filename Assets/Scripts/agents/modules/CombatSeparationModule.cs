// Pushes agents apart when they overlap during combat. Runs as a side-effect (never
// claims movement) so it stacks with any movement module without interfering.
// Works for both herd and solo agents.
using UnityEngine;
using UnityEngine.AI;

public class CombatSeparationModule : BehaviourModuleBase
{
    [Header("Detection")]
    [Tooltip("Layer mask containing agent colliders to push against.")]
    [SerializeField] private LayerMask agentLayer;
    [Tooltip("Agents within this radius get pushed apart.")]
    [SerializeField] private float pushRadius = 2f;
    [Tooltip("How strongly to push. Scales with overlap amount.")]
    [SerializeField] private float pushStrength = 3f;

    [Header("Condition")]
    [Tooltip("Only separate when ChaseModule has an active target. Keeps idle behaviour natural.")]
    [SerializeField] private bool onlyDuringCombat = true;

    public override bool ClaimsMovement => false;

    private NavMeshAgent navAgent;
    private ChaseModule chaseModule;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

    private void Awake()
    {
        navAgent    = GetComponent<NavMeshAgent>();
        chaseModule = GetComponent<ChaseModule>();
    }

    public override string ModuleDescription =>
        "Pushes agents apart when they get too close during combat. Works for both herd and solo agents.\n\n" +
        "• agentLayer — layer(s) containing agent colliders\n" +
        "• pushRadius — agents within this distance get pushed apart\n" +
        "• pushStrength — how hard the push is\n" +
        "• onlyDuringCombat — only activate when ChaseModule has a target";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (navAgent == null || !navAgent.isOnNavMesh)
            return null;

        if (onlyDuringCombat && (chaseModule == null || !chaseModule.HasTarget))
            return null;

        Collider[] hits = Physics.OverlapSphere(context.Position, pushRadius, agentLayer);
        if (hits.Length == 0)
            return null;

        Vector3 push = Vector3.zero;
        foreach (Collider hit in hits)
        {
            if (hit.transform == context.Self)
                continue;

            Vector3 away = context.Position - hit.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist < 0.001f)
                away = Random.insideUnitSphere;

            // Stronger push the closer they are.
            push += away.normalized * (1f - dist / pushRadius);
        }

        if (push.sqrMagnitude < 0.001f)
            return null;

        navAgent.Move(push.normalized * (pushStrength * deltaTime));
        return null;
    }

    protected override void OnValidate()
    {
        pushRadius   = Mathf.Max(0.1f, pushRadius);
        pushStrength = Mathf.Max(0f, pushStrength);
    }
}
