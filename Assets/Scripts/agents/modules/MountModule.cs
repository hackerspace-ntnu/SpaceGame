// Single-component mount lifecycle owner. Absorbs what used to be three classes:
// MountController (rider attach/detach + rider state), MountInteractor (IInteractable surface),
// and MountSuppressorModule (disables other modules while mounted).
//
// A fully mountable entity now needs just two modules:
//   • MountModule (this — lifecycle, state, interaction surface, AI suppression)
//   • SteerModule (rider input → movement, camera, jump, leap)
// Plus the usual AgentController + IMovementMotor if you want AI in the downtime between inputs.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Late execution order so our LateUpdate runs after any other LateUpdate in the scene —
// guarantees the mounted third-person camera transform isn't overwritten afterwards.
[DefaultExecutionOrder(1000)]
public partial class MountModule : BehaviourModuleBase, IInteractable
{
    public enum CameraPerspective
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("Mount Points")]
    [SerializeField] private Transform seatPoint;
    [SerializeField] private Transform dismountPoint;

    [Header("Player Components To Toggle")]
    [SerializeField] private bool disablePlayerMovement = true;
    [SerializeField] private bool disablePlayerLook = true;
    [SerializeField] private bool disablePlayerInteractor = true;

    [Header("Dismount")]
    [SerializeField] private float mountCooldown = 0.25f;
    [SerializeField] private float fallbackDismountDistance = 1.6f;

    [Header("Mounted Camera")]
    [SerializeField] private CameraPerspective defaultPerspective = CameraPerspective.ThirdPerson;
    [Tooltip("Prefab spawned and parented to the mount when entering third-person view. Falls back " +
             "to a clone of Camera.main if null. Leave default unless this vehicle needs custom render settings.")]
    [SerializeField] private Camera thirdPersonCameraPrefab;
    [SerializeField] private Transform thirdPersonPivot;
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 2.2f, -3.8f);
    [SerializeField] private float thirdPersonDistance = 3.8f;
    [SerializeField] private float thirdPersonFollowLerp = 14f;
    [Tooltip("Meters ahead of the pivot the camera aims at. Higher = camera tilts further down, shows more ground ahead.")]
    [SerializeField] private float thirdPersonLookAhead = 6f;

    [Header("Mounted Look")]
    [SerializeField] private string lookActionName = "Look";
    [SerializeField] private float lookSensitivity = 1f;
    [SerializeField] private float lookPitchClamp = 75f;
    [SerializeField] private float defaultMountedPitch = -15f;
    [SerializeField] private float cameraAutoAlignSpeed = 90f;
    [SerializeField] private float cameraAutoAlignDelay = 0.5f;

    [Header("While Mounted")]
    [Tooltip("If true, the mount keeps running its own AI modules (wander, patrol, etc.) between rider inputs. " +
             "If false, all non-mount modules are disabled while mounted — the mount stands still when the rider isn't steering.")]
    [SerializeField] private bool allowAISelfMovementWhenMounted = false;

    // Camera / look runtime state
    private InputAction lookAction;
    private bool forcedLookActionEnabled;
    private Camera runtimeThirdPersonCamera;
    private float mountedPitch;
    private float cameraYaw;
    private float cameraYawOffset;
    private float timeSinceLastLookInput;
    private CameraPerspective activePerspective;

    // Rider state
    private Transform mountedPlayer;
    private PlayerMovement mountedPlayerMovement;
    private PlayerLook mountedPlayerLook;
    private Interactor mountedInteractor;
    private Rigidbody mountedPlayerRigidbody;
    private bool playerRigidbodyWasKinematic;
    private bool playerRigidbodyHadGravity;
    private float lastMountChangeTime;

    private Transform activeSeatPoint;
    private Camera mountedFirstPersonCamera;
    private Transform mountedFirstPersonCameraRoot;

    private MonoBehaviour[] suppressibleModules;

    // Animator state captured at mount time so root-motion-driven drift is suppressed while
    // ridden and restored on dismount.
    private Animator[] suppressibleAnimators;
    private bool[] suppressibleAnimatorRootMotion;

    // Rigidbody constraints captured at mount time so physics can't spin the mount via
    // contact forces (notably the rider's own collider overlapping the seat point).
    private Rigidbody ownRigidbody;
    private RigidbodyConstraints ownRigidbodyConstraints;
    private bool ownRigidbodyConstraintsCaptured;

    // Rider<->mount collider pairs ignored while mounted so the rider's kinematic collider
    // doesn't push the mount around. Restored on dismount.
    private (Collider a, Collider b)[] ignoredCollisionPairs;

    public event Action<PlayerMovement> Mounted;
    public event Action<PlayerMovement> Dismounted;

    // ─────────── Public API ───────────
    public bool IsMounted => mountedPlayer != null;
    public bool IsAvailableForMount => !IsMounted && Time.time >= lastMountChangeTime + mountCooldown;
    public bool AllowAISelfMovementWhenMounted => allowAISelfMovementWhenMounted;
    public Transform ActiveSeatPoint => activeSeatPoint != null ? activeSeatPoint : seatPoint;
    public Transform MountedPlayerTransform => mountedPlayer;
    public PlayerMovement MountedPlayerMovement => mountedPlayerMovement;
    public PlayerLook MountedPlayerLook => mountedPlayerLook;
    public Interactor MountedInteractor => mountedInteractor;
    public Rigidbody MountedPlayerRigidbody => mountedPlayerRigidbody;
    public Camera MountedFirstPersonCamera => mountedFirstPersonCamera;
    public Transform MountedFirstPersonCameraRoot => mountedFirstPersonCameraRoot;
    public Camera MountedThirdPersonCamera => runtimeThirdPersonCamera;
    public CameraPerspective ActivePerspective => activePerspective;
    public float CameraYaw => cameraYaw;
    public float CameraYawOffset => cameraYawOffset;
    public float MountedPitch => mountedPitch;

    public override string ModuleDescription =>
        "Mount lifecycle + interaction surface + AI suppression. Drop this + SteerModule to make anything mountable.\n\n" +
        "• Implements IInteractable — players mount by interacting.\n" +
        "• Fires Mounted/Dismounted events.\n" +
        "• When allowAISelfMovementWhenMounted = false, disables non-mount IBehaviourModules for the duration.";

    private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

    // ─────────── Lifecycle ───────────
    private void Awake()
    {
        if (!seatPoint)
            seatPoint = transform;
        activeSeatPoint = seatPoint;
        CacheSuppressibleModules();
    }

    private void OnEnable()
    {
        ResolveCameraInputActions();
    }

    private void OnDisable()
    {
        if (IsMounted)
            Dismount();

        if (forcedLookActionEnabled && lookAction != null)
        {
            lookAction.Disable();
            forcedLookActionEnabled = false;
        }
    }

    private void Update()
    {
        if (!IsMounted)
            return;

        EnsureLookActionEnabled();
        HandleLookInput(Time.deltaTime);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        mountCooldown = Mathf.Max(0f, mountCooldown);
        fallbackDismountDistance = Mathf.Max(0.1f, fallbackDismountDistance);
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
        lookPitchClamp = Mathf.Clamp(lookPitchClamp, 0f, 89f);
        thirdPersonDistance = Mathf.Max(0.1f, thirdPersonDistance);
        thirdPersonFollowLerp = Mathf.Max(0.01f, thirdPersonFollowLerp);
        thirdPersonLookAhead = Mathf.Max(0.1f, thirdPersonLookAhead);
        cameraAutoAlignSpeed = Mathf.Max(0f, cameraAutoAlignSpeed);
        cameraAutoAlignDelay = Mathf.Max(0f, cameraAutoAlignDelay);
    }

    // MountModule never produces movement. Null → AgentController falls through to other modules
    // (or to MoveIntent.Idle() if none matched).
    public override MoveIntent? Tick(in AgentContext context, float deltaTime) => null;

    // ─────────── IInteractable ───────────
    public bool CanInteract() => IsAvailableForMount;

    public void Interact(Interactor interactor)
    {
        TryMount(interactor, transform);
    }

    // ─────────── Suppressor ───────────
    public void RefreshModuleCache() => CacheSuppressibleModules();

    private void CacheSuppressibleModules()
    {
        List<MonoBehaviour> list = new List<MonoBehaviour>();
        MonoBehaviour[] all = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour mb in all)
        {
            // Suppress anything that could produce movement or a MoveIntent while mounted:
            // IBehaviourModule (except Mount/Steer themselves) and legacy IAgentBrain fallbacks.
            // Without this, e.g. a legacy NpcBrain/EnemyBrain would keep feeding intents to the
            // motor and make the mount drift/circle while the rider is idle.
            if ((mb is IBehaviourModule || mb is IAgentBrain) && !IsMountAware(mb))
                list.Add(mb);
        }
        suppressibleModules = list.ToArray();
    }

    // Modules that must keep running while mounted so the rider can actually drive.
    private static bool IsMountAware(MonoBehaviour mb)
    {
        return mb is MountModule || mb is SteerModule;
    }

    private void ApplyModuleSuppression()
    {
        if (allowAISelfMovementWhenMounted || suppressibleModules == null)
            return;
        foreach (MonoBehaviour mb in suppressibleModules)
            if (mb) mb.enabled = false;
    }

    private void RestoreModuleSuppression()
    {
        if (suppressibleModules == null)
            return;
        foreach (MonoBehaviour mb in suppressibleModules)
            if (mb) mb.enabled = true;
    }
}
