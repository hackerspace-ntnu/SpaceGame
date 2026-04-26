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

    // Clone Camera.main so the mount's third-person view inherits every render setting:
    // URP UniversalAdditionalCameraData (renderer, post-processing, volume mask), shaders,
    // clear flags, culling mask, skybox — everything. Falls back to a bare Camera only if
    // Camera.main is missing entirely.
    //
    // Critical: any user scripts on the main camera (PlayerLook, head-bob, etc.) get cloned
    // too, and their LateUpdate would overwrite the rotation this module sets each frame.
    // We disable every non-Unity MonoBehaviour on the clone to neutralize them.
    private void EnsureRuntimeThirdPersonCamera()
    {
        if (runtimeThirdPersonCamera != null)
            return;

        Camera source = Camera.main;
        GameObject cameraObject;
        if (source != null)
        {
            cameraObject = Object.Instantiate(source.gameObject);
            cameraObject.name = $"{name}_MountThirdPersonCamera";
            cameraObject.tag = "Untagged";
            cameraObject.transform.SetParent(transform, false);
            foreach (Transform child in cameraObject.transform)
                Object.Destroy(child.gameObject);
            runtimeThirdPersonCamera = cameraObject.GetComponent<Camera>();
            runtimeThirdPersonCamera.targetTexture = null;
            DisableUserScriptsOnClone(cameraObject);
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

    // Disable every user-assembly MonoBehaviour on the cloned camera so nothing rotates or
    // repositions it from under us. Keeps Unity and URP components (Camera, AudioListener,
    // UniversalAdditionalCameraData, Volume, etc.) untouched so render settings still apply.
    private static void DisableUserScriptsOnClone(GameObject cameraObject)
    {
        foreach (MonoBehaviour mb in cameraObject.GetComponents<MonoBehaviour>())
        {
            if (!mb)
                continue;
            string asm = mb.GetType().Assembly.GetName().Name;
            if (asm.StartsWith("UnityEngine") || asm.StartsWith("Unity."))
                continue;
            mb.enabled = false;
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
