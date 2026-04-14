// Lifecycle/update flow for mounted steering input and shared state upkeep.
using UnityEngine;
using UnityEngine.InputSystem;

public partial class MountSteeringController
{
    private void Awake()
    {
        if (!mountController)
        {
            mountController = GetComponent<MountController>();
        }

        ResolveInputActions();
        movementMotor = GetComponent<IMovementMotor>();
        currentSteeringForward = GetSteeringForward();
        if (visualTiltRoot)
        {
            visualTiltBaseLocalRotation = visualTiltRoot.localRotation;
        }

        SetThirdPersonCameraEnabled(false);
    }

    private void OnEnable()
    {
        if (mountController != null)
        {
            mountController.Mounted += HandleMounted;
            mountController.Dismounted += HandleDismounted;

            if (mountController.IsMounted)
            {
                HandleMounted(mountController.MountedPlayerMovement);
            }
        }
    }

    private void Update()
    {
        if (mountController == null || !mountController.IsMounted)
        {
            currentMoveInput = Vector2.zero;
            hasSteeringOverride = false;
            currentSteeringForward = GetSteeringForward();
            jumpPressedThisFrame = false;
            DampVisualLeanToNeutral(Time.deltaTime);
            return;
        }

        Vector2 rawMoveInput = ReadMountedMoveInput();
        currentMoveInput = new Vector2(
            Mathf.SmoothDamp(currentMoveInput.x, rawMoveInput.x, ref moveInputVelocityX, turnSmoothTime),
            Mathf.SmoothDamp(currentMoveInput.y, rawMoveInput.y, ref moveInputVelocityY, turnSmoothTime));
        jumpPressedThisFrame = jumpAction != null && jumpAction.WasPressedThisFrame();
        hasSteeringOverride = rawMoveInput.sqrMagnitude >= steeringOverrideThreshold * steeringOverrideThreshold;
        EnsureMountedLookActionEnabled();

        HandleMountedLook(Time.deltaTime);

        if (mountController.MountedPlayerMovement != null)
        {
            mountController.MountedPlayerMovement.ForceIdleAnimation();
        }

        if (togglePerspectiveAction != null && togglePerspectiveAction.WasPressedThisFrame())
        {
            TogglePerspective();
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            mountController.Dismount();
        }
    }

    private void OnDisable()
    {
        if (mountController != null)
        {
            mountController.Mounted -= HandleMounted;
            mountController.Dismounted -= HandleDismounted;
        }

        SetThirdPersonCameraEnabled(false);
        SetVisualLean(0f);
        ResetSteeringState();
    }

    private void OnValidate()
    {
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
        lookPitchClamp = Mathf.Clamp(lookPitchClamp, 0f, 89f);
        steerSpeed = Mathf.Max(1f, steerSpeed);
        turnSmoothTime = Mathf.Max(0.01f, turnSmoothTime);
        leanAmount = Mathf.Max(0f, leanAmount);
        leanSmoothTime = Mathf.Max(0.01f, leanSmoothTime);
        momentumDamping = Mathf.Max(0.1f, momentumDamping);
        thirdPersonDistance = Mathf.Max(0.1f, thirdPersonDistance);
        thirdPersonFollowLerp = Mathf.Max(0.01f, thirdPersonFollowLerp);
        cameraAutoAlignSpeed = Mathf.Max(0f, cameraAutoAlignSpeed);
        cameraAutoAlignDelay = Mathf.Max(0f, cameraAutoAlignDelay);
        steeringOverrideThreshold = Mathf.Max(0.01f, steeringOverrideThreshold);
    }

    private void ResolveInputActions()
    {
        moveAction = InputSystem.actions.FindAction("Move");
        lookAction = InputSystem.actions.FindAction("Look");
        jumpAction = InputSystem.actions.FindAction("Jump");
        togglePerspectiveAction = InputSystem.actions.FindAction(perspectiveToggleActionName);
    }

    private Vector2 ReadMountedMoveInput()
    {
        if (moveAction == null)
        {
            return Vector2.zero;
        }

        return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
    }

    private void EnsureMountedLookActionEnabled()
    {
        if (lookAction == null || lookAction.enabled)
        {
            return;
        }

        lookAction.Enable();
        forcedMountedLookActionEnabled = true;
    }

    private void HandleMounted(PlayerMovement _)
    {
        forcedMountedLookActionEnabled = false;
        InitializeMountedViewState();
        ResetMountedInputState();
        currentSteeringForward = GetSteeringForward();
        ResetSteeringState();
        ApplyPerspective(defaultPerspective);
    }

    private void HandleDismounted(PlayerMovement _)
    {
        if (forcedMountedLookActionEnabled && lookAction != null)
        {
            lookAction.Disable();
        }

        forcedMountedLookActionEnabled = false;
        SetThirdPersonCameraEnabled(false);
        SetFirstPersonCameraEnabled(true);
        SetMountedVisorEnabled(true);
        ResetMountedInputState();
        currentSteeringForward = GetSteeringForward();
        ResetSteeringState();
        SetVisualLean(0f);
    }

    private void InitializeMountedViewState()
    {
        float yaw = transform.rotation.eulerAngles.y;
        mountedYaw = yaw;
        cameraYaw = yaw;
        cameraYawOffset = 0f;
        timeSinceLastLookInput = 0f;

        Transform cameraRoot = mountController != null ? mountController.MountedFirstPersonCameraRoot : null;
        mountedPitch = cameraRoot ? cameraRoot.localEulerAngles.x : 0f;
        if (mountedPitch > 180f)
        {
            mountedPitch -= 360f;
        }
    }

    private void ResetMountedInputState()
    {
        currentMoveInput = Vector2.zero;
        moveInputVelocityX = 0f;
        moveInputVelocityY = 0f;
        jumpPressedThisFrame = false;
        hasSteeringOverride = false;
    }
}
