// Mounted look handling, visual lean, and third/first-person camera management for SteerModule.
using UnityEngine;

public partial class SteerModule
{
    private void HandleMountedLook(float deltaTime)
    {
        Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

        cameraYawOffset += lookInput.x * lookSensitivity * deltaTime;
        mountedPitch = Mathf.Clamp(mountedPitch - lookInput.y * lookSensitivity * deltaTime, -lookPitchClamp, lookPitchClamp);

        if (Mathf.Abs(lookInput.x) > 0.01f)
            timeSinceLastLookInput = 0f;
        else
            timeSinceLastLookInput += deltaTime;

        if (timeSinceLastLookInput >= cameraAutoAlignDelay)
            cameraYawOffset = Mathf.MoveTowards(cameraYawOffset, 0f, cameraAutoAlignSpeed * deltaTime);

        float normalizedSpeed = GetNormalizedSpeed();

        if (hasSteeringOverride)
        {
            float speedSteerScale = Mathf.Lerp(1f, 0.75f, normalizedSpeed);
            float steerInput = currentMoveInput.x;
            float targetYawRate = steerInput * turnSpeed * speedSteerScale;
            float momentumBlend = 1f - Mathf.Exp(-momentumDamping * deltaTime);
            steeringMomentum = Mathf.Lerp(steeringMomentum, targetYawRate, momentumBlend);
            if (Mathf.Abs(steerInput) <= 0.01f)
                steeringMomentum = Mathf.Lerp(steeringMomentum, 0f, momentumBlend * 0.6f);

            mountedYaw += steeringMomentum * deltaTime;
            transform.rotation = Quaternion.Euler(0f, mountedYaw, 0f);
        }
        else
        {
            steeringMomentum = Mathf.Lerp(steeringMomentum, 0f, 1f - Mathf.Exp(-momentumDamping * deltaTime));
            mountedYaw = transform.rotation.eulerAngles.y;
        }

        cameraYaw = mountedYaw + cameraYawOffset;

        Transform firstPersonCameraRoot = mountModule != null ? mountModule.MountedFirstPersonCameraRoot : null;
        if (firstPersonCameraRoot)
            firstPersonCameraRoot.localRotation = Quaternion.Euler(mountedPitch, 0f, 0f);

        UpdateVisualLean(normalizedSpeed, deltaTime);
        currentSteeringForward = GetSteeringForward();
    }

    private Vector3 GetSteeringForward()
    {
        Camera firstPersonCamera = mountModule != null ? mountModule.MountedFirstPersonCamera : null;
        if (activePerspective == CameraPerspective.FirstPerson && firstPersonCamera != null)
            return firstPersonCamera.transform.forward;

        return transform.forward;
    }

    private float GetNormalizedSpeed()
    {
        IMovementMotor motor = hasAgentController ? GetComponent<IMovementMotor>() : null;
        if (motor != null)
        {
            float speed = motor.Velocity.magnitude;
            if (speed > 0.01f)
                return Mathf.Clamp01(speed / 8f);
        }

        if (selfDriveRigidbody)
            return Mathf.Clamp01(selfDriveRigidbody.linearVelocity.magnitude / 8f);

        return Mathf.Clamp01(currentMoveInput.magnitude);
    }

