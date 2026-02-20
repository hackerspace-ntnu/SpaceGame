using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class VisorOverlayController : MonoBehaviour
{
    [SerializeField] private Camera sourceCamera; // The camera rendering the scene
    [SerializeField] private RawImage visorImage; // The UI RawImage displaying the overlay
    [SerializeField] private Material visorMaterial; // The EVA_Visor material
    [SerializeField] private Light primaryLight; // Primary light source for lens flare (leave empty to auto-detect)
    
    private RenderTexture renderTexture;
    private Camera overlayCamera; // Duplicate camera for rendering to texture
    
    void Start()
    {
        if (sourceCamera == null)
        {
            Debug.LogError("Source Camera not assigned to VisorOverlayController!");
            return;
        }
        
        // Create render texture matching screen resolution
        renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        renderTexture.filterMode = FilterMode.Bilinear;
        
        // Create a duplicate camera that renders to the texture
        GameObject cameraObj = new GameObject("VisorOverlayCamera");
        cameraObj.transform.SetParent(sourceCamera.transform);
        cameraObj.transform.localPosition = Vector3.zero;
        cameraObj.transform.localRotation = Quaternion.identity;
        
        overlayCamera = cameraObj.AddComponent<Camera>();
        overlayCamera.CopyFrom(sourceCamera);
        overlayCamera.targetTexture = renderTexture;
        overlayCamera.depth = sourceCamera.depth - 1; // Render before main camera
        
        // Assign render texture to material
        if (visorMaterial != null)
        {
            visorMaterial.SetTexture("_MainTex", renderTexture);
        }
        else
        {
            Debug.LogError("Visor Material not assigned!");
        }
        
        // Assign to RawImage
        if (visorImage != null)
        {
            visorImage.texture = renderTexture;
            visorImage.material = visorMaterial;
        }
        else
        {
            Debug.LogError("Visor Image (RawImage) not assigned!");
        }
        
        Debug.Log("VisorOverlayController initialized successfully");
    }
    
    void Update()
    {
        // Keep overlay camera synced with source camera
        if (overlayCamera != null && sourceCamera != null)
        {
            overlayCamera.fieldOfView = sourceCamera.fieldOfView;
            overlayCamera.transform.position = sourceCamera.transform.position;
            overlayCamera.transform.rotation = sourceCamera.transform.rotation;
        }
        
        // Update light source position in shader
        if (visorMaterial != null)
        {
            // Auto-detect primary light if not assigned
            if (primaryLight == null)
            {
                Light[] lights = FindObjectsOfType<Light>();
                float maxIntensity = 0;
                foreach (Light light in lights)
                {
                    if (light.intensity > maxIntensity && light.enabled)
                    {
                        maxIntensity = light.intensity;
                        primaryLight = light;
                    }
                }
            }
            
            // Convert light direction to screen space position
            if (primaryLight != null && sourceCamera != null)
            {
                Vector3 lightDir = -primaryLight.transform.forward; // Direction light is shining
                
                // For directional light, use the direction to calculate screen position
                // Project a point far in front of camera in light direction
                Vector3 worldPoint = sourceCamera.transform.position + lightDir * 1000f;
                Vector3 screenPos = sourceCamera.WorldToViewportPoint(worldPoint);
                
                // Clamp to ensure it's on screen (flares work best when light is visible)
                screenPos.x = Mathf.Clamp01(screenPos.x);
                screenPos.y = Mathf.Clamp01(screenPos.y);
                
                visorMaterial.SetVector("_LightSourcePosition", new Vector4(screenPos.x, screenPos.y, 0, 0));
                
                // Debug info
                Debug.Log($"Light Direction: {lightDir}, Screen Pos: ({screenPos.x:F2}, {screenPos.y:F2})");
            }
            else
            {
                // Default to center if no light found
                visorMaterial.SetVector("_LightSourcePosition", new Vector4(0.5f, 0.5f, 0, 0));
            }
        }
    }
    
    void OnDestroy()
    {
        // Clean up
        if (overlayCamera != null)
        {
            Destroy(overlayCamera.gameObject);
        }
        
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}