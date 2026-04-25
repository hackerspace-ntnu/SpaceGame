// Visual lean + mount/dismount handlers for SteerModule.
// Body-yaw steering now lives in the motor (via IRiderControllable). Camera rigging lives on
// MountModule. This file is only responsible for the cosmetic lean and for clearing input state
// across mount lifecycle transitions.
using UnityEngine;

public partial class SteerModule
{
    private void UpdateVisualLean(float deltaTime)
    {
        if (!visualTiltRoot)
            return;

        // Lean opposite to yaw input, scaled by throttle (no lean when stationary).
        float throttleScale = Mathf.Clamp01(Mathf.Abs(currentMoveInput.y));
        float targetLean = hasSteeringOverride
            ? -currentMoveInput.x * leanAmount * Mathf.Lerp(0.35f, 1f, throttleScale)
            : 0f;

        currentLean = Mathf.SmoothDamp(currentLean, targetLean, ref leanVelocity, leanSmoothTime);
        SetVisualLean(currentLean);
    }

    private void DampVisualLeanToNeutral(float deltaTime)
    {
        if (!visualTiltRoot)
            return;

        currentLean = Mathf.SmoothDamp(currentLean, 0f, ref leanVelocity, leanSmoothTime);
        SetVisualLean(currentLean);
    }

    private void SetVisualLean(float leanZ)
    {
        if (!visualTiltRoot)
            return;
        visualTiltRoot.localRotation = visualTiltBaseLocalRotation * Quaternion.Euler(0f, 0f, leanZ);
    }

    private void HandleMounted(PlayerMovement _)
    {
        ResolveMotorReferences();
        EnsureMountedInputActionsEnabled();
        ResetMountedInputState();
        currentLean = 0f;
        leanVelocity = 0f;
    }

    private void HandleDismounted(PlayerMovement _)
    {
        RestoreMountedInputActions();
        ResetMountedInputState();
        currentLean = 0f;
        leanVelocity = 0f;
        SetVisualLean(0f);
    }
}
