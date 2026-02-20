using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class GlassLensDistortion : MonoBehaviour
{
    [Header("Material")]
    public Material distortionMaterial;
    
    [Header("Lens Settings")]
    [Range(0f, 1f)] public float lensCenterX = 0.5f;
    [Range(0f, 1f)] public float lensCenterY = 0.5f;
    [Range(0f, 10000f)] public float distortionStrength = 5000f;
    [Range(2f, 16f)] public float roundedPower = 8f;
    [Range(0f, 2f)] public float blurSize = 0.5f;
    
    [Header("Mouse Control")]
    public bool followMouse = false;
    
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (distortionMaterial == null)
        {
            Debug.LogWarning("GlassLensDistortion: No material assigned!");
            Graphics.Blit(source, destination);
            return;
        }
        
        Debug.Log("GlassLensDistortion: Effect applied");
        
        // Update lens center
        Vector2 lensCenter = new Vector2(lensCenterX, lensCenterY);
        if (followMouse && Input.mousePosition.magnitude > 1f)
        {
            lensCenter = new Vector2(
                Input.mousePosition.x / Screen.width,
                Input.mousePosition.y / Screen.height
            );
        }
        
        // Set shader properties
        distortionMaterial.SetVector("_LensCenter", lensCenter);
        distortionMaterial.SetFloat("_DistortionStrength", distortionStrength);
        distortionMaterial.SetFloat("_RoundedPower", roundedPower);
        distortionMaterial.SetFloat("_BlurSize", blurSize);
        
        // Apply the effect
        Graphics.Blit(source, destination, distortionMaterial);
    }
}
