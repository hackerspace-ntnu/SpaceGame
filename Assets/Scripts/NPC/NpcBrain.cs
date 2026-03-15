using UnityEngine;

public class NpcBrain : MonoBehaviour, IAgentBrain
{
    [Header("Behaviours")]
    [SerializeField] private WanderBehaviour wanderBehaviour;

    [Header("Goal Override")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float stopAndLookRadius = 4f;

    [Header("Movement")]
    [SerializeField] private float moveStopDistance = 0.2f;
    [SerializeField] private float moveSpeedMultiplier = 1f;

    private float pauseTimer;
    private Transform interactionFocusTarget;
    private float interactionFocusTimer;
    private LassoTarget lassoTarget;

    private void Awake()
    {
        if (!wanderBehaviour)
        {
            wanderBehaviour = GetComponent<WanderBehaviour>();
        }

        lassoTarget = GetComponent<LassoTarget>();
    }

    private void OnEnable()
    {
        pauseTimer = 0f;
        interactionFocusTarget = null;
        interactionFocusTimer = 0f;
        wanderBehaviour?.ResetState();
    }

    public MoveIntent Tick(in AgentContext context, float deltaTime)
    {
        if (lassoTarget != null && lassoTarget.TryGetLeadIntent(context.Position, out MoveIntent lassoIntent))
        {
            return lassoIntent;
        }

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
            return MoveIntent.Idle();
        }

        TryResolvePlayerTarget();
        if (ShouldStopAndLookAtPlayer(context.Position))
        {
            return MoveIntent.StopAndFace(playerTarget.position);
        }

        if (!wanderBehaviour)
        {
            return MoveIntent.Idle();
        }

        if (!wanderBehaviour.Tick(context.Position, context.HasReachedDestination, deltaTime, out Vector3 destination))
        {
            return MoveIntent.Idle();
        }

        return MoveIntent.MoveTo(destination, moveStopDistance, moveSpeedMultiplier);
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

    private bool ShouldStopAndLookAtPlayer(Vector3 agentPosition)
    {
        if (!playerTarget)
        {
            return false;
        }

        return Vector3.Distance(agentPosition, playerTarget.position) <= stopAndLookRadius;
    }

    private void OnValidate()
    {
        stopAndLookRadius = Mathf.Max(0.1f, stopAndLookRadius);
        moveStopDistance = Mathf.Max(0.01f, moveStopDistance);
        moveSpeedMultiplier = Mathf.Max(0.01f, moveSpeedMultiplier);
    }
}
