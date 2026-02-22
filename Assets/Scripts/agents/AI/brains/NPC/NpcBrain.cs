using UnityEngine;
using UnityEngine.AI;

public enum NpcPlayerReactionMode
{
    Ignore,
    Watch,
    Approach,
    KeepDistance,
    Flee
}

public class NpcBrain : MonoBehaviour, IAgentBrain
{
    [Header("Behaviours")]
    [SerializeField] private WanderBehaviour wanderBehaviour;

    [Header("Player Target")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private string playerTag = "Player";

    [Header("Player Reaction")]
    [SerializeField] private NpcPlayerReactionMode playerReactionMode = NpcPlayerReactionMode.Watch;
    [SerializeField] private float playerDetectRadius = 4f;
    [SerializeField] private float reactionSpeedMultiplier = 1.1f;
    [SerializeField] private float moveStopDistance = 0.2f;
    [SerializeField] private float approachStopDistance = 1.6f;
    [SerializeField] private float keepDistanceRadius = 3f;
    [SerializeField] private float fleeDistanceRadius = 7f;

    [Header("Idle Personality")]
    [SerializeField] private bool enableLookAround = true;
    [SerializeField] private float lookAroundMinInterval = 1.5f;
    [SerializeField] private float lookAroundMaxInterval = 4.5f;
    [SerializeField] private float lookAroundTurnAngle = 70f;
    [SerializeField] private float lookAroundDuration = 0.75f;

    [Header("Fallback Movement")]
    [SerializeField] private float wanderSpeedMultiplier = 1f;

    private float pauseTimer;
    private Transform interactionFocusTarget;
    private float interactionFocusTimer;

    private float lookAroundTimer;
    private float lookAroundActiveTimer;
    private Vector3 lookAroundFacePosition;

    private void Awake()
    {
        if (!wanderBehaviour)
        {
            wanderBehaviour = GetComponent<WanderBehaviour>();
        }
    }

    private void OnEnable()
    {
        pauseTimer = 0f;
        interactionFocusTarget = null;
        interactionFocusTimer = 0f;

        lookAroundActiveTimer = 0f;
        ScheduleNextLookAround();

        wanderBehaviour?.ResetState();
    }

    public MoveIntent Tick(in AgentContext context, float deltaTime)
    {
        if (interactionFocusTimer > 0f && interactionFocusTarget)
        {
            interactionFocusTimer -= deltaTime;
            return MoveIntent.StopAndFace(interactionFocusTarget.position);
        }

        if (interactionFocusTimer <= 0f)
        {
            interactionFocusTarget = null;
            interactionFocusTimer = 0f;
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= deltaTime;
            return ProcessIdleLookAround(context, deltaTime) ?? MoveIntent.Idle();
        }

        TryResolvePlayerTarget();
        MoveIntent? playerIntent = TryGetPlayerReactionIntent(context);
        if (playerIntent.HasValue)
        {
            return playerIntent.Value;
        }

        if (!wanderBehaviour)
        {
            return ProcessIdleLookAround(context, deltaTime) ?? MoveIntent.Idle();
        }

        if (!wanderBehaviour.Tick(context.Position, context.HasReachedDestination, deltaTime, out Vector3 destination))
        {
            return ProcessIdleLookAround(context, deltaTime) ?? MoveIntent.Idle();
        }

        return MoveIntent.MoveTo(destination, moveStopDistance, wanderSpeedMultiplier);
    }

    public void Pause(float duration)
    {
        pauseTimer = Mathf.Max(pauseTimer, duration);
    }

    public void FocusOn(Transform target, float duration)
    {
        if (!target)
        {
            return;
        }

        interactionFocusTarget = target;
        interactionFocusTimer = Mathf.Max(interactionFocusTimer, duration);
        pauseTimer = Mathf.Max(pauseTimer, duration);
    }

    private void TryResolvePlayerTarget()
    {
        if (playerTarget)
        {
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject)
        {
            playerTarget = playerObject.transform;
        }
    }

    private MoveIntent? TryGetPlayerReactionIntent(in AgentContext context)
    {
        if (playerReactionMode == NpcPlayerReactionMode.Ignore || !playerTarget)
        {
            return null;
        }

        float distance = Vector3.Distance(context.Position, playerTarget.position);
        if (distance > playerDetectRadius)
        {
            return null;
        }

        Vector3 playerPosition = playerTarget.position;
        switch (playerReactionMode)
        {
            case NpcPlayerReactionMode.Watch:
                return MoveIntent.StopAndFace(playerPosition);

            case NpcPlayerReactionMode.Approach:
                if (distance <= approachStopDistance)
                {
                    return MoveIntent.StopAndFace(playerPosition);
                }

                return MoveIntent.MoveTo(playerPosition, approachStopDistance, reactionSpeedMultiplier);

            case NpcPlayerReactionMode.KeepDistance:
                if (distance < keepDistanceRadius)
                {
                    if (TryGetPositionAwayFrom(context.Position, playerPosition, keepDistanceRadius, out Vector3 keepDistanceDestination))
                    {
                        return MoveIntent.MoveTo(keepDistanceDestination, moveStopDistance, reactionSpeedMultiplier);
                    }
                }

                return MoveIntent.StopAndFace(playerPosition);

            case NpcPlayerReactionMode.Flee:
                if (TryGetPositionAwayFrom(context.Position, playerPosition, fleeDistanceRadius, out Vector3 fleeDestination))
                {
                    return MoveIntent.MoveTo(fleeDestination, moveStopDistance, reactionSpeedMultiplier);
                }

                return MoveIntent.StopAndFace(playerPosition);

            default:
                return null;
        }
    }

    private bool TryGetPositionAwayFrom(Vector3 selfPosition, Vector3 threatPosition, float desiredDistance, out Vector3 destination)
    {
        Vector3 away = selfPosition - threatPosition;
        away.y = 0f;

        if (away.sqrMagnitude < 0.0001f)
        {
            away = Random.insideUnitSphere;
            away.y = 0f;
        }

        Vector3 candidate = selfPosition + away.normalized * Mathf.Max(0.5f, desiredDistance);
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            destination = hit.position;
            return true;
        }

        destination = selfPosition;
        return false;
    }

