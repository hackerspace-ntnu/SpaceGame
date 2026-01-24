using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference lookAction; // Vector2

    [Header("References")]
    public Transform cameraRoot;          
    public Transform playerBody;
    private Rigidbody rigidbody;

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
        rigidbody = playerBody.GetComponent<Rigidbody>();
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
        //body rotation (yaw)
        float yaw = lookInput.x * sensitivity * Time.fixedDeltaTime;
        Quaternion delta = Quaternion.Euler(0f, yaw, 0f);
        rigidbody.MoveRotation(rigidbody.rotation * delta);
        
        //camera rotation (pitch)
        lookInput = lookAction.action.ReadValue<Vector2>();
        pitch -= lookInput.y * sensitivity * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, -verticalClamp, verticalClamp);
        cameraRoot.localRotation = Quaternion.Euler(pitch, 0, 0);
    }
    
    void OnGUI()
    {
        float size = 40f; // size of the arrow
        float x = Screen.width / 2;
        float y = Screen.height / 2;
        
        GUI.Label(new Rect(x - size/2, y - size/2, size, size), "+"); 
    }
}
