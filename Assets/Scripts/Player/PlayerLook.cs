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
        // Lock cursor to center
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
}
