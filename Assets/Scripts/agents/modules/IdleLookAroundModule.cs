// When no higher-priority module claims the frame, occasionally turns to face a random direction.
// Gives characters personality. Works as a fallback above WanderModule or standalone.
// Only fires while the agent is idle — never interrupts movement.
using UnityEngine;

public class IdleLookAroundModule : BehaviourModuleBase
{
    [Header("Look Around — Idle")]
    [SerializeField] private float minInterval = 1.5f;
    [SerializeField] private float maxInterval = 4.5f;
    [SerializeField] private float turnAngle = 70f;
    [SerializeField] private float lookDuration = 0.75f;

    private float intervalTimer;
    private float activeTimer;
    private Vector3 facePosition;

    private void Reset() => SetPriorityDefault(ModulePriority.Personality);

    private void OnEnable()
    {
        activeTimer = 0f;
        ScheduleNext();
    }

    public override string ModuleDescription =>
        "Periodically turns to face a random direction when the agent is idle. Adds personality and life to standing entities.\n\n" +
        "• minInterval / maxInterval — random time between turns\n" +
        "• turnAngle — maximum degrees to rotate from forward\n" +
        "• lookDuration — how long to hold the turned pose";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (context.IsMoving)
        {
            activeTimer = 0f;
            return null;
        }

        if (activeTimer > 0f)
        {
            activeTimer -= deltaTime;
            return MoveIntent.StopAndFace(facePosition);
        }

        intervalTimer -= deltaTime;
        if (intervalTimer > 0f)
            return null;

        float yaw = Random.Range(-turnAngle, turnAngle);
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * context.Self.forward;
        facePosition = context.Position + dir;
        activeTimer = lookDuration;
        ScheduleNext();
        return MoveIntent.StopAndFace(facePosition);
    }

    private void ScheduleNext() =>
        intervalTimer = Random.Range(minInterval, maxInterval);

    protected override void OnValidate()
    {
        minInterval = Mathf.Max(0.1f, minInterval);
        maxInterval = Mathf.Max(minInterval, maxInterval);
        turnAngle = Mathf.Clamp(turnAngle, 1f, 179f);
        lookDuration = Mathf.Max(0.1f, lookDuration);
    }
}
