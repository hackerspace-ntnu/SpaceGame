using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Generic mount controller that can be used by any mountable object.
/// Handles rider state, mounted input, and first/third-person camera modes.
/// </summary>
public class MountController : MonoBehaviour
{
    public enum CameraPerspective
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("Mount Points")]
    [SerializeField] private Transform seatPoint;
    [SerializeField] private Transform dismountPoint;
    [SerializeField] private Transform steeringReference;

    [Header("Player Components To Toggle")]
    [SerializeField] private bool disablePlayerMovement = true;
    [SerializeField] private bool disablePlayerLook = true;
    [SerializeField] private bool disablePlayerInteractor = true;

    [Header("Dismount")]
    [SerializeField] private float mountCooldown = 0.25f;
    [SerializeField] private float fallbackDismountDistance = 1.6f;

    [Header("Mounted Look")]
    [SerializeField] private float lookSensitivity = 1f;
    [SerializeField] private float lookPitchClamp = 75f;

    [Header("Mounted Steering Feel")]
    [SerializeField] private float steerSpeed = 120f;
    [SerializeField] private float turnSmoothTime = 0.12f;
    [SerializeField] private float leanAmount = 10f;
    // Smooth time for the lean spring, independent of turnSmoothTime.
    // Slightly longer (0.18) makes lean trail behind the turn — reads as body mass.
    // Range: 0.05 (snappy) to 0.35 (heavy/floaty).
    [SerializeField] private float leanSmoothTime = 0.18f;
    [SerializeField] private float momentumDamping = 7f;
    // Fraction of steer speed applied when the player is NOT pressing a movement key.
    // Prevents the mount body freezing in third-person when only looking around.
    // 0 = original frozen behavior. 0.15 = gentle passive alignment (recommended).
    // Raise to 0.35-0.5 for mounts that should aggressively track the camera.
    [SerializeField] private float idleAlignFraction = 0.15f;
    [SerializeField] private Transform visualTiltRoot;

    [Header("Camera")]
    [SerializeField] private CameraPerspective defaultPerspective = CameraPerspective.ThirdPerson;
    [SerializeField] private Camera thirdPersonCamera;
    [SerializeField] private Transform thirdPersonPivot;
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 2.2f, -3.8f);
    [SerializeField] private float thirdPersonFollowLerp = 14f;
    [SerializeField] private float cameraAutoAlignSpeed = 90f;
    [SerializeField] private float cameraAutoAlignDelay = 0.5f;
    [SerializeField] private string perspectiveToggleActionName = "Next";

    private Transform mountedPlayer;
    private PlayerMovement mountedPlayerMovement;
    private PlayerLook mountedPlayerLook;
    private Interactor mountedInteractor;
    private Rigidbody mountedPlayerRigidbody;
    private bool playerRigidbodyWasKinematic;
    private bool playerRigidbodyHadGravity;
    private float lastMountChangeTime;

    private Transform activeSeatPoint;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction interactAction;
    private InputAction togglePerspectiveAction;
    private Vector2 currentMoveInput;
    private Vector3 currentSteeringForward;
    private bool jumpPressedThisFrame;
    private float mountedYaw;
    private float cameraYaw;
    private float mountedPitch;
    private float smoothedSteerInput;
    private float steerInputVelocity;
    private float steeringMomentum;
    private float moveInputVelocityX;
    private float moveInputVelocityY;
    private float currentLean;
    private float leanVelocity;
    private float cameraYawOffset;
    private float timeSinceLastLookInput;

    private Camera mountedFirstPersonCamera;
    private Transform mountedFirstPersonCameraRoot;
    private CameraPerspective activePerspective;
    private bool forcedMountedLookActionEnabled;
    private IMovementMotor movementMotor;
    private Quaternion visualTiltBaseLocalRotation;

    public bool IsMounted => mountedPlayer != null;
    public bool IsAvailableForMount => !IsMounted && Time.time >= lastMountChangeTime + mountCooldown;
    public Vector2 CurrentMoveInput => currentMoveInput;
    public Vector3 CurrentSteeringForward => currentSteeringForward;
    public bool ConsumeMountedJumpPressed()
    {
        if (!jumpPressedThisFrame)
        {
            return false;
        }

        jumpPressedThisFrame = false;
        return true;
    }

    private void Awake()
    {
        if (!seatPoint)
        {
            seatPoint = transform;
        }

        activeSeatPoint = seatPoint;
        moveAction = InputSystem.actions.FindAction("Move");
        lookAction = InputSystem.actions.FindAction("Look");
        jumpAction = InputSystem.actions.FindAction("Jump");
        interactAction = InputSystem.actions.FindAction("Interact");
        togglePerspectiveAction = InputSystem.actions.FindAction(perspectiveToggleActionName);
        movementMotor = GetComponent<IMovementMotor>();
        currentSteeringForward = GetSteeringForward();
        if (visualTiltRoot)
        {
            visualTiltBaseLocalRotation = visualTiltRoot.localRotation;
        }

        EnsureThirdPersonCamera();
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

        Vector2 rawMoveInput = moveAction != null
            ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f)
            : Vector2.zero;
        currentMoveInput = new Vector2(
            Mathf.SmoothDamp(currentMoveInput.x, rawMoveInput.x, ref moveInputVelocityX, turnSmoothTime),
            Mathf.SmoothDamp(currentMoveInput.y, rawMoveInput.y, ref moveInputVelocityY, turnSmoothTime));
        jumpPressedThisFrame = jumpAction != null && jumpAction.WasPressedThisFrame();
        if (lookAction != null && !lookAction.enabled)
        {
            lookAction.Enable();
            forcedMountedLookActionEnabled = true;
        }

        HandleMountedLook(Time.deltaTime);

        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.ForceIdleAnimation();
        }

        if (togglePerspectiveAction != null && togglePerspectiveAction.WasPressedThisFrame())
        {
            TogglePerspective();
        }

        if (interactAction != null && interactAction.WasPressedThisFrame())
        {
            Dismount();
        }
    }

    private void LateUpdate()
    {
        if (!IsMounted || activePerspective != CameraPerspective.ThirdPerson || thirdPersonCamera == null)
        {
            return;
        }

        Transform pivot = GetThirdPersonPivot();
        // Camera orbits around the pivot using pitch + yaw.
        Quaternion cameraRot = Quaternion.Euler(mountedPitch, cameraYaw, 0f);
        Vector3 targetPosition = pivot.position + cameraRot * thirdPersonOffset;
        thirdPersonCamera.transform.position = Vector3.Lerp(
            thirdPersonCamera.transform.position,
            targetPosition,
            Mathf.Clamp01(thirdPersonFollowLerp * Time.deltaTime));

        // Horizontal centering: yaw toward the pivot's XZ position, keep mountedPitch for vertical.
        Vector3 toPivot = pivot.position - thirdPersonCamera.transform.position;
        float horizontalYaw = Mathf.Atan2(toPivot.x, toPivot.z) * Mathf.Rad2Deg;
        thirdPersonCamera.transform.rotation = Quaternion.Euler(mountedPitch, horizontalYaw, 0f);
    }

    public bool CanMount(Interactor interactor)
    {
        if (!IsAvailableForMount)
        {
            return false;
        }

        return interactor != null && interactor.GetComponentInParent<PlayerMovement>() != null;
    }

    public bool TryMount(Interactor interactor)
    {
        return TryMount(interactor, seatPoint);
    }

    public bool TryMount(Interactor interactor, Transform mountPointOverride)
    {
        if (!CanMount(interactor))
        {
            return false;
        }

        PlayerMovement playerMovement = interactor.GetComponentInParent<PlayerMovement>();
        if (!playerMovement)
        {
            return false;
        }

        mountedPlayer = playerMovement.transform;
        mountedPlayerMovement = playerMovement;
        mountedPlayerLook = mountedPlayer.GetComponent<PlayerLook>();
        mountedInteractor = mountedPlayer.GetComponentInChildren<Interactor>(true);
        mountedPlayerRigidbody = mountedPlayer.GetComponent<Rigidbody>();
        mountedFirstPersonCamera = mountedPlayer.GetComponentInChildren<Camera>(true);
        mountedFirstPersonCameraRoot = mountedPlayerLook != null ? mountedPlayerLook.cameraRoot : null;
        activeSeatPoint = mountPointOverride ? mountPointOverride : seatPoint;

        if (disablePlayerMovement && mountedPlayerMovement)
        {
            mountedPlayerMovement.enabled = false;
            mountedPlayerMovement.ForceIdleAnimation();
        }

        if (disablePlayerLook && mountedPlayerLook)
        {
            mountedPlayerLook.enabled = false;
        }

        // PlayerLook disables the shared Look action in OnDisable().
        // Re-enable it so mounted steering can still read mouse/gamepad look.
        if (lookAction != null && !lookAction.enabled)
        {
            lookAction.Enable();
            forcedMountedLookActionEnabled = true;
        }

        if (disablePlayerInteractor && mountedInteractor)
        {
            mountedInteractor.enabled = false;
        }

        if (mountedPlayerRigidbody)
        {
            playerRigidbodyWasKinematic = mountedPlayerRigidbody.isKinematic;
            playerRigidbodyHadGravity = mountedPlayerRigidbody.useGravity;
            mountedPlayerRigidbody.linearVelocity = Vector3.zero;
            mountedPlayerRigidbody.angularVelocity = Vector3.zero;
            mountedPlayerRigidbody.isKinematic = true;
            mountedPlayerRigidbody.useGravity = false;
        }

        Transform rideParent = seatPoint ? seatPoint : transform;
        // Always parent to seatPoint (a child of this mount) so the player moves with it.
        // activeSeatPoint is used only for the initial position placement.
        mountedPlayer.SetParent(rideParent, true);
        mountedPlayer.localPosition = Vector3.zero;
        mountedPlayer.localRotation = Quaternion.identity;

        
        //mountedPlayer.SetParent(rideParent, true);
        //mountedPlayer.SetPositionAndRotation(activeSeatPoint.position, activeSeatPoint.rotation);

        Vector3 euler = transform.rotation.eulerAngles;
        mountedYaw = euler.y;
        cameraYaw = euler.y;
        cameraYawOffset = 0f;
        timeSinceLastLookInput = 0f;
        mountedPitch = mountedFirstPersonCameraRoot ? mountedFirstPersonCameraRoot.localEulerAngles.x : 0f;
        if (mountedPitch > 180f)
        {
            mountedPitch -= 360f;
        }

        currentMoveInput = Vector2.zero;
        moveInputVelocityX = 0f;
        moveInputVelocityY = 0f;
        currentSteeringForward = GetSteeringForward();
        lastMountChangeTime = Time.time;
        ResetSteeringState();
        ApplyPerspective(defaultPerspective);
        return true;
    }

    public void Dismount()
    {
        if (!IsMounted)
        {
            return;
        }

        Transform rider = mountedPlayer;
        rider.SetParent(null, true);

        Vector3 dismountPosition = dismountPoint
            ? dismountPoint.position
            : transform.position + transform.right * fallbackDismountDistance;
        rider.position = dismountPosition;

        SetThirdPersonCameraEnabled(false);
        SetFirstPersonCameraEnabled(true);

        if (mountedPlayerRigidbody)
        {
            mountedPlayerRigidbody.isKinematic = playerRigidbodyWasKinematic;
            mountedPlayerRigidbody.useGravity = playerRigidbodyHadGravity;
            mountedPlayerRigidbody.linearVelocity = Vector3.zero;
            mountedPlayerRigidbody.angularVelocity = Vector3.zero;
        }

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
            // If we explicitly enabled look input but cannot hand control back to PlayerLook,
            // clean up here so input action state is not leaked.
            lookAction.Disable();
        }

        if (disablePlayerInteractor && mountedInteractor)
        {
            mountedInteractor.enabled = true;
        }

        mountedPlayer = null;
        mountedPlayerMovement = null;
        mountedPlayerLook = null;
        mountedInteractor = null;
        mountedPlayerRigidbody = null;
        mountedFirstPersonCamera = null;
        mountedFirstPersonCameraRoot = null;
        activeSeatPoint = seatPoint;
        currentMoveInput = Vector2.zero;
        currentSteeringForward = GetSteeringForward();
        jumpPressedThisFrame = false;
        forcedMountedLookActionEnabled = false;
        moveInputVelocityX = 0f;
        moveInputVelocityY = 0f;
        ResetSteeringState();
        SetVisualLean(0f);
        lastMountChangeTime = Time.time;
    }

    private void HandleMountedLook(float deltaTime)
    {
        Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

        // Mouse X adds to the offset from mount forward; mouse Y controls pitch.
        cameraYawOffset += lookInput.x * lookSensitivity * deltaTime;
        mountedPitch = Mathf.Clamp(mountedPitch - lookInput.y * lookSensitivity * deltaTime, -lookPitchClamp, lookPitchClamp);

        // After a short idle period, drift the camera back behind the mount.
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

        // A/D (currentMoveInput.x, already smoothed) rotates the mount body — tank steering.
        float normalizedSpeed = GetMountedNormalizedSpeed();
        float speedSteerScale = Mathf.Lerp(1f, 0.75f, normalizedSpeed);
        smoothedSteerInput = currentMoveInput.x;
        float targetYawRate = smoothedSteerInput * steerSpeed * speedSteerScale;
        float momentumBlend = 1f - Mathf.Exp(-momentumDamping * deltaTime);
        steeringMomentum = Mathf.Lerp(steeringMomentum, targetYawRate, momentumBlend);
        if (Mathf.Abs(smoothedSteerInput) <= 0.01f)
        {
            steeringMomentum = Mathf.Lerp(steeringMomentum, 0f, momentumBlend * 0.6f);
        }

        mountedYaw += steeringMomentum * deltaTime;

        // Mount body directly tracks mountedYaw — no lag, no alignment heuristic needed.
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

        // Tank steering: mount always moves in its own forward direction.
        return transform.forward;
    }

    private void TogglePerspective()
    {
        if (!IsMounted)
        {
            return;
        }

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
            mountedPlayerLook?.SetHeadVisible(false);
        }
        else
        {
            SetFirstPersonCameraEnabled(false);
            SetThirdPersonCameraEnabled(true);
            mountedPlayerLook?.SetHeadVisible(true);
        }

        currentSteeringForward = GetSteeringForward();
    }

    private void EnsureThirdPersonCamera()
    {
        if (thirdPersonCamera)
        {
            return;
        }

        GameObject cameraObject = new GameObject($"{name}_MountThirdPersonCamera");
        thirdPersonCamera = cameraObject.AddComponent<Camera>();
        if (!cameraObject.GetComponent<AudioListener>())
        {
            cameraObject.AddComponent<AudioListener>();
        }
    }

    private Transform GetThirdPersonPivot()
    {
        if (thirdPersonPivot)
        {
            return thirdPersonPivot;
        }

        if (activeSeatPoint)
        {
            return activeSeatPoint;
        }

        return transform;
    }

    private void SetFirstPersonCameraEnabled(bool enabled)
    {
        if (mountedFirstPersonCamera != null)
        {
            mountedFirstPersonCamera.enabled = enabled;
            AudioListener listener = mountedFirstPersonCamera.GetComponent<AudioListener>();
            if (listener)
            {
                listener.enabled = enabled;
            }
        }
    }

    private void SetThirdPersonCameraEnabled(bool enabled)
    {
        EnsureThirdPersonCamera();

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.enabled = enabled;
            AudioListener listener = thirdPersonCamera.GetComponent<AudioListener>();
            if (listener)
            {
                listener.enabled = enabled;
            }
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
        idleAlignFraction = Mathf.Clamp(idleAlignFraction, 0f, 1f);
        thirdPersonFollowLerp = Mathf.Max(0.01f, thirdPersonFollowLerp);
        cameraAutoAlignSpeed = Mathf.Max(0f, cameraAutoAlignSpeed);
        cameraAutoAlignDelay = Mathf.Max(0f, cameraAutoAlignDelay);
    }

    private float GetMountedNormalizedSpeed()
    {
        if (movementMotor != null)
        {
            float speed = movementMotor.Velocity.magnitude;
            if (speed > 0.01f)
            {
                // 8 m/s is a practical "high-speed" reference for mounted steering.
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

        // Drive lean from actual turning rate, not raw look-stick position.
        // steeringMomentum peaks at steerSpeed deg/s → normalise to [-1, 1] so the
        // maximum possible turn rate gives exactly ±leanAmount tilt. This means lean
        // is zero when the mount is not actually rotating, even if the stick is held sideways.
        float normalizedMomentum = steerSpeed > 0.001f
            ? Mathf.Clamp(steeringMomentum / steerSpeed, -1f, 1f)
            : 0f;
        float targetLean = -normalizedMomentum * leanAmount * Mathf.Lerp(0.35f, 1f, normalizedSpeed);
        // leanSmoothTime is independent of turnSmoothTime so lean can trail slightly
        // behind the turn rate, reading as the animal's body mass shifting weight.
        currentLean = Mathf.SmoothDamp(currentLean, targetLean, ref leanVelocity, leanSmoothTime);
        SetVisualLean(currentLean);
    }

    private void DampVisualLeanToNeutral(float deltaTime)
    {
        if (!visualTiltRoot)
        {
            return;
        }

        // Use leanSmoothTime so the damp-to-neutral when unmounted matches the mounted feel.
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
        smoothedSteerInput = 0f;
        steerInputVelocity = 0f;
        steeringMomentum = 0f;
        currentLean = 0f;
        leanVelocity = 0f;
    }
}
