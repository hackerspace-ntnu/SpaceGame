// Airborne fallback behaviour. Picks random 3D points inside a spherical volume around an anchor
// and emits MoveIntent.MoveTo. Pair with FlyingRigidbodyMotor. No NavMesh sampling — the motor
// flies in free 3D space.
using UnityEngine;

public class AirWanderModule : BehaviourModuleBase
{
    [Header("Wander Volume")]
    [Tooltip("If set, the wander volume is centered on this transform. Otherwise it uses the agent's spawn position.")]
    [SerializeField] private Transform anchor;
    [SerializeField] private float horizontalRadius = 30f;
    [Tooltip("Half-height of the cylindrical wander volume (±verticalRange around the anchor Y).")]
    [SerializeField] private float verticalRange = 8f;
    [SerializeField] private float minAltitude = 20f;

    [Header("Arrival")]
    [SerializeField] private float stopDistance = 2f;
    [SerializeField] private float minTargetDistance = 8f;
    [SerializeField] private int maxSampleAttempts = 6;

    [Header("Wait")]
    [SerializeField] private float minWaitTime = 0.5f;
    [SerializeField] private float maxWaitTime = 2.5f;

    [Header("Movement")]
    [SerializeField] private float speedMultiplier = 1f;

    private bool hasDestination;
    private Vector3 currentDestination;
    private Vector3 anchorPosition;
    private float waitTimer;

    private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

    public override string ModuleDescription =>
        "Random 3D wander for flying agents. No NavMesh — picks points in a cylinder around an anchor.\n\n" +
        "• anchor — optional reference transform. If null, uses the agent's own position at enable time.\n" +
        "• horizontalRadius / verticalRange — size of the wander volume.\n" +
        "• minAltitude — never pick a point below this world Y.\n" +
        "• Pairs naturally with FlyingRigidbodyMotor.";

    private void OnEnable()
    {
        anchorPosition = anchor ? anchor.position : transform.position;
        hasDestination = false;
        waitTimer = 0f;
    }

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (hasDestination && context.HasReachedDestination)
        {
            hasDestination = false;
            waitTimer = Random.Range(minWaitTime, maxWaitTime);
        }

        if (waitTimer > 0f)
        {
            waitTimer -= deltaTime;
            return null;
        }

        if (!hasDestination)
        {
            if (!TryPickDestination(context.Position, out currentDestination))
                return null;
            hasDestination = true;
        }

        return MoveIntent.MoveTo(currentDestination, stopDistance, speedMultiplier);
    }

    private bool TryPickDestination(Vector3 origin, out Vector3 destination)
    {
        Vector3 center = anchor ? anchor.position : anchorPosition;

        for (int i = 0; i < maxSampleAttempts; i++)
        {
            Vector2 flat = Random.insideUnitCircle * horizontalRadius;
            float y = Mathf.Max(minAltitude, center.y + Random.Range(-verticalRange, verticalRange));
            Vector3 candidate = new Vector3(center.x + flat.x, y, center.z + flat.y);

            if ((candidate - origin).sqrMagnitude < minTargetDistance * minTargetDistance)
                continue;

            destination = candidate;
            return true;
        }

        destination = origin;
        return false;
    }

    protected override void OnValidate()
    {
        horizontalRadius = Mathf.Max(1f, horizontalRadius);
        verticalRange = Mathf.Max(0f, verticalRange);
        minAltitude = Mathf.Max(0f, minAltitude);
        stopDistance = Mathf.Max(0.1f, stopDistance);
        minTargetDistance = Mathf.Max(0.1f, minTargetDistance);
        maxSampleAttempts = Mathf.Max(1, maxSampleAttempts);
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
    }
}
