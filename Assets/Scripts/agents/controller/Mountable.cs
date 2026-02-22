using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public class Mountable : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("If enabled, the mountable has two gears: Walk and Run.")]
    [SerializeField] private bool runnable = true;

    [Tooltip("The speed when walking.")]
    [SerializeField] private float walkSpeed = 5f;

    [Tooltip("The speed when running. Defaults to 1.5x walkSpeed.")]
    [SerializeField] private float runSpeed = 7.5f;

    [Header("Input Settings")]
    [Tooltip("Max time between key presses to register a double tap.")]
    [SerializeField] private float doubleTapThreshold = 0.3f;

    [Header("Camera Settings")]
    [Tooltip("The camera prefab to generate when mounting.")]
    [SerializeField] private GameObject thirdPersonCameraPrefab;
    [Tooltip("A reference to the player's main camera that should be disabled on mount.")]
    [SerializeField] private Camera mainCamera;

    private float lastWPressTime = -100f; // Initialize to allow immediate first press
    private bool isRunning;
    private GameObject _generatedCamera;
    private bool _isMounted;

    /// <summary>
    /// Returns the current speed based on the input state.
    /// </summary>
    public float CurrentSpeed => (runnable && isRunning) ? runSpeed : walkSpeed;

    private void Update()
    {
        HandleInput();
    }

    private void HandleInput()
    {
        // Ensure the keyboard is present
        if (Keyboard.current == null) return;

        var wKey = Keyboard.current.wKey;

        // Check for key press
        if (wKey.wasPressedThisFrame)
        {
            // If pressed again within threshold, start running
            if (Time.time - lastWPressTime <= doubleTapThreshold)
            {
                isRunning = true;
            }
            
            // Update the last press time
            lastWPressTime = Time.time;
        }

        // Stop running when key is released
        if (wKey.wasReleasedThisFrame)
        {
            isRunning = false;
        }
    }

    public void EnableMountCamera()
    {
        if (mainCamera == null)
        {
            Debug.LogError($"[Mountable] Main Camera is not assigned on {gameObject.name}! Please assign it in the inspector.");
            return;
        }

        if (thirdPersonCameraPrefab == null)
        {
            Debug.LogError($"[Mountable] Third Person Camera Prefab is not assigned on {gameObject.name}!");
            return;
        }

        if (_generatedCamera == null)
        {
            _generatedCamera = Instantiate(thirdPersonCameraPrefab, transform);
        }

        mainCamera.gameObject.SetActive(false);
        _generatedCamera.SetActive(true);

        // Re-configure every time we enable it to ensure settings are correct
        var cam = _generatedCamera.GetComponent<Camera>();
        if (cam != null) ConfigureCameraRendering(cam);

        _isMounted = true;
    }

    public void DisableMountCamera()
    {
        if (_generatedCamera != null) 
            _generatedCamera.SetActive(false);
        
        if(mainCamera != null)
            mainCamera.gameObject.SetActive(true);

        _isMounted = false;
    }

    private void ConfigureCameraRendering(Camera cam)
    {
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.enabled = true;
        
        var cameraData = cam.GetUniversalAdditionalCameraData();
        if (cameraData == null) cameraData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        
        if (cameraData != null)
        {
            // Critical: Must be Base to render to screen. Overlay cameras only render on top of others.
            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(0); // Default Renderer (Index 0)
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraData.renderShadows = true;
            cameraData.dithering = true;
        }
    }
}