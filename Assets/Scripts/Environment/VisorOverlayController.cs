using UnityEngine;

public class VisorOverlayController : MonoBehaviour
{
    [SerializeField] private Camera sourceCamera; // The camera for light tracking
    [SerializeField] private Material visorMaterial; // The EVA_Visor material
    [SerializeField] private Light primaryLight; // Primary light source for lens flare (leave empty to auto-detect)
    [SerializeField] private float maxRaycastDistance = 10000f; // Maximum distance to check for sun occlusion
    [SerializeField] private float offScreenFlareMultiplier = 0.3f; // Flare intensity when sun is off-screen
    
    void Start()
    {
        if (visorMaterial == null)
        {
            Debug.LogError("Visor Material not assigned!");
        }
    }
    
    void Update()
    {
        // Auto-detect camera if not set or if it became null (prefab replacement scenario)
        if (sourceCamera == null)
        {
            sourceCamera = Camera.main;
        }
        
        // Update light source position in shader
        if (visorMaterial != null && sourceCamera != null)
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
                Vector3 cameraPos = sourceCamera.transform.position;
                Vector3 cameraForward = sourceCamera.transform.forward;
                
                // Calculate angle between camera forward and light direction
                float dotProduct = Vector3.Dot(cameraForward, lightDir);
                
                // Check if sun is hitting the front of the helmet (within ~180 degree cone)
                bool lightHittingVisor = dotProduct > 0;
                
                // Raycast to check if light path is occluded
                bool lightVisible = false;
                if (lightHittingVisor)
                {
                    Ray ray = new Ray(cameraPos, lightDir);
                    // If raycast doesn't hit anything, the sun is visible
                    lightVisible = !Physics.Raycast(ray, maxRaycastDistance);
                }
                
                // Calculate screen space position for on-screen flares
                Vector3 worldPoint = cameraPos + lightDir * 1000f;
                Vector3 screenPos = sourceCamera.WorldToViewportPoint(worldPoint);
                
                // Check if light is on screen
                bool onScreen = screenPos.z > 0 && screenPos.x >= 0 && screenPos.x <= 1 && screenPos.y >= 0 && screenPos.y <= 1;
                
                // Calculate flare intensity based on multiple factors
                float flareIntensity = 0f;
                
                if (lightVisible)
                {
                    if (onScreen)
                    {
                        // Full intensity when on screen and visible
                        flareIntensity = 1.0f;
                        
                        // Clamp screen position
                        screenPos.x = Mathf.Clamp01(screenPos.x);
                        screenPos.y = Mathf.Clamp01(screenPos.y);
                    }
                    else if (lightHittingVisor)
                    {
                        // Reduced intensity when off-screen but hitting visor
                        // Use dot product to fade based on angle (stronger when more centered)
                        flareIntensity = dotProduct * offScreenFlareMultiplier;
                        
                        // Position flare at edge of screen in the direction of the light
                        // Project light direction onto screen edge
                        Vector3 screenDir = screenPos - new Vector3(0.5f, 0.5f, 0);
                        screenDir.z = 0;
                        
                        if (screenDir.magnitude > 0.001f)
                        {
                            screenDir.Normalize();
                            
                            // Find intersection with screen edge
                            float maxDist = Mathf.Max(Mathf.Abs(screenDir.x), Mathf.Abs(screenDir.y));
                            if (maxDist > 0)
                            {
                                screenDir /= maxDist * 2f;
                            }
                            
                            screenPos.x = 0.5f + screenDir.x;
                            screenPos.y = 0.5f + screenDir.y;
                            screenPos.x = Mathf.Clamp01(screenPos.x);
                            screenPos.y = Mathf.Clamp01(screenPos.y);
                        }
                        else
                        {
                            // Default to center if calculation fails
                            screenPos = new Vector3(0.5f, 0.5f, 0);
                        }
                    }
                }
                
                // Send data to shader
                visorMaterial.SetVector("_LightSourcePosition", new Vector4(screenPos.x, screenPos.y, flareIntensity, 0));
                
               
            }
            else
            {
                // Default to center if no light found
                visorMaterial.SetVector("_LightSourcePosition", new Vector4(0.5f, 0.5f, 0, 0));
            }
        }
    }
}