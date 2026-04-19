// Receives alerts from AlertBroadcaster and forces ChaseModule to acquire a target
// it may not have independently detected yet.
// Drag onto any entity that should respond to ally alerts (guards, pack hunters, etc.).
using UnityEngine;

public class AlertReceiverModule : BehaviourModuleBase
{
    [Header("Alert Response")]
    [Tooltip("How long to investigate the alerted position before giving up if no target is found.")]
    [SerializeField] private float alertDuration = 8f;
    [SerializeField] private float stopDistance = 0.5f;

    private Transform alertTarget;
    private Vector3 alertPosition;
    private float alertTimer;
    private ChaseModule chaseModule;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive);
    private void Awake() => chaseModule = GetComponent<ChaseModule>();
    private void OnEnable() => ClearAlert();

    // Called by AlertBroadcaster.
    public void ReceiveAlert(Transform target, Vector3 lastKnownPosition)
    {
        alertTarget = target;
        alertPosition = lastKnownPosition;
        alertTimer = alertDuration;

        // Directly force the ChaseModule to track this target if it isn't already.
        if (chaseModule != null && target)
            chaseModule.ForceTarget(target);
    }

    public void ClearAlert()
    {
        alertTarget = null;
        alertTimer = 0f;
    }

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (alertTimer <= 0f)
            return null;

        alertTimer -= deltaTime;

        // If ChaseModule already has the target, let it handle movement.
        if (chaseModule != null && chaseModule.HasTarget)
            return null;

        // Move toward the last known alert position while we don't independently have the target.
        return MoveIntent.MoveTo(alertPosition, stopDistance, 1.2f);
    }

    protected override void OnValidate()
    {
        alertDuration = Mathf.Max(0.1f, alertDuration);
        stopDistance = Mathf.Max(0.01f, stopDistance);
    }
}
