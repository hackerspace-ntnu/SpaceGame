using UnityEngine;

public class RocketBoosterShaderController : MonoBehaviour
{
    [SerializeField] private Texture2D noiseTexture;
    [SerializeField] private Color flameColor = new Color(1.0f, 0.5f, 0.2f, 1.0f);
    [SerializeField] private float scale = 1.0f;
    [SerializeField] private float speed = 1.0f;
    [SerializeField] private float brightness = 1.0f;

    private Material material;
    private Renderer meshRenderer;

    private void OnEnable()
    {
        InitializeMaterial();
        UpdateMaterial();
    }

    private void InitializeMaterial()
    {
        meshRenderer = GetComponent<Renderer>();
        if (meshRenderer == null)
        {
            Debug.LogError("RocketBoosterShaderController requires a Renderer component!");
            return;
        }

        material = meshRenderer.material;
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            UpdateMaterial();
        }
    }

    private void UpdateMaterial()
    {
        if (material == null)
        {
            InitializeMaterial();
        }

        if (material == null) return;

        if (noiseTexture != null)
        {
            material.SetTexture("_MainTex", noiseTexture);
        }

        material.SetColor("_FlameColor", flameColor);
        material.SetFloat("_Scale", scale);
        material.SetFloat("_Speed", speed);
        material.SetFloat("_Brightness", brightness);
    }

    /// <summary>
    /// Set the noise texture for the flame effect
    /// </summary>
    public void SetNoiseTexture(Texture2D texture)
    {
        noiseTexture = texture;
        if (material != null)
        {
            material.SetTexture("_MainTex", texture);
        }
    }

    /// <summary>
    /// Set the flame color
    /// </summary>
    public void SetFlameColor(Color newColor)
    {
        flameColor = newColor;
        if (material != null)
        {
            material.SetColor("_FlameColor", newColor);
        }
    }

    /// <summary>
    /// Set the scale of the effect
    /// </summary>
    public void SetScale(float newScale)
    {
        scale = newScale;
        if (material != null)
        {
            material.SetFloat("_Scale", newScale);
        }
    }

    /// <summary>
    /// Set the animation speed
    /// </summary>
    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
        if (material != null)
        {
            material.SetFloat("_Speed", newSpeed);
        }
    }

    /// <summary>
    /// Set the brightness of the flame
    /// </summary>
    public void SetBrightness(float newBrightness)
    {
        brightness = newBrightness;
        if (material != null)
        {
            material.SetFloat("_Brightness", newBrightness);
        }
    }
}
