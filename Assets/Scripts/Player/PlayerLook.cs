using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference lookAction; // Vector2

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
    
    private void Start()
    {
        // Lock cursor to center
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        playerRigidbody = playerBody.GetComponent<Rigidbody>();
        
        // Hide the player head mesh to prevent clipping with the camera
        var headRenderer = playerHead.GetComponent<SkinnedMeshRenderer>();
        headRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }
    
    private void OnEnable()
    {
        lookAction.action.Enable();
    }

    private void OnDisable()
    {
        lookAction.action.Disable();
    }
    
    void Update()
    {
        lookInput = lookAction.action.ReadValue<Vector2>();

        // body rotation (yaw)
        float yaw = lookInput.x * sensitivity * Time.deltaTime;
        Quaternion delta = Quaternion.Euler(0f, yaw, 0f);
        playerRigidbody.MoveRotation(playerRigidbody.rotation * delta);

        // camera rotation (pitch)
        pitch -= lookInput.y * sensitivity * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, -verticalClamp, verticalClamp);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }


    public void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