    private MoveIntent? ProcessIdleLookAround(in AgentContext context, float deltaTime)
    {
        if (!enableLookAround)
        {
            return null;
        }

        if (lookAroundActiveTimer > 0f)
        {
            lookAroundActiveTimer -= deltaTime;
            return MoveIntent.StopAndFace(lookAroundFacePosition);
        }

        lookAroundTimer -= deltaTime;
        if (lookAroundTimer > 0f)
        {
            return null;
        }

        float randomYaw = Random.Range(-lookAroundTurnAngle, lookAroundTurnAngle);
        Vector3 direction = Quaternion.Euler(0f, randomYaw, 0f) * context.Self.forward;
        lookAroundFacePosition = context.Position + direction;
        lookAroundActiveTimer = lookAroundDuration;
        ScheduleNextLookAround();
        return MoveIntent.StopAndFace(lookAroundFacePosition);
    }

    private void ScheduleNextLookAround()
    {
        if (!enableLookAround)
        {
            lookAroundTimer = 0f;
            return;
        }

        lookAroundTimer = Random.Range(lookAroundMinInterval, lookAroundMaxInterval);
    }

    private void OnValidate()
    {
        playerDetectRadius = Mathf.Max(0.1f, playerDetectRadius);
        reactionSpeedMultiplier = Mathf.Max(0.01f, reactionSpeedMultiplier);
        moveStopDistance = Mathf.Max(0.01f, moveStopDistance);
        approachStopDistance = Mathf.Max(0.1f, approachStopDistance);
        keepDistanceRadius = Mathf.Max(0.1f, keepDistanceRadius);
        fleeDistanceRadius = Mathf.Max(0.1f, fleeDistanceRadius);

        lookAroundMinInterval = Mathf.Max(0.1f, lookAroundMinInterval);
        lookAroundMaxInterval = Mathf.Max(lookAroundMinInterval, lookAroundMaxInterval);
        lookAroundTurnAngle = Mathf.Clamp(lookAroundTurnAngle, 1f, 179f);
        lookAroundDuration = Mathf.Max(0.1f, lookAroundDuration);

        wanderSpeedMultiplier = Mathf.Max(0.01f, wanderSpeedMultiplier);
    }
}
