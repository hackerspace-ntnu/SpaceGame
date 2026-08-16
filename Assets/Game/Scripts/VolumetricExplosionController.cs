using UnityEngine;

/// <summary>
/// Clean controller for volumetric explosion shader
/// Provides simple on/off control and parameter adjustment
/// </summary>
public class VolumetricExplosionController : MonoBehaviour
{
    [Header("Speed & Scale")]
    [SerializeField] private float explosionSpeed = 1.0f;
    [SerializeField] private float explosionScale = 1.0f;

    [Header("Colors")]
    [SerializeField] private Color baseColor = new Color(0.82f, 0.07f, 0.00f, 1.0f);
    [SerializeField] private Color highlightColor = new Color(1.0f, 0.78f, 0.18f, 1.0f);
    [SerializeField] private Color sootColor = new Color(0.02f, 0.018f, 0.016f, 1.0f);

    [Header("Effects")]
    [SerializeField] private float overallIntensity = 1.0f;
    [SerializeField] private float noiseScale = 1.0f;

    private Material material;
    private Renderer meshRenderer;
    private bool isActive = false;

    private void OnEnable()
    {
        InitializeMaterial();
    }

    private void InitializeMaterial()
    {
        meshRenderer = GetComponent<Renderer>();
        if (meshRenderer == null)
        {
            Debug.LogError($"VolumetricExplosionController on '{gameObject.name}' requires a Renderer component!");
            return;
        }

        material = meshRenderer.material;
        if (material == null)
        {
            Debug.LogError($"VolumetricExplosionController on '{gameObject.name}': Renderer has no material!");
            return;
        }

        // Apply initial parameters
        ApplyMaterialParameters();
        
        // Start hidden
        meshRenderer.enabled = false;
    }

    private void ApplyMaterialParameters()
    {
        if (material == null)
            return;

        material.SetFloat("_Speed", explosionSpeed);
        material.SetFloat("_Scale", explosionScale);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_HighlightColor", highlightColor);
        material.SetColor("_SootColor", sootColor);
        material.SetFloat("_Intensity", overallIntensity);
        material.SetFloat("_NoiseScale", noiseScale);
        material.SetFloat("_IsActive", isActive ? 1.0f : 0.0f);
    }

    /// <summary>
    /// Turn the explosion effect on
    /// </summary>
    public void PlayExplosion()
    {
        if (material == null)
            InitializeMaterial();

        if (material == null)
        {
            Debug.LogError("Cannot play explosion - material failed to initialize!");
            return;
        }

        isActive = true;
        meshRenderer.enabled = true;
        material.SetFloat("_IsActive", 1.0f);

        Debug.Log($"Explosion activated on '{gameObject.name}'");
    }

    /// <summary>
    /// Turn the explosion effect off
    /// </summary>
    public void StopExplosion()
    {
        isActive = false;
        
        if (material != null)
            material.SetFloat("_IsActive", 0.0f);
        
        if (meshRenderer != null)
            meshRenderer.enabled = false;

        Debug.Log($"Explosion deactivated on '{gameObject.name}'");
    }

    /// <summary>
    /// Toggle explosion on/off
    /// </summary>
    public void ToggleExplosion()
    {
        if (isActive)
            StopExplosion();
        else
            PlayExplosion();
    }

    [ContextMenu("Play Explosion")]
    private void EditorPlayExplosion()
    {
        PlayExplosion();
    }

    [ContextMenu("Stop Explosion")]
    private void EditorStopExplosion()
    {
        StopExplosion();
    }

    #region Parameter Setters

    public void SetExplosionSpeed(float speed)
    {
        explosionSpeed = speed;
        if (material != null)
            material.SetFloat("_Speed", explosionSpeed);
    }

    public void SetExplosionScale(float scale)
    {
        explosionScale = scale;
        if (material != null)
            material.SetFloat("_Scale", explosionScale);
    }

    public void SetBaseColor(Color color)
    {
        baseColor = color;
        if (material != null)
            material.SetColor("_BaseColor", baseColor);
    }

    public void SetHighlightColor(Color color)
    {
        highlightColor = color;
        if (material != null)
            material.SetColor("_HighlightColor", highlightColor);
    }

    public void SetSootColor(Color color)
    {
        sootColor = color;
        if (material != null)
            material.SetColor("_SootColor", sootColor);
    }

    public void SetOverallIntensity(float intensity)
    {
        overallIntensity = intensity;
        if (material != null)
            material.SetFloat("_Intensity", overallIntensity);
    }

    public void SetNoiseScale(float scale)
    {
        noiseScale = scale;
        if (material != null)
            material.SetFloat("_NoiseScale", noiseScale);
    }

    #endregion

    #region Getters

    public bool IsActive => isActive;
    public float ExplosionSpeed => explosionSpeed;
    public float ExplosionScale => explosionScale;
    public Color BaseColor => baseColor;
    public Color HighlightColor => highlightColor;
    public Color SootColor => sootColor;
    public float OverallIntensity => overallIntensity;

    #endregion
}
