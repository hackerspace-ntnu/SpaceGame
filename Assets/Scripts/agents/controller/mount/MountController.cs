// Core mount runtime controller for rider state, mounted input, and camera behavior.
// This file holds shared fields/properties while implementation is split by responsibility.
using UnityEngine;
using UnityEngine.InputSystem;

public partial class MountController : MonoBehaviour
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

    [Header("Mounted Look")]
    [SerializeField] private float lookSensitivity = 1f;
    [SerializeField] private float lookPitchClamp = 75f;

    [Header("Mounted Steering Feel")]
    [SerializeField] private float steerSpeed = 120f;
    [SerializeField] private float turnSmoothTime = 0.12f;
    [SerializeField] private float leanAmount = 10f;
    [SerializeField] private float leanSmoothTime = 0.18f;
    [SerializeField] private float momentumDamping = 7f;
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
    private InputAction togglePerspectiveAction;
    private Vector2 currentMoveInput;
    private Vector3 currentSteeringForward;
    private bool jumpPressedThisFrame;
    private float mountedYaw;
    private float cameraYaw;
    private float mountedPitch;
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
}
