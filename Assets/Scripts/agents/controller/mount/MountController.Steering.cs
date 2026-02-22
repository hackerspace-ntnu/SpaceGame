// Mounted steering, look-to-yaw integration, and visual lean behavior.
using UnityEngine;

public partial class MountController
{
    private void HandleMountedLook(float deltaTime)
    {
        Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

        cameraYawOffset += lookInput.x * lookSensitivity * deltaTime;
        mountedPitch = Mathf.Clamp(mountedPitch - lookInput.y * lookSensitivity * deltaTime, -lookPitchClamp, lookPitchClamp);

        if (Mathf.Abs(lookInput.x) > 0.01f)
        {
            timeSinceLastLookInput = 0f;
        }
        else
        {
            timeSinceLastLookInput += deltaTime;
        }

        if (timeSinceLastLookInput >= cameraAutoAlignDelay)
        {
            cameraYawOffset = Mathf.MoveTowards(cameraYawOffset, 0f, cameraAutoAlignSpeed * deltaTime);
        }

        cameraYaw = mountedYaw + cameraYawOffset;

        float normalizedSpeed = GetMountedNormalizedSpeed();
        float speedSteerScale = Mathf.Lerp(1f, 0.75f, normalizedSpeed);
        float steerInput = currentMoveInput.x;
        float targetYawRate = steerInput * steerSpeed * speedSteerScale;
        float momentumBlend = 1f - Mathf.Exp(-momentumDamping * deltaTime);
        steeringMomentum = Mathf.Lerp(steeringMomentum, targetYawRate, momentumBlend);
        if (Mathf.Abs(steerInput) <= 0.01f)
        {
            steeringMomentum = Mathf.Lerp(steeringMomentum, 0f, momentumBlend * 0.6f);
        }

        mountedYaw += steeringMomentum * deltaTime;
        transform.rotation = Quaternion.Euler(0f, mountedYaw, 0f);

        if (mountedFirstPersonCameraRoot)
        {
            mountedFirstPersonCameraRoot.localRotation = Quaternion.Euler(mountedPitch, 0f, 0f);
        }

        UpdateVisualLean(normalizedSpeed, deltaTime);
        currentSteeringForward = GetSteeringForward();
    }

    private Vector3 GetSteeringForward()
    {
        if (activePerspective == CameraPerspective.FirstPerson && mountedFirstPersonCamera != null)
        {
            return mountedFirstPersonCamera.transform.forward;
        }

        return transform.forward;
    }

    private float GetMountedNormalizedSpeed()
    {
        if (movementMotor != null)
        {
            float speed = movementMotor.Velocity.magnitude;
            if (speed > 0.01f)
            {
                return Mathf.Clamp01(speed / 8f);
            }
        }

        return Mathf.Clamp01(currentMoveInput.magnitude);
    }

    private void UpdateVisualLean(float normalizedSpeed, float deltaTime)
    {
        if (!visualTiltRoot)
        {
            return;
        }

        float normalizedMomentum = steerSpeed > 0.001f
            ? Mathf.Clamp(steeringMomentum / steerSpeed, -1f, 1f)
            : 0f;
        float targetLean = -normalizedMomentum * leanAmount * Mathf.Lerp(0.35f, 1f, normalizedSpeed);
        currentLean = Mathf.SmoothDamp(currentLean, targetLean, ref leanVelocity, leanSmoothTime);
        SetVisualLean(currentLean);
    }

    private void DampVisualLeanToNeutral(float deltaTime)
    {
        if (!visualTiltRoot)
        {
            return;
        }

        currentLean = Mathf.SmoothDamp(currentLean, 0f, ref leanVelocity, leanSmoothTime);
        SetVisualLean(currentLean);
    }

    private void SetVisualLean(float leanZ)
    {
        if (!visualTiltRoot)
        {
            return;
        }

        visualTiltRoot.localRotation = visualTiltBaseLocalRotation * Quaternion.Euler(0f, 0f, leanZ);
    }

    private void ResetSteeringState()
    {
        steeringMomentum = 0f;
        currentLean = 0f;
        leanVelocity = 0f;
    }
}
