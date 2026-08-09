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
    //
    // Deliberately spawned unparented. LateUpdate below writes the camera's world pose outright,
    // so parenting it to the mount applies the vehicle's motion twice — once through the parent
    // transform, once through the recomputed target — and the residual smoothing between them
    // shows up as judder (measured: 48% frame-to-frame variance parented vs 2.6% free).
    // Lifetime is handled explicitly by ReleaseRuntimeThirdPersonCamera, not by the hierarchy.
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
            cameraObject = Object.Instantiate(prefabToUse.gameObject);
            cameraObject.name = $"{name}_MountThirdPersonCamera";
            cameraObject.tag = "Untagged";
            runtimeThirdPersonCamera = cameraObject.GetComponent<Camera>();
            runtimeThirdPersonCamera.targetTexture = null;
        }
        else if (Camera.main != null)
        {
            cameraObject = Object.Instantiate(Camera.main.gameObject);
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
            runtimeThirdPersonCamera = cameraObject.AddComponent<Camera>();
        }

        if (!cameraObject.GetComponent<AudioListener>())
            cameraObject.AddComponent<AudioListener>();

        // Without the parent to start it in the right place, the first frame must snap rather
        // than lerp — otherwise the camera swoops in from wherever the prefab was authored.
        thirdPersonCameraNeedsSnap = true;
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

        // Light smoothing on the orbit yaw. This is a comfort filter, not the shake fix — the
        // shake came from the motor driving the Rigidbody on the render loop, which made the
        // interpolated pose advance unevenly per frame; that is fixed in the motor's FixedUpdate.
        // Keep this responsive: over-damping here reads as the camera lagging the vehicle.
        float targetYaw = transform.rotation.eulerAngles.y + cameraYawOffset;
        if (thirdPersonCameraNeedsSnap)
            cameraYaw = targetYaw;
        else
            cameraYaw = Mathf.LerpAngle(cameraYaw, targetYaw, 1f - Mathf.Exp(-thirdPersonYawLerp * Time.deltaTime));

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

        // Aim: look at a point ahead of the pivot at pivot height. Because the camera is
        // above the pivot, LookRotation naturally tilts down — exactly enough to frame both
        // the vehicle and the ground ahead. thirdPersonLookAhead controls how far down.
        Vector3 targetAimPoint = pivot.position + yawRot * (Vector3.forward * thirdPersonLookAhead);

        if (thirdPersonCameraNeedsSnap)
        {
            camTransform.position = targetPosition;
            smoothedAimPoint = targetAimPoint;
            thirdPersonCameraNeedsSnap = false;
        }
        else
        {
            // Exponential decay rather than Lerp(a, b, k * deltaTime): the naive form makes the
            // smoothing strength scale with frame time, so on an uncapped framerate the camera
            // catches up by a different fraction every frame and shimmers behind the vehicle.
            float follow = 1f - Mathf.Exp(-thirdPersonFollowLerp * Time.deltaTime);
            camTransform.position = Vector3.Lerp(camTransform.position, targetPosition, follow);

            // The aim point gets its own, slower filter. Deriving rotation straight from the raw
            // target meant the camera both chased a moving point and pivoted toward it, so the
            // small position error left by the follow lerp was re-expressed as angular jitter at
            // the end of a long boom. Filtering the point the camera looks at breaks that coupling.
            float aim = 1f - Mathf.Exp(-thirdPersonAimLerp * Time.deltaTime);
            smoothedAimPoint = Vector3.Lerp(smoothedAimPoint, targetAimPoint, aim);
        }

        Vector3 aimDir = smoothedAimPoint - camTransform.position;
        if (aimDir.sqrMagnitude > 1e-4f)
            camTransform.rotation = Quaternion.LookRotation(aimDir);
    }
}
