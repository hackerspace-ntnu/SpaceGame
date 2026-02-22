// Lifecycle/update flow for mount input polling and shared state upkeep.
using UnityEngine;
using UnityEngine.InputSystem;

public partial class MountController
{
    private void Awake()
    {
        if (!seatPoint)
        {
            seatPoint = transform;
        }

        activeSeatPoint = seatPoint;
        ResolveInputActions();
        movementMotor = GetComponent<IMovementMotor>();
        currentSteeringForward = GetSteeringForward();
        if (visualTiltRoot)
        {
            visualTiltBaseLocalRotation = visualTiltRoot.localRotation;
        }

        SetThirdPersonCameraEnabled(false);
    }

    private void Update()
    {
        if (!IsMounted)
        {
            currentMoveInput = Vector2.zero;
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
        EnsureMountedLookActionEnabled();

        HandleMountedLook(Time.deltaTime);

        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.ForceIdleAnimation();
        }

        if (togglePerspectiveAction != null && togglePerspectiveAction.WasPressedThisFrame())
        {
            TogglePerspective();
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Dismount();
        }
    }

    private void OnDisable()
    {
        if (IsMounted)
        {
            Dismount();
        }
        else
        {
            SetThirdPersonCameraEnabled(false);
        }

        SetVisualLean(0f);
        ResetSteeringState();
    }

    private void OnValidate()
    {
        mountCooldown = Mathf.Max(0f, mountCooldown);
        fallbackDismountDistance = Mathf.Max(0.1f, fallbackDismountDistance);
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
        lookPitchClamp = Mathf.Clamp(lookPitchClamp, 0f, 89f);
        steerSpeed = Mathf.Max(1f, steerSpeed);
        turnSmoothTime = Mathf.Max(0.01f, turnSmoothTime);
        leanAmount = Mathf.Max(0f, leanAmount);
        leanSmoothTime = Mathf.Max(0.01f, leanSmoothTime);
        momentumDamping = Mathf.Max(0.1f, momentumDamping);
        thirdPersonFollowLerp = Mathf.Max(0.01f, thirdPersonFollowLerp);
        cameraAutoAlignSpeed = Mathf.Max(0f, cameraAutoAlignSpeed);
        cameraAutoAlignDelay = Mathf.Max(0f, cameraAutoAlignDelay);
    }
}
