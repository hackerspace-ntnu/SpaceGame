using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Day/Night Cycle Settings")]
    [Tooltip("Duration of a full day/night cycle in seconds")]
    public float cycleDuration = 120f;
    
    
    [Tooltip("Starting time of day (0 = midnight, 0.5 = noon)")]
    [Range(0f, 1f)]
    public float startTimeOfDay = 0.25f;
    
    [Header("Rotation Settings")]
    [Tooltip("The axis around which the light rotates (typically X-axis for sun/moon)")]
    public Vector3 rotationAxis = Vector3.right;
    
    private Light directionalLight;
    private float currentTimeOfDay;
    
    void Start()
    {
        // Get the Light component attached to this GameObject
        directionalLight = GetComponent<Light>();
        
        if (directionalLight == null)
        {
            Debug.LogError("DayNightCycle: No Light component found on this GameObject!");
            enabled = false;
            return;
        }
        
        if (directionalLight.type != LightType.Directional)
        {
            Debug.LogWarning("DayNightCycle: Light is not set to Directional type!");
        }
        
        currentTimeOfDay = startTimeOfDay;
        UpdateLightRotation();
    }

    void Update()
    {
        // Update time of day based on cycle duration
        currentTimeOfDay += Time.deltaTime / cycleDuration;
        
        // Keep time in 0-1 range
        if (currentTimeOfDay >= 1f)
        {
            currentTimeOfDay -= 1f;
        }
        
        UpdateLightRotation();
    }
    
    private void UpdateLightRotation()
    {
        // Convert time of day (0-1) to rotation angle (0-360 degrees)
        float rotationAngle = currentTimeOfDay * 360f;
        
        // Apply rotation around the specified axis
        transform.rotation = Quaternion.AngleAxis(rotationAngle, rotationAxis);
    }
}
