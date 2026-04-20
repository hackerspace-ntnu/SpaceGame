// Hears noise events emitted by NoiseEmitters and reacts by alerting ChaseModule
// or moving toward the noise source. Drag onto any entity that should respond to sound.
// Configure which NoiseTypes trigger investigation vs immediate aggro.
using System;
using UnityEngine;
using UnityEngine.Events;

[Flags]
public enum NoiseTypeMask
{
    None      = 0,
    Footstep  = 1 << 0,
    Alert     = 1 << 1,
    Hurt      = 1 << 2,
    Death     = 1 << 3,
    Gunshot   = 1 << 4,
    Explosion = 1 << 5,
    Custom    = 1 << 6,
    All       = ~0
}

public class NoiseReceiverModule : BehaviourModuleBase
{
    [Header("Hearing")]
    [Tooltip("Which noise types trigger investigation.")]
    [SerializeField] private NoiseTypeMask investigateOn = NoiseTypeMask.Footstep | NoiseTypeMask.Gunshot | NoiseTypeMask.Explosion;
    [Tooltip("Which noise types immediately force-alert ChaseModule.")]
    [SerializeField] private NoiseTypeMask aggroOn = NoiseTypeMask.Alert | NoiseTypeMask.Hurt;

    [Header("Investigation")]
    [SerializeField] private float investigateDuration = 5f;
    [SerializeField] private float stopDistance = 0.5f;
    [SerializeField] private float speedMultiplier = 1.1f;

    [Header("Events")]
    public UnityEvent<Vector3> OnHearNoise;

    private ChaseModule chaseModule;

    private bool isInvestigating;
    private Vector3 investigatePosition;
    private float investigateTimer;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive - 2); // 18 — below Chase, above Search
    private void Awake() => chaseModule = GetComponent<ChaseModule>();
    private void OnEnable() { isInvestigating = false; investigateTimer = 0f; }

    // Called by NoiseEmitter when this receiver is within range.
    public void OnNoiseHeard(NoiseType type, Vector3 origin, float radius, Transform instigator)
    {
        NoiseTypeMask typeMask = TypeToMask(type);

        OnHearNoise?.Invoke(origin);

        if ((aggroOn & typeMask) != 0 && chaseModule != null && instigator)
        {
            chaseModule.ForceTarget(instigator);
            isInvestigating = false;
            return;
        }

        if ((investigateOn & typeMask) != 0)
        {
            investigatePosition = origin;
            investigateTimer = investigateDuration;
            isInvestigating = true;
        }
    }

    public override string ModuleDescription =>
        "Hears noise events from nearby NoiseEmitters and reacts based on noise type.\n\n" +
        "• investigateOn — noise types that trigger moving to the source (footsteps, gunshots)\n" +
        "• aggroOn — noise types that immediately force-alert ChaseModule (alerts, hurt sounds)\n" +
        "• investigateDuration — how long to investigate a noise source before giving up\n" +
        "• Requires: ChaseModule for aggro response. NoiseEmitters in the scene emit the events.\n" +
        "• OnHearNoise — UnityEvent fired on any heard noise, regardless of type mask";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        // Don't investigate if already chasing.
        if (chaseModule != null && chaseModule.HasTarget)
        {
            isInvestigating = false;
            return null;
        }

        if (!isInvestigating)
            return null;

        investigateTimer -= deltaTime;
        if (investigateTimer <= 0f)
        {
            isInvestigating = false;
            return null;
        }

        return MoveIntent.MoveTo(investigatePosition, stopDistance, speedMultiplier);
    }

    private static NoiseTypeMask TypeToMask(NoiseType type) => type switch
    {
        NoiseType.Footstep  => NoiseTypeMask.Footstep,
        NoiseType.Alert     => NoiseTypeMask.Alert,
        NoiseType.Hurt      => NoiseTypeMask.Hurt,
        NoiseType.Death     => NoiseTypeMask.Death,
        NoiseType.Gunshot   => NoiseTypeMask.Gunshot,
        NoiseType.Explosion => NoiseTypeMask.Explosion,
        NoiseType.Custom    => NoiseTypeMask.Custom,
        _                   => NoiseTypeMask.None
    };

    protected override void OnValidate()
    {
        investigateDuration = Mathf.Max(0.1f, investigateDuration);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
    }
}
