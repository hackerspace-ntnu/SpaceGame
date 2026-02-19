using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : NetworkBehaviour
{
    [Header("Input")]
    public InputActionReference lookAction; // Vector2

    [Header("References")]
    public GameObject camera;    
    public Transform playerHead; 
    public Transform playerBody;
    private Rigidbody rigidbody;

    [Header("Settings")]
    public float sensitivity = 1f;
    public float verticalClamp = 80f;

    private float pitch = 0f;
    
    private Vector2 lookInput;
    
    private void Start()
    {
        if(!IsOwner) return;
        camera.SetActive(true);
        // Lock cursor to center
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        rigidbody = playerBody.GetComponent<Rigidbody>();
        
        // Hide the player head mesh to prevent clipping with the camera
        var headRenderer = playerHead.GetComponent<SkinnedMeshRenderer>();
        headRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }
    
    private void OnEnable()
    {
        if(!IsOwner) return;
        lookAction.action.Enable();
    }

    private void OnDisable()
    {
        if(!IsOwner) return;
        lookAction.action.Disable();
    }
    
    void Update()
    {
        if(!IsOwner) return;
        lookInput = lookAction.action.ReadValue<Vector2>();

        // body rotation (yaw)
        float yaw = lookInput.x * sensitivity * Time.deltaTime;
        Quaternion delta = Quaternion.Euler(0f, yaw, 0f);
        rigidbody.MoveRotation(rigidbody.rotation * delta);

        // camera rotation (pitch)
        pitch -= lookInput.y * sensitivity * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, -verticalClamp, verticalClamp);
        camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }


    private void OnDestroy()
    {
        if(!IsOwner) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
