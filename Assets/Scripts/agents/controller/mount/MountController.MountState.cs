// MountController helper partial containing mount-state/input transition routines.
// Keeps the main controller file smaller by isolating rider setup/teardown internals.
// This file focuses on state management, not steering/camera math.
using UnityEngine;
using UnityEngine.InputSystem;

public partial class MountController
{
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

    private void CacheMountedPlayerReferences(PlayerMovement playerMovement, Transform mountPointOverride)
    {
        mountedPlayer = playerMovement.transform;
        mountedPlayerMovement = playerMovement;
        mountedPlayerLook = mountedPlayer.GetComponent<PlayerLook>();
        mountedInteractor = mountedPlayer.GetComponentInChildren<Interactor>(true);
        mountedPlayerRigidbody = mountedPlayer.GetComponent<Rigidbody>();
        mountedFirstPersonCamera = mountedPlayer.GetComponentInChildren<Camera>(true);
        mountedFirstPersonCameraRoot = mountedPlayerLook != null ? mountedPlayerLook.cameraRoot : null;
        activeSeatPoint = mountPointOverride ? mountPointOverride : seatPoint;
    }

    private void DisableRiderComponentsForMount()
    {
        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.enabled = false;
            mountedPlayerMovement.ForceIdleAnimation();
        }

        if (disablePlayerLook && mountedPlayerLook)
        {
            mountedPlayerLook.enabled = false;
        }

        EnsureMountedLookActionEnabled();

        if (disablePlayerInteractor && mountedInteractor)
        {
            mountedInteractor.enabled = false;
        }
    }

    private void RestoreRiderComponentsAfterDismount()
    {
        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.enabled = true;
        }

        if (disablePlayerLook && mountedPlayerLook)
        {
            mountedPlayerLook.SetHeadVisible(false);
            mountedPlayerLook.enabled = true;
        }

        if (forcedMountedLookActionEnabled && lookAction != null && disablePlayerLook && mountedPlayerLook == null)
        {
            lookAction.Disable();
        }

        if (disablePlayerInteractor && mountedInteractor)
        {
            mountedInteractor.enabled = true;
        }
    }

    private void EnterMountedRigidbodyState()
    {
        if (!mountedPlayerRigidbody)
        {
            return;
        }

        playerRigidbodyWasKinematic = mountedPlayerRigidbody.isKinematic;
        playerRigidbodyHadGravity = mountedPlayerRigidbody.useGravity;
        mountedPlayerRigidbody.linearVelocity = Vector3.zero;
        mountedPlayerRigidbody.angularVelocity = Vector3.zero;
        mountedPlayerRigidbody.isKinematic = true;
        mountedPlayerRigidbody.useGravity = false;
    }

    private void ExitMountedRigidbodyState()
    {
        if (!mountedPlayerRigidbody)
        {
            return;
        }

        mountedPlayerRigidbody.isKinematic = playerRigidbodyWasKinematic;
        mountedPlayerRigidbody.useGravity = playerRigidbodyHadGravity;
        mountedPlayerRigidbody.linearVelocity = Vector3.zero;
        mountedPlayerRigidbody.angularVelocity = Vector3.zero;
    }

    private void ParentRiderToMount()
    {
        Transform rideParent = seatPoint ? seatPoint : transform;
        mountedPlayer.SetParent(rideParent, true);
        mountedPlayer.localPosition = Vector3.zero;
        mountedPlayer.localRotation = Quaternion.identity;
    }

    private void InitializeMountedViewState()
    {
        float yaw = transform.rotation.eulerAngles.y;
        mountedYaw = yaw;
        cameraYaw = yaw;
        cameraYawOffset = 0f;
        timeSinceLastLookInput = 0f;
        mountedPitch = mountedFirstPersonCameraRoot ? mountedFirstPersonCameraRoot.localEulerAngles.x : 0f;
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
    }

    private void ClearMountedReferences()
    {
        mountedPlayer = null;
        mountedPlayerMovement = null;
        mountedPlayerLook = null;
        mountedInteractor = null;
        mountedPlayerRigidbody = null;
        mountedFirstPersonCamera = null;
        mountedFirstPersonCameraRoot = null;
        forcedMountedLookActionEnabled = false;
    }
}
