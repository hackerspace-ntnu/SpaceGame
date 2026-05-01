using UnityEngine;

/// <summary>
/// Controller for volumetric explosion shader
/// Manages material properties and explosion timing
/// </summary>
public class VolumetricExplosionController : MonoBehaviour
{
    [SerializeField] private float explosionSpeed = 1.0f;
    [SerializeField] private float explosionScale = 0.5f;
    [SerializeField] private float explosionDuration = 3.0f;
    [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [SerializeField] private bool loopExplosion = true;
    
    private Material material;
    private Renderer meshRenderer;
    private float explosionStartTime;
    private bool isExploding = false;

    private void OnEnable()
    {
        InitializeMaterial();
    }

    private void InitializeMaterial()
    {
        meshRenderer = GetComponent<Renderer>();
        if (meshRenderer == null)
        {
            Debug.LogError("VolumetricExplosionController requires a Renderer component!");
            return;
        }

        material = meshRenderer.material;
    }

    private void Update()
    {
        if (isExploding)
        {
            float elapsedTime = Time.time - explosionStartTime;
            float progress = elapsedTime / explosionDuration;

            if (progress >= 1.0f)
            {
                if (loopExplosion)
                {
                    // Restart explosion - reset time to 0
                    explosionStartTime = Time.time;
                    elapsedTime = 0f;
                    progress = 0f;
                }
                else
                {
                    // Explosion finished - stop
                    isExploding = false;
                    GetComponent<Renderer>().enabled = false;
                    return;
                }
            }
            
            // Update intensity and pass elapsed time to shader
            float intensity = intensityCurve.Evaluate(progress);
            UpdateMaterial(intensity, elapsedTime);
        }
    }

    private void UpdateMaterial(float intensity = 1.0f, float customTime = 0f)
    {
        if (material == null) return;

        material.SetFloat("_Speed", explosionSpeed);
        material.SetFloat("_Scale", explosionScale);
        material.SetFloat("_ExplosionIntensity", intensity);
        material.SetFloat("_CustomTime", customTime);
    }

    /// <summary>
    /// Start the explosion effect
    /// </summary>
    public void PlayExplosion()
    {
        if (meshRenderer == null) InitializeMaterial();
        
        isExploding = true;
        explosionStartTime = Time.time;
        GetComponent<Renderer>().enabled = true;
        UpdateMaterial();
    }

    /// <summary>
    /// Stop the explosion effect
    /// </summary>
    public void StopExplosion()
    {
        isExploding = false;
        GetComponent<Renderer>().enabled = false;
    }

    /// <summary>
    /// Set the speed of the explosion animation
    /// </summary>
    public void SetExplosionSpeed(float newSpeed)
    {
        explosionSpeed = newSpeed;
        if (material != null)
        {
            material.SetFloat("_Speed", explosionSpeed);
        }
    }

    /// <summary>
    /// Set the scale of the explosion
    /// </summary>
    public void SetExplosionScale(float newScale)
    {
        explosionScale = newScale;
        if (material != null)
        {
            material.SetFloat("_Scale", explosionScale);
        }
    }

    /// <summary>
    /// Set explosion duration
    /// </summary>
    public void SetExplosionDuration(float newDuration)
    {
        explosionDuration = newDuration;
    }

    public bool IsExploding => isExploding;
}
