// Mounted look input, perspective toggle, and third/first-person camera placement.
// Lives on MountModule so every mount (steered or not) gets the camera system.
using UnityEngine;
using UnityEngine.InputSystem;

public partial class MountModule
{
    private void ResolveCameraInputActions()
    {
        if (InputSystem.actions == null)
            return;
        lookAction = InputSystem.actions.FindAction(lookActionName);
    }

    private void EnsureLookActionEnabled()
    {
        if (lookAction == null || lookAction.enabled)
            return;
        lookAction.Enable();
        forcedLookActionEnabled = true;
    }

    private void HandleLookInput(float deltaTime)
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

        if (mountedFirstPersonCameraRoot)
            mountedFirstPersonCameraRoot.localRotation = Quaternion.Euler(mountedPitch, 0f, 0f);
    }

    private void ApplyPerspective(CameraPerspective perspective)
    {
        activePerspective = perspective;
        if (activePerspective == CameraPerspective.FirstPerson)
        {
            SetThirdPersonCameraEnabled(false);
            SetFirstPersonCameraEnabled(true);
            SetMountedVisorEnabled(true);
            mountedPlayerLook?.SetHeadVisible(false);
        }
        else
        {
            SetFirstPersonCameraEnabled(false);
            SetThirdPersonCameraEnabled(true);
            SetMountedVisorEnabled(false);
            mountedPlayerLook?.SetHeadVisible(true);
        }
    }

    private void InitializeMountedViewState()
    {
        float yaw = transform.rotation.eulerAngles.y;
        cameraYaw = yaw;
        cameraYawOffset = 0f;
        timeSinceLastLookInput = 0f;
        mountedPitch = defaultMountedPitch;
    }

    private void SetFirstPersonCameraEnabled(bool enabledState)
    {
        if (mountedFirstPersonCamera == null)
            return;
        mountedFirstPersonCamera.enabled = enabledState;
        AudioListener listener = mountedFirstPersonCamera.GetComponent<AudioListener>();
        if (listener)
            listener.enabled = enabledState;
    }

    private void SetThirdPersonCameraEnabled(bool enabledState)
    {
        if (enabledState)
            EnsureRuntimeThirdPersonCamera();

        if (runtimeThirdPersonCamera == null)
            return;

        runtimeThirdPersonCamera.enabled = enabledState;
        AudioListener listener = runtimeThirdPersonCamera.GetComponent<AudioListener>();
        if (listener)
            listener.enabled = enabledState;
    }

    // Default third-person camera prefab loaded from Resources/ when no per-vehicle prefab
    // is wired. Authored with the right URP settings (post-processing on, volume mask, etc.)
    // so every mountable gets sane visuals out of the box.
    private const string DefaultThirdPersonCameraResourcePath = "Mount Third Person Camera";
    private static Camera s_defaultThirdPersonCameraPrefab;

    private static Camera ResolveDefaultThirdPersonCameraPrefab()
    {
        if (s_defaultThirdPersonCameraPrefab != null)
            return s_defaultThirdPersonCameraPrefab;
        s_defaultThirdPersonCameraPrefab = Resources.Load<Camera>(DefaultThirdPersonCameraResourcePath);
        return s_defaultThirdPersonCameraPrefab;
    }

    // Spawn the third-person camera. Prefers thirdPersonCameraPrefab (per-vehicle override),
    // then the project default loaded from Resources/, then a clone of Camera.main, then a
    // bare Camera as last resort.
    private void EnsureRuntimeThirdPersonCamera()
    {
        if (runtimeThirdPersonCamera != null)
            return;

        Camera prefabToUse = thirdPersonCameraPrefab != null
            ? thirdPersonCameraPrefab
            : ResolveDefaultThirdPersonCameraPrefab();

        GameObject cameraObject;
        if (prefabToUse != null)
        {
            cameraObject = Object.Instantiate(prefabToUse.gameObject, transform);
            cameraObject.name = $"{name}_MountThirdPersonCamera";
            cameraObject.tag = "Untagged";
            runtimeThirdPersonCamera = cameraObject.GetComponent<Camera>();
            runtimeThirdPersonCamera.targetTexture = null;
        }
        else if (Camera.main != null)
        {
            cameraObject = Object.Instantiate(Camera.main.gameObject, transform);
            cameraObject.name = $"{name}_MountThirdPersonCamera";
            cameraObject.tag = "Untagged";
            foreach (Transform child in cameraObject.transform)
                Object.Destroy(child.gameObject);
            runtimeThirdPersonCamera = cameraObject.GetComponent<Camera>();
            runtimeThirdPersonCamera.targetTexture = null;
        }
        else
        {
            cameraObject = new GameObject($"{name}_MountThirdPersonCamera");
            cameraObject.transform.SetParent(transform, false);
            runtimeThirdPersonCamera = cameraObject.AddComponent<Camera>();
        }

        if (!cameraObject.GetComponent<AudioListener>())
            cameraObject.AddComponent<AudioListener>();
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

    private void LateUpdate()
    {
        if (!IsMounted)
            return;

        cameraYaw = transform.rotation.eulerAngles.y + cameraYawOffset;

        if (activePerspective != CameraPerspective.ThirdPerson || runtimeThirdPersonCamera == null)
            return;

        Transform pivot = thirdPersonPivot
            ? thirdPersonPivot
            : (mountedPlayer ? mountedPlayer : activeSeatPoint);
        if (pivot == null)
            pivot = transform;

        // Position: behind+above the pivot, rotated around by cameraYaw (rider's look-stick yaw).
        Quaternion yawRot = Quaternion.Euler(0f, cameraYaw, 0f);
        Vector3 targetPosition = pivot.position + yawRot * GetThirdPersonCameraOffset();
        Transform camTransform = runtimeThirdPersonCamera.transform;
        camTransform.position = Vector3.Lerp(
            camTransform.position,
            targetPosition,
            Mathf.Clamp01(thirdPersonFollowLerp * Time.deltaTime));

        // Aim: look at a point ahead of the pivot at pivot height. Because the camera is
        // above the pivot, LookRotation naturally tilts down — exactly enough to frame both
        // the vehicle and the ground ahead. thirdPersonLookAhead controls how far down.
        Vector3 aimPoint = pivot.position + yawRot * (Vector3.forward * thirdPersonLookAhead);
        Vector3 aimDir = aimPoint - camTransform.position;
        if (aimDir.sqrMagnitude > 1e-4f)
            camTransform.rotation = Quaternion.LookRotation(aimDir);
    }
}