    private void UpdateVisualLean(float normalizedSpeed, float deltaTime)
    {
        if (!visualTiltRoot)
            return;

        float normalizedMomentum = turnSpeed > 0.001f
            ? Mathf.Clamp(steeringMomentum / turnSpeed, -1f, 1f)
            : 0f;
        float targetLean = hasSteeringOverride
            ? -normalizedMomentum * leanAmount * Mathf.Lerp(0.35f, 1f, normalizedSpeed)
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

    private void ResetSteeringState()
    {
        steeringMomentum = 0f;
        currentLean = 0f;
        leanVelocity = 0f;
        currentSpeedMultiplier = 0f;
    }

    // ─────────── Camera management ───────────
    private void LateUpdate()
    {
        if (mountModule == null || !mountModule.IsMounted)
            return;
        if (activePerspective != CameraPerspective.ThirdPerson || thirdPersonCamera == null)
            return;

        Transform pivot = thirdPersonPivot
            ? thirdPersonPivot
            : (mountModule.MountedPlayerTransform ? mountModule.MountedPlayerTransform : mountModule.ActiveSeatPoint);
        if (pivot == null)
            pivot = transform;

        Quaternion cameraRot = Quaternion.Euler(mountedPitch, cameraYaw, 0f);
        Vector3 targetPosition = pivot.position + cameraRot * GetThirdPersonCameraOffset();
        thirdPersonCamera.transform.position = Vector3.Lerp(
            thirdPersonCamera.transform.position,
            targetPosition,
            Mathf.Clamp01(thirdPersonFollowLerp * Time.deltaTime));

        Vector3 toPivot = pivot.position - thirdPersonCamera.transform.position;
        float horizontalYaw = Mathf.Atan2(toPivot.x, toPivot.z) * Mathf.Rad2Deg;
        thirdPersonCamera.transform.rotation = Quaternion.Euler(mountedPitch, horizontalYaw, 0f);
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
            lookAction.Disable();

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
        mountedPitch = defaultMountedPitch;
    }

    private void TogglePerspective()
    {
        if (mountModule == null || !mountModule.IsMounted)
            return;

        CameraPerspective next = activePerspective == CameraPerspective.FirstPerson
            ? CameraPerspective.ThirdPerson
            : CameraPerspective.FirstPerson;
        ApplyPerspective(next);
    }

    private void ApplyPerspective(CameraPerspective perspective)
    {
        activePerspective = perspective;
        if (activePerspective == CameraPerspective.FirstPerson)
        {
            SetThirdPersonCameraEnabled(false);
            SetFirstPersonCameraEnabled(true);
            SetMountedVisorEnabled(true);
            mountModule.MountedPlayerLook?.SetHeadVisible(false);
        }
        else
        {
            SetFirstPersonCameraEnabled(false);
            SetThirdPersonCameraEnabled(true);
            SetMountedVisorEnabled(false);
            mountModule.MountedPlayerLook?.SetHeadVisible(true);
        }

        currentSteeringForward = GetSteeringForward();
    }

    private void EnsureThirdPersonCamera()
    {
        if (mountModule != null && mountModule.MountedThirdPersonCamera != null)
        {
            thirdPersonCamera = mountModule.MountedThirdPersonCamera;
            return;
        }

        if (thirdPersonCamera)
            return;

        GameObject cameraObject = new GameObject($"{name}_SteerThirdPersonCamera");
        thirdPersonCamera = cameraObject.AddComponent<Camera>();
        if (!cameraObject.GetComponent<AudioListener>())
            cameraObject.AddComponent<AudioListener>();
    }

    private void SetFirstPersonCameraEnabled(bool enabledState)
    {
        Camera firstPersonCamera = mountModule != null ? mountModule.MountedFirstPersonCamera : null;
        if (firstPersonCamera != null)
        {
            firstPersonCamera.enabled = enabledState;
            AudioListener listener = firstPersonCamera.GetComponent<AudioListener>();
            if (listener)
                listener.enabled = enabledState;
        }
    }

    private void SetThirdPersonCameraEnabled(bool enabledState)
    {
        EnsureThirdPersonCamera();

        if (mountModule != null && mountModule.MountedThirdPersonCamera != null)
            thirdPersonCamera = mountModule.MountedThirdPersonCamera;

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.enabled = enabledState;
            AudioListener listener = thirdPersonCamera.GetComponent<AudioListener>();
            if (listener)
                listener.enabled = enabledState;
        }
    }

    private void SetMountedVisorEnabled(bool enabledState)
    {
        GlassDistortionRenderFeature.RuntimeEnabled = enabledState;
    }

    private Vector3 GetThirdPersonCameraOffset()
    {
        float resolved = thirdPersonDistance > 0.01f ? thirdPersonDistance : Mathf.Max(0.1f, Mathf.Abs(thirdPersonOffset.z));
        float signedDistance = thirdPersonOffset.z > 0f ? resolved : -resolved;
        return new Vector3(thirdPersonOffset.x, thirdPersonOffset.y, signedDistance);
    }
}
