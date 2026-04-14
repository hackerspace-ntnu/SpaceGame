// Core mount lifecycle controller for rider attachment, dismounting, and rider state.
using UnityEngine;
using System;

public partial class MountController : MonoBehaviour
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

    public event Action<PlayerMovement> Mounted;
    public event Action<PlayerMovement> Dismounted;

    public bool IsMounted => mountedPlayer != null;
    public bool IsAvailableForMount => !IsMounted && Time.time >= lastMountChangeTime + mountCooldown;
    public Transform ActiveSeatPoint => activeSeatPoint != null ? activeSeatPoint : seatPoint;
    public Transform MountedPlayerTransform => mountedPlayer;
    public PlayerMovement MountedPlayerMovement => mountedPlayerMovement;
    public PlayerLook MountedPlayerLook => mountedPlayerLook;
    public Interactor MountedInteractor => mountedInteractor;
    public Rigidbody MountedPlayerRigidbody => mountedPlayerRigidbody;
    public Camera MountedFirstPersonCamera => mountedFirstPersonCamera;
    public Transform MountedFirstPersonCameraRoot => mountedFirstPersonCameraRoot;
    public Camera MountedThirdPersonCamera => mountedThirdPersonCamera;

    private void Awake()
    {
        if (!seatPoint)
        {
            seatPoint = transform;
        }

        activeSeatPoint = seatPoint;
    }

    private void OnDisable()
    {
        if (IsMounted)
        {
            Dismount();
        }
    }

    private void OnValidate()
    {
        mountCooldown = Mathf.Max(0f, mountCooldown);
        fallbackDismountDistance = Mathf.Max(0.1f, fallbackDismountDistance);
    }
}
