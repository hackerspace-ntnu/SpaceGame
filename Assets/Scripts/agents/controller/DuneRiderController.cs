using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(MountModule))]
[RequireComponent(typeof(Rigidbody))]
public class DuneRiderController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MountModule mountModule;
    [SerializeField] private Rigidbody body;
    [SerializeField] private Transform visualRoot;

    [Header("Drive")]
    [SerializeField] private float maxForwardSpeed = 12f;
    [SerializeField] private float maxReverseSpeed = 5f;
    [SerializeField] private float acceleration = 22f;
    [SerializeField] private float deceleration = 28f;
    [SerializeField] private float turnSpeed = 115f;
    [SerializeField] private float sprintMultiplier = 1.35f;

    [Header("Hop")]
    [SerializeField] private float hopVelocity = 5.5f;
    [SerializeField] private float groundCheckDistance = 0.9f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Visual Lean")]
    [SerializeField] private float leanAngle = 7f;
    [SerializeField] private float leanLerp = 12f;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;
    private RigidbodyConstraints defaultConstraints;
    private Quaternion visualBaseLocalRotation = Quaternion.identity;
    private bool capturedDefaults;
    private bool hopQueued;
    private float currentLean;
    private float currentSpeed;

    private void Awake()
    {
        if (!mountModule)
            mountModule = GetComponent<MountModule>();
        if (!body)
            body = GetComponent<Rigidbody>();
        if (visualRoot)
            visualBaseLocalRotation = visualRoot.localRotation;

        CaptureBodyDefaults();
        ConfigureBodyForRiding();
        ResolveInputActions();
    }

    private void OnEnable()
    {
        if (mountModule)
        {
            mountModule.Mounted += HandleMounted;
            mountModule.Dismounted += HandleDismounted;
        }
    }

    private void OnDisable()
    {
        if (mountModule)
        {
            mountModule.Mounted -= HandleMounted;
            mountModule.Dismounted -= HandleDismounted;
        }
    }

    private void Update()
    {
        if (!mountModule || !mountModule.IsMounted)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            mountModule.Dismount();

        if (ReadHopPressed())
            hopQueued = true;
    }

    private void FixedUpdate()
    {
        if (!mountModule || !mountModule.IsMounted || !body)
            return;

        ConfigureBodyForRiding();

        float deltaTime = Time.fixedDeltaTime;
        Vector2 move = ReadMoveInput();
        ApplyYaw(move.x, deltaTime);
        ApplyThrottle(move.y, ReadSprintHeld(), deltaTime);
        ApplyHop();
        ApplyVisualLean(move.x, move.y, deltaTime);
    }

    private void HandleMounted(PlayerMovement _)
    {
        ConfigureBodyForRiding();
        if (body)
            body.WakeUp();
    }

    private void HandleDismounted(PlayerMovement _)
    {
        hopQueued = false;
        currentLean = 0f;
        currentSpeed = 0f;
        if (visualRoot)
            visualRoot.localRotation = visualBaseLocalRotation;
        RestoreBodyDefaults();
    }

    private void CaptureBodyDefaults()
    {
        if (!body || capturedDefaults)
            return;

        defaultConstraints = body.constraints;
        capturedDefaults = true;
    }

    private void ConfigureBodyForRiding()
    {
        if (!body)
            return;

        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void RestoreBodyDefaults()
    {
        if (!body || !capturedDefaults)
            return;

        body.constraints = defaultConstraints;
        body.angularVelocity = Vector3.zero;
    }

    private void ResolveInputActions()
    {
        if (InputSystem.actions == null)
            return;

        moveAction = InputSystem.actions.FindAction("Move", false);
        jumpAction = InputSystem.actions.FindAction("Jump", false);
        sprintAction = InputSystem.actions.FindAction("Sprint", false);
    }

    private Vector2 ReadMoveInput()
    {
        Vector2 keyboard = ReadKeyboardMove();
        if (keyboard.sqrMagnitude > 0.001f)
            return Vector2.ClampMagnitude(keyboard, 1f);

        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.001f)
                return Vector2.ClampMagnitude(stick, 1f);
        }

        if (moveAction != null && moveAction.enabled)
            return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

        return Vector2.zero;
    }

    private static Vector2 ReadKeyboardMove()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        Vector2 input = Vector2.zero;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            input.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            input.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            input.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            input.y += 1f;

        return input;
    }

    private bool ReadSprintHeld()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
            return true;

        if (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed)
            return true;

        return sprintAction != null && sprintAction.enabled && sprintAction.IsPressed();
    }

    private bool ReadHopPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            return true;

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            return true;

        return jumpAction != null && jumpAction.enabled && jumpAction.WasPressedThisFrame();
    }

    private void ApplyYaw(float yawInput, float deltaTime)
    {
        if (Mathf.Abs(yawInput) <= 0.001f)
            return;

        Quaternion delta = Quaternion.Euler(0f, yawInput * turnSpeed * deltaTime, 0f);
        body.MoveRotation(delta * body.rotation);
    }

    private void ApplyThrottle(float throttle, bool sprint, float deltaTime)
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float topSpeed = throttle >= 0f ? maxForwardSpeed : maxReverseSpeed;
        if (sprint && throttle > 0f)
            topSpeed *= sprintMultiplier;

        // Track speed in our own state so ground friction between FixedUpdates can't drain it.
        float targetSpeed = throttle * topSpeed;
        float ramp = Mathf.Abs(throttle) > 0.001f ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, ramp * deltaTime);

        Vector3 velocity = body.linearVelocity;
        Vector3 horizontal = forward * currentSpeed;
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;
        body.linearVelocity = velocity;
        body.WakeUp();
    }

    private void ApplyHop()
    {
        if (!hopQueued)
            return;

        hopQueued = false;
        if (!IsGrounded())
            return;

        Vector3 velocity = body.linearVelocity;
        velocity.y = Mathf.Max(velocity.y, hopVelocity);
        body.linearVelocity = velocity;
        body.WakeUp();
    }

    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        return Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
    }

    private void ApplyVisualLean(float yawInput, float throttle, float deltaTime)
    {
        if (!visualRoot)
            return;

        float targetLean = -yawInput * leanAngle * Mathf.Clamp01(Mathf.Abs(throttle));
        currentLean = Mathf.Lerp(currentLean, targetLean, Mathf.Clamp01(leanLerp * deltaTime));
        visualRoot.localRotation = visualBaseLocalRotation * Quaternion.Euler(0f, 0f, currentLean);
    }

    private void OnValidate()
    {
        maxForwardSpeed = Mathf.Max(0.1f, maxForwardSpeed);
        maxReverseSpeed = Mathf.Max(0.1f, maxReverseSpeed);
        acceleration = Mathf.Max(0.1f, acceleration);
        deceleration = Mathf.Max(0.1f, deceleration);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
        hopVelocity = Mathf.Max(0f, hopVelocity);
        groundCheckDistance = Mathf.Max(0.1f, groundCheckDistance);
        leanAngle = Mathf.Max(0f, leanAngle);
        leanLerp = Mathf.Max(0.1f, leanLerp);
    }
}
