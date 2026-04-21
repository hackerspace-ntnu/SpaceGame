// Detects a target by tag or direct reference and chases it.
// Stops and faces when within attackRange — fires OnAttackRange event for combat systems to hook into.
// Loses target when it moves outside loseTargetRange (hysteresis prevents flickering).
using System;
using UnityEngine;

public class ChaseModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string targetTag = "Player";
    [Tooltip("Only chase targets with this relationship. Requires EntityFaction on both entities.")]
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Ranges")]
    [SerializeField] private float detectRange = 10f;
    [Tooltip("Omnidirectional detection radius — bypasses FOV/LoS. Lets the agent react to targets that sneak up from behind. Should be <= detectRange.")]
    [SerializeField] private float proximityDetectRange = 4f;
    [SerializeField] private float loseTargetRange = 14f;
    [SerializeField] private float attackRange = 1.8f;

    [Header("Movement")]
    [SerializeField] private float chaseStopDistance = 1.3f;
    [SerializeField] private float chaseSpeedMultiplier = 1.3f;

    // Subscribe to trigger attacks, animations, etc. from other components.
    public event Action OnEnterAttackRange;
    public event Action OnExitAttackRange;

    public bool HasTarget => hasTarget;
    // Last position where the target was seen, for SearchModule fallback when PerceptionModule is absent.
    public Vector3? LastKnownPosition { get; private set; }

    // Allow AlertReceiverModule or external systems to force a target.
    public void ForceTarget(Transform newTarget)
    {
        target = newTarget;
        hasTarget = true;
    }

    private bool hasTarget;
    private bool inAttackRange;
    private PerceptionModule perception;
    private AlertBroadcaster alertBroadcaster;

    private void Awake()
    {
        perception = GetComponent<PerceptionModule>();
        alertBroadcaster = GetComponent<AlertBroadcaster>();
    }
    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

    private void OnEnable()
    {
        hasTarget = false;
        inAttackRange = false;
        LastKnownPosition = null;
    }

    public override string ModuleDescription =>
        "Detects a target and chases it. Stops and faces the target when within attackRange. Loses the target beyond loseTargetRange.\n\n" +
        "• targetTag — tag used to find the target (default: Player)\n" +
        "• detectRange — range at which the entity notices the target (requires FOV+LoS if PerceptionModule present)\n" +
        "• proximityDetectRange — inner omnidirectional range that bypasses FOV/LoS; lets the agent react to targets behind it\n" +
        "• attackRange — how close before the entity stops and faces\n" +
        "• loseTargetRange — target is forgotten beyond this distance\n" +
        "• Add PerceptionModule to require FOV + line-of-sight for the outer detectRange\n" +
        "• Add AlertBroadcaster to notify nearby allies when the target is first spotted\n" +
        "• OnEnterAttackRange event — wire to EntityCombatModule or animations";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);

        // Acquire target: proximity range ignores FOV/LoS (omnidirectional), outer detectRange requires CanSee.
        if (!hasTarget)
        {
            if (distance > detectRange)
                return null;

            bool withinProximity = distance <= proximityDetectRange;
            if (!withinProximity && perception != null && !perception.CanSee(target))
                return null;

            hasTarget = true;
            perception?.NotifySpotted(target);
            alertBroadcaster?.Broadcast(target, target.position);
        }
        else if (distance > loseTargetRange)
        {
            hasTarget = false;
        }

        if (hasTarget)
            LastKnownPosition = target.position;

        if (!hasTarget)
            return null;

        bool nowInRange = distance <= attackRange;
        if (nowInRange && !inAttackRange)
        {
            inAttackRange = true;
            OnEnterAttackRange?.Invoke();
        }
        else if (!nowInRange && inAttackRange)
        {
            inAttackRange = false;
            OnExitAttackRange?.Invoke();
        }

        if (inAttackRange)
            return MoveIntent.StopAndFace(target.position);

        return MoveIntent.MoveTo(target.position, chaseStopDistance, chaseSpeedMultiplier);
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
        detectRange = Mathf.Max(0.1f, detectRange);
        proximityDetectRange = Mathf.Clamp(proximityDetectRange, 0f, detectRange);
        loseTargetRange = Mathf.Max(detectRange, loseTargetRange);
        attackRange = Mathf.Max(0.1f, attackRange);
        chaseStopDistance = Mathf.Max(0.01f, chaseStopDistance);
        chaseSpeedMultiplier = Mathf.Max(0.01f, chaseSpeedMultiplier);
    }
}
