using UnityEngine;

/// <summary>
/// Controller for the Starship shader effect.
/// Manages material properties and texture assignment.
/// </summary>
public class StarshipShaderController : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Texture2D noiseTexture;
    [SerializeField] private Color baseColor = new Color(0.35f, 0.75f, 1.25f, 1.0f);
    [SerializeField] private float scale = 1.0f;
    [SerializeField] private float speed = 1.0f;
    [SerializeField] private float brightness = 1.0f;

    private Material starshipMaterial;

    private void OnEnable()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (targetRenderer != null)
        {
            starshipMaterial = targetRenderer.material;
            UpdateMaterial();
        }
        else
        {
            Debug.LogError("StarshipShaderController: No Renderer found on this GameObject or assigned in inspector!", this);
        }
    }

    /// <summary>
    /// Update all material properties.
    /// </summary>
    private void UpdateMaterial()
    {
        if (starshipMaterial == null)
        {
            return;
        }

        if (noiseTexture != null)
        {
            starshipMaterial.SetTexture("_MainTex", noiseTexture);
        }

        starshipMaterial.SetColor("_BaseColor", baseColor);
        starshipMaterial.SetFloat("_Scale", scale);
        starshipMaterial.SetFloat("_Speed", speed);
        starshipMaterial.SetFloat("_Brightness", brightness);
    }

    /// <summary>
    /// Set the noise texture for the effect.
    /// </summary>
    public void SetNoiseTexture(Texture2D texture)
    {
        noiseTexture = texture;
        if (starshipMaterial != null)
        {
            starshipMaterial.SetTexture("_MainTex", texture);
        }
    }

    /// <summary>
    /// Set the scale parameter.
    /// </summary>
    public void SetScale(float newScale)
    {
        scale = newScale;
        if (starshipMaterial != null)
        {
            starshipMaterial.SetFloat("_Scale", scale);
        }
    }

    /// <summary>
    /// Set the speed parameter.
    /// </summary>
    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
        if (starshipMaterial != null)
        {
            starshipMaterial.SetFloat("_Speed", speed);
        }
    }

    /// <summary>
    /// Set the brightness parameter.
    /// </summary>
    public void SetBrightness(float newBrightness)
    {
        brightness = newBrightness;
        if (starshipMaterial != null)
        {
            starshipMaterial.SetFloat("_Brightness", brightness);
        }
    }

    /// <summary>
    /// Set the base color of the starship effect.
    /// </summary>
    public void SetBaseColor(Color newColor)
    {
        baseColor = newColor;
        if (starshipMaterial != null)
        {
            starshipMaterial.SetColor("_BaseColor", baseColor);
        }
    }

    private void OnValidate()
    {
        // Update material in editor when values change
        if (Application.isPlaying && starshipMaterial != null)
        {
            UpdateMaterial();
        }
    }
}
