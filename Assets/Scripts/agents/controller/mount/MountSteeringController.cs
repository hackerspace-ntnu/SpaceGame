using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(MountController))]
public partial class MountSteeringController : MonoBehaviour
{
    public enum CameraPerspective
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("References")]
    [SerializeField] private MountController mountController;

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
    [SerializeField] private float thirdPersonDistance = 3.8f;
    [SerializeField] private float thirdPersonFollowLerp = 14f;
    [SerializeField] private float cameraAutoAlignSpeed = 90f;
    [SerializeField] private float cameraAutoAlignDelay = 0.5f;
    [SerializeField] private string perspectiveToggleActionName = "Next";
    [SerializeField] private float steeringOverrideThreshold = 0.1f;

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction togglePerspectiveAction;
    private Vector2 currentMoveInput;
    private Vector3 currentSteeringForward;
    private bool jumpPressedThisFrame;
    private bool hasSteeringOverride;
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
    private bool forcedMountedLookActionEnabled;
    private IMovementMotor movementMotor;
    private Quaternion visualTiltBaseLocalRotation;
    private CameraPerspective activePerspective;

    public bool HasSteeringOverride => hasSteeringOverride;
    public Vector2 CurrentMoveInput => currentMoveInput;
    public Vector3 CurrentSteeringForward => currentSteeringForward;
    public float ThirdPersonDistance => GetResolvedThirdPersonDistance();

    public void SetThirdPersonDistance(float distance)
    {
        thirdPersonDistance = Mathf.Max(0.1f, distance);
    }

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
