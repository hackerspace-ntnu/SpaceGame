// Hybrid brain that uses NPC fallback AI when unmounted and rider input when mounted.
// Converts mount controller input into navigation-friendly MoveIntent commands.
// Also forwards mounted jump requests to motors that support mount jumping.
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Brain wrapper that delegates to fallback AI when not mounted, and uses rider input when mounted.
/// </summary>
public class MountedAgentBrain : MonoBehaviour, IAgentBrain
{
    [Header("References")]
    [SerializeField] private NpcBrain fallbackBrain;
    [SerializeField] private MountController mountController;
    [SerializeField] private MountSteeringController steeringController;

    [Header("Mounted Control")]
    [SerializeField] private float mountedMoveDistance = 2f;
    [SerializeField] private float mountedStopDistance = 0.15f;
    [SerializeField] private float mountedSpeedMultiplier = 2.4f;
    [SerializeField] private float mountedNavMeshSampleDistance = 4f;
    [SerializeField] private bool faceMouseLookDirection = true;

    [Header("Mounted Speed Feel")]
    // Rate (multiplier units/sec) at which the mount accelerates and decelerates.
    // 4 = ~0.25s from zero to full speed. Lower = heavier feel, higher = more agile.
    [SerializeField] private float mountedAcceleration = 4f;

    private float currentSpeedMultiplier;

    [Header("Mounted Jump")]
    [SerializeField] private bool enableMountedJump = true;

    private IMountJumpMotor jumpMotor;

    private void Awake()
    {
        if (!fallbackBrain)
        {
            fallbackBrain = GetComponent<NpcBrain>();
        }

        if (!mountController)
        {
            mountController = GetComponent<MountController>();
        }

        if (!steeringController)
        {
            steeringController = GetComponent<MountSteeringController>();
        }

        if (!steeringController && mountController)
        {
            steeringController = gameObject.AddComponent<MountSteeringController>();
        }

        jumpMotor = GetComponent<IMountJumpMotor>();
    }

    public MoveIntent Tick(in AgentContext context, float deltaTime)
    {
        if (mountController && mountController.IsMounted)
        {
            if (enableMountedJump && jumpMotor != null && steeringController != null && steeringController.ConsumeMountedJumpPressed())
            {
                jumpMotor.RequestJump();
            }

            if (steeringController != null && steeringController.HasSteeringOverride)
            {
                return ProcessMountedControl(context, steeringController.CurrentMoveInput, steeringController.CurrentSteeringForward, deltaTime);
            }
        }

        if (fallbackBrain)
        {
            return fallbackBrain.Tick(in context, deltaTime);
        }

        return MoveIntent.Idle();
    }

    private MoveIntent ProcessMountedControl(in AgentContext context, Vector2 moveInput, Vector3 forwardReference, float deltaTime)
    {
        Vector3 referenceForward = forwardReference;
        referenceForward.y = 0f;
        if (referenceForward.sqrMagnitude <= 0.0001f)
        {
            referenceForward = context.Self ? context.Self.forward : Vector3.forward;
            referenceForward.y = 0f;
        }

        if (referenceForward.sqrMagnitude <= 0.0001f)
        {
            referenceForward = Vector3.forward;
        }

        referenceForward.Normalize();
        // Tank steering: A/D rotates the mount body (handled in MountController).
        // W/S moves forward/back only — no strafing.
        Vector3 moveDirection = referenceForward * moveInput.y;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude <= 0.0001f)
        {
            // Decelerate while idle so the next acceleration ramp starts from near zero.
            currentSpeedMultiplier = Mathf.MoveTowards(currentSpeedMultiplier, 0f, mountedAcceleration * deltaTime);
            return MoveIntent.Idle();
        }

        // Gradually ramp up to full speed — gives the animal a sense of mass.
        currentSpeedMultiplier = Mathf.MoveTowards(currentSpeedMultiplier, mountedSpeedMultiplier, mountedAcceleration * deltaTime);

        moveDirection.Normalize();
        Vector3 targetPosition = context.Position + moveDirection * mountedMoveDistance;

        if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, mountedNavMeshSampleDistance, NavMesh.AllAreas))
        {
            targetPosition = hit.position;
        }

        Vector3 facingDirection = referenceForward;
        return MoveIntent.MoveTo(
            targetPosition,
            mountedStopDistance,
            currentSpeedMultiplier,
            faceMouseLookDirection,
            facingDirection);
    }

    private void OnValidate()
    {
        mountedMoveDistance = Mathf.Max(0.1f, mountedMoveDistance);
        mountedStopDistance = Mathf.Max(0.01f, mountedStopDistance);
        mountedSpeedMultiplier = Mathf.Max(0.01f, mountedSpeedMultiplier);
        mountedNavMeshSampleDistance = Mathf.Max(0.1f, mountedNavMeshSampleDistance);
        mountedAcceleration = Mathf.Max(0.1f, mountedAcceleration);
    }
}
