// Universal rider-steering module. Attach alongside MountModule to let a player drive
// anything: ground vehicle, mounted creature, flying blimp — the module doesn't care.
//
// SteerModule reads rider input and forwards it to the motor via IRiderControllable.
// The motor interprets that input in its own physics model (tank-steer on RigidbodyMotor /
// NavMeshAgentMotor, throttle+yaw+vertical on FlyingRigidbodyMotor, etc.).
//
// Flow per frame while mounted + rider has input above threshold:
//   1. SteerModule.Update → ReadMountedInput → build RiderInput.
//   2. SteerModule.Tick → motor.ApplyRiderInput(input, dt).
//   3. Tick returns MoveIntent.Idle() to claim the frame (blocks AI modules).
//   4. AgentController calls motor.Tick(Idle); motor's rider-frame guard skips the MoveIntent
//      path so the rider's direct writes stand.
//
// When the rider lets off (input magnitude below threshold), Tick returns null — AI modules
// can then run if MountModule.allowAISelfMovementWhenMounted is true.
//
// Jump / leap are one-shot rider actions and keep their dedicated interfaces
// (IMountJumpMotor / IMountLeapMotor). Motors that don't implement them (e.g. FlyingRigidbodyMotor)
// simply ignore the rider's jump button.
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(MountModule))]
[RequireComponent(typeof(AgentController))]
public partial class SteerModule : BehaviourModuleBase
{
    [Header("References")]
    [SerializeField] private MountModule mountModule;

    [Header("Input Action Names")]
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string jumpActionName = "Jump";
    [Tooltip("Optional Vector2 action whose Y axis is used as ascend/descend input for flying motors. " +
             "Leave blank if this vehicle doesn't fly.")]
    [SerializeField] private string verticalActionName = "";
    [SerializeField] private float steeringOverrideThreshold = 0.1f;

    [Header("Input Smoothing")]
    [SerializeField] private float turnSmoothTime = 0.12f;

    [Header("Running")]
    [SerializeField] private bool riderCanRun = false;
    [SerializeField] private string runActionName = "Sprint";

    [Header("Jump")]
    [SerializeField] private bool jumpEnabled = true;

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

    [Header("Visual Lean")]
    [SerializeField] private Transform visualTiltRoot;
    [SerializeField] private float leanAmount = 10f;
    [SerializeField] private float leanSmoothTime = 0.18f;

    // ─────────── Runtime state ───────────
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction verticalAction;
    private InputAction runAction;

    private Vector2 currentMoveInput;
    private float currentVerticalInput;
    private bool hasSteeringOverride;

    private float moveInputVelocityX;
    private float moveInputVelocityY;
    private float verticalInputVelocity;
    private float currentLean;
    private float leanVelocity;

    private Quaternion visualTiltBaseLocalRotation;

    // Jump/leap input tracking
    private bool jumpHeld;
    private float jumpHoldDuration;

    private IRiderControllable riderMotor;
    private IMountJumpMotor jumpMotor;
    private IMountLeapMotor leapMotor;

    // ─────────── Public API ───────────
    public bool IsMounted => mountModule && mountModule.IsMounted;
    public Vector2 CurrentMoveInput => currentMoveInput;
    public bool HasSteeringOverride => hasSteeringOverride;
    public bool IsLeaping => leapMotor != null && leapMotor.IsLeaping;

    public override string ModuleDescription =>
        "Universal rider steering. Reads input, forwards to motor.ApplyRiderInput(), claims the frame.\n\n" +
        "• Works with any motor implementing IRiderControllable (ground, flight, custom).\n" +
        "• Jump = tap, Leap = hold-and-release (uses IMountJumpMotor / IMountLeapMotor if present).\n" +
        "• Set verticalActionName for flying vehicles.\n" +
        "• Pair with MountModule for the mount lifecycle.";

    private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

    // ─────────── Lifecycle ───────────
    private void Awake()
    {
        if (!mountModule)
            mountModule = GetComponent<MountModule>();

        riderMotor = GetComponent<IRiderControllable>();
        jumpMotor = GetComponent<IMountJumpMotor>();
        leapMotor = GetComponent<IMountLeapMotor>();

        if (visualTiltRoot)
            visualTiltBaseLocalRotation = visualTiltRoot.localRotation;
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

        SetVisualLean(0f);
        ResetMountedInputState();
    }

    private void Update()
    {
        if (!IsMounted)
        {
            ResetMountedInputState();
            DampVisualLeanToNeutral(Time.deltaTime);
            return;
        }

        ReadMountedInput();
        HandleJumpAndLeap(Time.deltaTime);
        UpdateVisualLean(Time.deltaTime);

        if (mountModule.MountedPlayerMovement != null)
            mountModule.MountedPlayerMovement.ForceIdleAnimation();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            mountModule.Dismount();
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

        // Rider is driving — forward input to the motor and claim the frame.
        if (riderMotor != null)
        {
            bool running = runAction != null && runAction.IsPressed();
            RiderInput input = new RiderInput(currentMoveInput, currentVerticalInput, running && riderCanRun);
            riderMotor.ApplyRiderInput(input, deltaTime);
        }

        return MoveIntent.Idle();
    }

    // ─────────── OnValidate ───────────
    protected override void OnValidate()
    {
        base.OnValidate();
        turnSmoothTime = Mathf.Max(0.01f, turnSmoothTime);
        leapHoldTime = Mathf.Max(0.05f, leapHoldTime);
        leapHorizontal = Mathf.Max(0f, leapHorizontal);
        leapVertical = Mathf.Max(0f, leapVertical);
        leapDuration = Mathf.Max(0.05f, leapDuration);
        steeringOverrideThreshold = Mathf.Max(0.01f, steeringOverrideThreshold);
        leanAmount = Mathf.Max(0f, leanAmount);
        leanSmoothTime = Mathf.Max(0.01f, leanSmoothTime);
    }
}
