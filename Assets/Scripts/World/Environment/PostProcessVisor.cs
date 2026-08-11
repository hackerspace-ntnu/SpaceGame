using UnityEngine;

[RequireComponent(typeof(Camera))]
public class PostProcessVisor : MonoBehaviour
{
    [Header("Glass Lens Distortion")]
    public Material glassDistortionMaterial;
    
    [Header("Lens Settings")]
    [Range(0f, 1f)] public float lensCenterX = 0.5f;
    [Range(0f, 1f)] public float lensCenterY = 0.5f;
    [Range(0f, 10000f)] public float distortionStrength = 5000f;
    [Range(2f, 16f)] public float roundedPower = 8f;
    [Range(0f, 2f)] public float blurSize = 0.5f;
    
    private Camera cam;
    
    void Awake()
    {
        cam = GetComponent<Camera>();
        Debug.Log($"[PostProcessVisor] Awake - Camera: {cam != null}, Enabled: {enabled}, GameObject: {gameObject.name}");
    }
    
    void Start()
    {
        Debug.Log($"[PostProcessVisor] Start - Camera: {cam != null}, Material: {glassDistortionMaterial != null}");
        if (glassDistortionMaterial == null)
        {
            Debug.LogError("[PostProcessVisor] NO MATERIAL ASSIGNED! Post-processing will not work!");
        }
    }
    
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Debug.Log($"[PostProcessVisor] OnRenderImage CALLED! Material: {glassDistortionMaterial != null}, Source: {source.width}x{source.height}");
        
        if (glassDistortionMaterial == null)
        {
            Debug.LogError("[PostProcessVisor] Material is NULL in OnRenderImage!");
            Graphics.Blit(source, destination);
            return;
        }
        
        // Update material properties
        glassDistortionMaterial.SetVector("_LensCenter", new Vector2(lensCenterX, lensCenterY));
        glassDistortionMaterial.SetFloat("_DistortionStrength", distortionStrength);
        glassDistortionMaterial.SetFloat("_RoundedPower", roundedPower);
        glassDistortionMaterial.SetFloat("_BlurSize", blurSize);
        
        Debug.Log($"[PostProcessVisor] Applying effect with distortion: {distortionStrength}");
        
        // Apply the glass distortion effect
        Graphics.Blit(source, destination, glassDistortionMaterial);
        
        Debug.Log("[PostProcessVisor] Graphics.Blit completed");
    }
}
