// Rider-steered movement module. Attach alongside MountModule to let a player drive
// the object while mounted. Works on AI agents (produces MoveIntent so AgentController can
// arbitrate with other modules) and on non-AI objects (directly drives a Rigidbody).
//
// Steering only claims the frame while the rider is actively inputting. When the rider
// lets off the sticks, Tick returns null — which lets the mount's own AI take over if
// MountModule.allowAISelfMovementWhenMounted is true, or idles if it's false.
//
// Features:
//   • Move + Look input, tank-steer yaw, third/first person toggle, visual lean
//   • Jump (tap) forwarded to IMountJumpMotor
//   • Leap (hold jump, release) forwarded to IMountLeapMotor — long + high arc
//   • Self-drive fallback when no AgentController is present
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(MountModule))]
public partial class SteerModule : BehaviourModuleBase
{
    public enum CameraPerspective
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("References")]
    [SerializeField] private MountModule mountModule;

    [Header("Input Action Names")]
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string lookActionName = "Look";
    [SerializeField] private string jumpActionName = "Jump";
    [SerializeField] private string togglePerspectiveActionName = "Next";
    [SerializeField] private float steeringOverrideThreshold = 0.1f;

    [Header("Movement")]
    [Tooltip("Speed multiplier applied to MoveIntent (agent mode) or m/s (self-drive mode).")]
    [SerializeField] private float moveSpeed = 2.4f;
    [Tooltip("Tank-steer yaw rate in degrees per second.")]
    [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private float turnSmoothTime = 0.12f;
    [SerializeField] private float momentumDamping = 7f;
    [Tooltip("Multiplier units/sec that speed ramps to target. 4 ≈ 0.25s 0→full.")]
    [SerializeField] private float acceleration = 4f;
    [SerializeField] private float mountedMoveDistance = 2f;
    [SerializeField] private float mountedStopDistance = 0.15f;
    [SerializeField] private float mountedNavMeshSampleDistance = 4f;
    [SerializeField] private bool faceMoveDirection = true;

    [Header("Jump")]
    [SerializeField] private bool jumpEnabled = true;
    [Tooltip("Height of the jump when the host has no IMountJumpMotor (self-drive fallback).")]
    [SerializeField] private float selfDriveJumpHeight = 1.2f;
    [Tooltip("Duration of the self-drive jump arc.")]
    [SerializeField] private float selfDriveJumpDuration = 0.45f;

    [Header("Leap (hold jump to charge, release to leap)")]
    [SerializeField] private bool leapEnabled = true;
    [Tooltip("Seconds the jump button must be held before release triggers a leap instead of a jump.")]
    [SerializeField] private float leapHoldTime = 0.4f;
    [Tooltip("Horizontal distance of a leap, in meters.")]
    [SerializeField] private float leapHorizontal = 8f;
    [Tooltip("Peak vertical height of a leap, in meters.")]
    [SerializeField] private float leapVertical = 3f;
    [Tooltip("Seconds the leap animation takes.")]
    [SerializeField] private float leapDuration = 0.9f;

    [Header("Camera")]
    [SerializeField] private CameraPerspective defaultPerspective = CameraPerspective.ThirdPerson;
    [SerializeField] private Camera thirdPersonCamera;
    [SerializeField] private Transform thirdPersonPivot;
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 2.2f, -3.8f);
    [SerializeField] private float thirdPersonDistance = 3.8f;
    [SerializeField] private float thirdPersonFollowLerp = 14f;
    [SerializeField] private float lookSensitivity = 1f;
    [SerializeField] private float lookPitchClamp = 75f;
    [SerializeField] private float defaultMountedPitch = -15f;
    [SerializeField] private float cameraAutoAlignSpeed = 90f;
    [SerializeField] private float cameraAutoAlignDelay = 0.5f;

    [Header("Visual Lean")]
    [SerializeField] private Transform visualTiltRoot;
    [SerializeField] private float leanAmount = 10f;
    [SerializeField] private float leanSmoothTime = 0.18f;

    // ─────────── Runtime state ───────────
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction togglePerspectiveAction;

    private Vector2 currentMoveInput;
    private bool hasSteeringOverride;
    private Vector3 currentSteeringForward;

    private float mountedYaw;
    private float cameraYaw;
    private float cameraYawOffset;
    private float mountedPitch;
    private float timeSinceLastLookInput;

    private float steeringMomentum;
    private float moveInputVelocityX;
    private float moveInputVelocityY;
    private float currentLean;
    private float leanVelocity;
    private float currentSpeedMultiplier;

    private Quaternion visualTiltBaseLocalRotation;
    private CameraPerspective activePerspective;
    private bool forcedMountedLookActionEnabled;

    // Jump/leap input tracking
    private bool jumpHeld;
    private float jumpHoldDuration;

    // Execution mode
    private bool hasAgentController;
    private IMountJumpMotor jumpMotor;
    private IMountLeapMotor leapMotor;

    // Self-drive (no AgentController) state
    private Rigidbody selfDriveRigidbody;
    private bool selfDriveArcing;
    private float selfDriveArcElapsed;
    private float selfDriveArcDuration;
    private float selfDriveArcHeight;
    private Vector3 selfDriveArcStart;
    private Vector3 selfDriveArcEnd;
    private bool selfDriveArcRestoredKinematic;
    private bool selfDriveArcKinematicBefore;

    // ─────────── Public API ───────────
    public bool IsMounted => mountModule && mountModule.IsMounted;
    public Vector2 CurrentMoveInput => currentMoveInput;
    public Vector3 CurrentSteeringForward => currentSteeringForward;
    public bool HasSteeringOverride => hasSteeringOverride;
    public CameraPerspective ActivePerspective => activePerspective;
    public bool IsLeaping => selfDriveArcing || (leapMotor != null && leapMotor.IsLeaping);

    // ─────────── BehaviourModuleBase ───────────
    // ClaimsMovement defaults to true (we want priority arbitration).
    // IsActive is inherited — Tick() early-outs when not mounted.

    public override string ModuleDescription =>
        "Rider-steered movement. Claims movement while the rider is actively steering.\n\n" +
        "• Works on AI agents (via AgentController + MoveIntent) and non-AI objects (via Rigidbody).\n" +
        "• Jump = tap. Leap = hold-and-release (defines leapHorizontal + leapVertical).\n" +
        "• Toggle first/third person with the 'Next' action.\n" +
        "• Pair with MountModule for the mount lifecycle.";

    private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

    // ─────────── Lifecycle ───────────
    private void Awake()
    {
        if (!mountModule)
            mountModule = GetComponent<MountModule>();

        jumpMotor = GetComponent<IMountJumpMotor>();
        leapMotor = GetComponent<IMountLeapMotor>();
        hasAgentController = GetComponent<AgentController>() != null;
        if (!hasAgentController)
            selfDriveRigidbody = GetComponent<Rigidbody>();

        if (visualTiltRoot)
            visualTiltBaseLocalRotation = visualTiltRoot.localRotation;

        currentSteeringForward = GetSteeringForward();
    }

    private void OnEnable()
    {
        ResolveInputActions();

        if (mountModule != null)
        {
            mountModule.Mounted += HandleMounted;
            mountModule.Dismounted += HandleDismounted;

            if (mountModule.IsMounted)
                HandleMounted(mountModule.MountedPlayerMovement);
        }
    }

    private void OnDisable()
    {
        if (mountModule != null)
        {
            mountModule.Mounted -= HandleMounted;
            mountModule.Dismounted -= HandleDismounted;
        }

        SetThirdPersonCameraEnabled(false);
        SetVisualLean(0f);
        ResetSteeringState();

        if (forcedMountedLookActionEnabled && lookAction != null)
        {
            lookAction.Disable();
            forcedMountedLookActionEnabled = false;
        }
    }

    private void Update()
    {
        if (!IsMounted)
        {
            ResetMountedInputState();
            currentSteeringForward = GetSteeringForward();
            DampVisualLeanToNeutral(Time.deltaTime);
            return;
        }

        EnsureMountedLookActionEnabled();

        ReadMountedInput();
        HandleJumpAndLeap(Time.deltaTime);
        HandleMountedLook(Time.deltaTime);
        HandleTogglePerspective();

        if (mountModule.MountedPlayerMovement != null)
            mountModule.MountedPlayerMovement.ForceIdleAnimation();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            mountModule.Dismount();

        if (!hasAgentController)
            SelfDriveTick(Time.deltaTime);
    }

    // ─────────── AgentController Tick ───────────
    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (!IsMounted)
            return null;

        // While a leap is active the motor owns position; keep other modules off it.
        if (IsLeaping)
            return MoveIntent.Idle();

        if (!hasSteeringOverride)
            return null;

        return ProcessSteering(in context, deltaTime);
    }

    private MoveIntent ProcessSteering(in AgentContext context, float deltaTime)
    {
        Vector3 forward = GetSteeringForward();
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = context.Self ? context.Self.forward : Vector3.forward;
        forward.Normalize();

        // Tank steering: A/D rotates the mount body (handled in HandleMountedLook).
        // W/S drives forward/back along the mount's own forward.
        Vector3 moveDir = forward * currentMoveInput.y;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude <= 0.0001f)
        {
            currentSpeedMultiplier = Mathf.MoveTowards(currentSpeedMultiplier, 0f, acceleration * deltaTime);
            return MoveIntent.Idle();
        }

        currentSpeedMultiplier = Mathf.MoveTowards(currentSpeedMultiplier, moveSpeed, acceleration * deltaTime);
        moveDir.Normalize();

        Vector3 target = context.Position + moveDir * mountedMoveDistance;
        if (UnityEngine.AI.NavMesh.SamplePosition(target, out UnityEngine.AI.NavMeshHit hit, mountedNavMeshSampleDistance, UnityEngine.AI.NavMesh.AllAreas))
            target = hit.position;

        return MoveIntent.MoveTo(
            target,
            mountedStopDistance,
            currentSpeedMultiplier,
            faceMoveDirection,
            forward);
    }

    // ─────────── OnValidate ───────────
    protected override void OnValidate()
    {
        base.OnValidate();
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        turnSmoothTime = Mathf.Max(0.01f, turnSmoothTime);
        momentumDamping = Mathf.Max(0.1f, momentumDamping);
        acceleration = Mathf.Max(0.1f, acceleration);
        mountedMoveDistance = Mathf.Max(0.1f, mountedMoveDistance);
        mountedStopDistance = Mathf.Max(0.01f, mountedStopDistance);
        mountedNavMeshSampleDistance = Mathf.Max(0.1f, mountedNavMeshSampleDistance);
        selfDriveJumpHeight = Mathf.Max(0f, selfDriveJumpHeight);
        selfDriveJumpDuration = Mathf.Max(0.05f, selfDriveJumpDuration);
        leapHoldTime = Mathf.Max(0.05f, leapHoldTime);
        leapHorizontal = Mathf.Max(0f, leapHorizontal);
        leapVertical = Mathf.Max(0f, leapVertical);
        leapDuration = Mathf.Max(0.05f, leapDuration);
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
        lookPitchClamp = Mathf.Clamp(lookPitchClamp, 0f, 89f);
        thirdPersonDistance = Mathf.Max(0.1f, thirdPersonDistance);
        thirdPersonFollowLerp = Mathf.Max(0.01f, thirdPersonFollowLerp);
        cameraAutoAlignSpeed = Mathf.Max(0f, cameraAutoAlignSpeed);
        cameraAutoAlignDelay = Mathf.Max(0f, cameraAutoAlignDelay);
        steeringOverrideThreshold = Mathf.Max(0.01f, steeringOverrideThreshold);
        leanAmount = Mathf.Max(0f, leanAmount);
        leanSmoothTime = Mathf.Max(0.01f, leanSmoothTime);
    }
}
