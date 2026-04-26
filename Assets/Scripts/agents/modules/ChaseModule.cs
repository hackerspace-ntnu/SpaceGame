// Detects a target and chases it.
// Movement-only: always returns a MoveTo while holding a target. Attack modules (higher priority)
// preempt the chase frame when they can hit, producing the stand-and-fire behaviour.
// Loses target when it moves outside loseTargetRange (hysteresis prevents flickering).
using UnityEngine;

public class ChaseModule : BehaviourModuleBase
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("Only chase targets with this relationship. Requires EntityFaction on both entities.")]
    [SerializeField] private FactionRelationship requiredRelationship = FactionRelationship.Hostile;

    [Header("Ranges")]
    [SerializeField] private float detectRange = 10f;
    [Tooltip("Omnidirectional detection radius — bypasses FOV/LoS. Lets the agent react to targets that sneak up from behind. Should be <= detectRange.")]
    [SerializeField] private float proximityDetectRange = 4f;
    [SerializeField] private float loseTargetRange = 14f;
    [Tooltip("Extra distance added to the longest sibling attack module's maxRange when Awake auto-expands loseTargetRange. " +
             "Ensures the agent keeps chasing a target that just stepped out of fire range instead of dropping aggro.")]
    [SerializeField] private float loseTargetRangeAttackBuffer = 4f;
    [Tooltip("Extra distance added to the longest sibling attack module's maxRange when Awake auto-expands detectRange. " +
             "Guarantees Chase aggros whenever an attack module can hit, so the target is tracked once the attack band is left.")]
    [SerializeField] private float detectRangeAttackBuffer = 1f;

    [Header("Movement")]
    [SerializeField] private float chaseStopDistance = 1.3f;
    [SerializeField] private float chaseSpeedMultiplier = 1.3f;

    public bool HasTarget => hasTarget;
    public Vector3? LastKnownPosition { get; private set; }

    public void ForceTarget(Transform newTarget)
    {
        target = newTarget;
        hasTarget = true;
    }

    private bool hasTarget;
    private PerceptionModule perception;
    private AlertBroadcaster alertBroadcaster;
    private HerdModule herdModule;
    private EntityFaction selfFaction;
    // Slot offset radius used when routing chase destinations through HerdModule. Auto-shrunk to
    // melee attack range so melee agents don't park outside their own attackRange.
    private float effectiveSpreadRadius;
    private bool hasMeleeAttack;
    // Time accumulated since the last positive PerceptionModule.CanSee while aggroed.
    // Used to drop aggro after memoryDuration elapses without a re-sighting.
    private float timeSinceSeen;

    private void Awake()
    {
        perception = GetComponent<PerceptionModule>();
        alertBroadcaster = GetComponent<AlertBroadcaster>();
        herdModule = GetComponent<HerdModule>();
        selfFaction = GetComponent<EntityFaction>();
        ExpandLoseTargetRangeForAttackModules();
        ConfigureMeleeMovement();
    }

    // For agents with a CloseCombatModule: disable the herd ring and tighten chaseStopDistance
    // so the final-arrival position (slotRadius + stopDistance + nav jitter) is strictly inside
    // attackRange. Without this the agent parks at the ring edge and the attackRange check
    // rejects by 0.5–1 m, producing the "chases me but rarely hits" symptom.
    //
    // Uses the smallest sibling melee range so every CloseCombatModule on the agent can fire.
    private void ConfigureMeleeMovement()
    {
        float meleeAttackRange = float.MaxValue;
        foreach (CloseCombatModule c in GetComponents<CloseCombatModule>())
            meleeAttackRange = Mathf.Min(meleeAttackRange, c.AttackRange);

        hasMeleeAttack = meleeAttackRange < float.MaxValue;

        effectiveSpreadRadius = herdModule != null ? herdModule.CombatSpreadRadius : 0f;
        if (hasMeleeAttack)
        {
            // No slot offset for melee — herd members cluster on the target, which is the
            // correct shape for melee combat. Spreading would park them outside attackRange.
            effectiveSpreadRadius = 0f;

            // Stop just inside attackRange so the agent is in swing reach but leaves visible
            // daylight between colliders (no hugging, no pushing). Floor at 0.3 so very short
            // attack ranges still produce a non-negative stop distance.
            float stopCap = Mathf.Max(0.3f, meleeAttackRange - 0.4f);
            if (chaseStopDistance > stopCap)
                chaseStopDistance = stopCap;
        }
    }

    // Widens detectRange and loseTargetRange if needed so Chase's aggro window fully covers the
    // weapon's fire band. Attack modules resolve targets independently via EntityTargetRegistry
    // and will fire as long as the target is in the fire band — but if Chase.detectRange is
    // smaller than the weapon's maxRange, Chase never aggros in the first place, and the agent
    // has no chase state to fall back on when the target steps out of fire range. Same story
    // for loseTargetRange: too small and aggro drops the moment the target exits the band.
    private void ExpandLoseTargetRangeForAttackModules()
    {
        float attackMaxRange = 0f;
        foreach (AgentRangedCombatModule r in GetComponents<AgentRangedCombatModule>())
            attackMaxRange = Mathf.Max(attackMaxRange, r.MaxRange);

        if (attackMaxRange <= 0f)
            return;

        float detectFloor = attackMaxRange + detectRangeAttackBuffer;
        if (detectRange < detectFloor)
            detectRange = detectFloor;

        float loseFloor = attackMaxRange + loseTargetRangeAttackBuffer;
        if (loseTargetRange < loseFloor)
            loseTargetRange = loseFloor;
    }

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

    private void OnEnable()
    {
        hasTarget = false;
        LastKnownPosition = null;
    }

    public override string ModuleDescription =>
        "Detects a target and chases it. Always drives toward the target while the target is known; attack modules (higher priority) preempt with StopAndFace when they can hit. Loses the target beyond loseTargetRange.\n\n" +
        "• requiredRelationship — faction relationship the nearest candidate must have (default: Hostile)\n" +
        "• detectRange — range at which the entity notices the target (requires FOV+LoS if PerceptionModule present)\n" +
        "• proximityDetectRange — inner omnidirectional range that bypasses FOV/LoS\n" +
        "• loseTargetRange — target is forgotten beyond this distance\n" +
        "• chaseStopDistance — NavMesh stopping distance on the approach (attack modules gate the actual halt)\n" +
        "• Add PerceptionModule to require FOV + line-of-sight for the outer detectRange\n" +
        "• Add AlertBroadcaster to notify nearby allies when the target is first spotted";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveTarget();
        if (!target)
            return null;

        float distance = Vector3.Distance(context.Position, target.position);

        if (!hasTarget)
        {
            if (distance > detectRange)
                return null;

            bool withinProximity = distance <= proximityDetectRange;
            if (!withinProximity && perception != null && !perception.CanSee(target))
                return null;

            hasTarget = true;
            timeSinceSeen = 0f;
            perception?.NotifySpotted(target);
            alertBroadcaster?.Broadcast(target, target.position);
        }
        else if (distance > loseTargetRange)
        {
            hasTarget = false;
        }

        if (!hasTarget)
            return null;

        // While aggroed, use PerceptionModule as the aggro lease: keep re-checking LoS/FOV.
        // Refresh on every positive sighting; drop aggro when memoryDuration elapses without one.
        // Without perception, fall back to the straight-line leash (loseTargetRange above).
        Vector3 chasePosition = target.position;
        if (perception != null)
        {
            if (perception.CanSee(target))
            {
                timeSinceSeen = 0f;
                chasePosition = target.position;
            }
            else
            {
                timeSinceSeen += deltaTime;
                if (timeSinceSeen > perception.MemoryDuration)
                {
                    hasTarget = false;
                    return null;
                }
                chasePosition = perception.HasLastKnownPosition ? perception.LastKnownPosition : target.position;
            }
        }

        LastKnownPosition = chasePosition;

        // Herd slot offset — suppressed for melee agents so they reach the target instead of
        // circling at the ring edge. effectiveSpreadRadius is 0 when hasMeleeAttack is true.
        Vector3 destination = herdModule != null
            ? herdModule.GetSlotPositionAround(chasePosition, effectiveSpreadRadius)
            : chasePosition;

        return MoveIntent.MoveTo(destination, chaseStopDistance, chaseSpeedMultiplier, isRunning: true);
    }

    private void TryResolveTarget()
    {
        if (target)
        {
            // Dying entities stay active during their despawn delay, so the Transform ref survives.
            // Drop the aggro as soon as the target's health reports dead and re-resolve.
            IDamageable currentDamageable = target.GetComponentInChildren<IDamageable>();
            if (currentDamageable != null && !currentDamageable.Alive)
            {
                target = null;
                hasTarget = false;
                LastKnownPosition = null;
            }
            else
            {
                return;
            }
        }

        Transform candidate = EntityTargetRegistry.ResolveNearest(selfFaction, requiredRelationship, transform.position);
        if (!candidate)
            return;

        IDamageable candidateDamageable = candidate.GetComponentInChildren<IDamageable>();
        if (candidateDamageable != null && !candidateDamageable.Alive)
            return;

        target = candidate;
    }

    protected override void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        proximityDetectRange = Mathf.Clamp(proximityDetectRange, 0f, detectRange);
        loseTargetRange = Mathf.Max(detectRange, loseTargetRange);
        loseTargetRangeAttackBuffer = Mathf.Max(0f, loseTargetRangeAttackBuffer);
        detectRangeAttackBuffer = Mathf.Max(0f, detectRangeAttackBuffer);
        chaseStopDistance = Mathf.Max(0.01f, chaseStopDistance);
        chaseSpeedMultiplier = Mathf.Max(0.01f, chaseSpeedMultiplier);
    }
}
