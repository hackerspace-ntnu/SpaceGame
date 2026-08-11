using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : MonoBehaviour
{
    private PlayerInputManager inputs;
    [Header("References")]
    public GameObject playerCamera;    
    public Transform playerHead; 
    public Transform playerBody;
    public Transform cameraRoot => playerCamera != null ? playerCamera.transform : null;
    private Rigidbody playerRigidbody;

    [Header("Settings")]
    public float sensitivity = 1f;
    public float verticalClamp = 80f;

    private float pitch = 0f;

    private Vector2 lookInput;
    private SkinnedMeshRenderer headRenderer;

    private void Start()
    {
        inputs = GetComponent<PlayerController>().Input;
        playerRigidbody = playerBody.GetComponent<Rigidbody>();
        
        // Hide the player head mesh to prevent clipping with the camera
        headRenderer = playerHead.GetComponent<SkinnedMeshRenderer>();
        headRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }

    public void SetHeadVisible(bool visible)
    {
        if (!headRenderer) return;
        headRenderer.shadowCastingMode = visible
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }
    
    private void OnEnable()
    {
        ApplyCursorLock();
    }

    private void OnDisable()
    {
        ReleaseCursorLock();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && isActiveAndEnabled)
        {
            ApplyCursorLock();
        }
    }

    private void LateUpdate()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
        {
            ApplyCursorLock();
        }
    }
    
    void Update()
    {
        lookInput = inputs.LookInput;

        // body rotation (yaw)
        float yaw = lookInput.x * sensitivity * Time.deltaTime;
        Quaternion delta = Quaternion.Euler(0f, yaw, 0f);
        playerRigidbody.MoveRotation(playerRigidbody.rotation * delta);

        // camera rotation (pitch)
        pitch -= lookInput.y * sensitivity * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, -verticalClamp, verticalClamp);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void ApplyCursorLock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ReleaseCursorLock()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
