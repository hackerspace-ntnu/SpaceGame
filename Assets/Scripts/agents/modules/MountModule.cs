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

public partial class MountModule : BehaviourModuleBase, IInteractable
{
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
    [SerializeField] private Camera thirdPersonCameraPrefab;

    [Header("While Mounted")]
    [Tooltip("If true, the mount keeps running its own AI modules (wander, patrol, etc.) between rider inputs. " +
             "If false, all non-mount modules are disabled while mounted — the mount stands still when the rider isn't steering.")]
    [SerializeField] private bool allowAISelfMovementWhenMounted = false;

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
    private Camera mountedThirdPersonCamera;

    private MonoBehaviour[] suppressibleModules;

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
    public Camera MountedThirdPersonCamera => mountedThirdPersonCamera;

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

    private void OnDisable()
    {
        if (IsMounted)
            Dismount();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        mountCooldown = Mathf.Max(0f, mountCooldown);
        fallbackDismountDistance = Mathf.Max(0.1f, fallbackDismountDistance);
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
            if (mb is IBehaviourModule && !IsMountAware(mb))
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
