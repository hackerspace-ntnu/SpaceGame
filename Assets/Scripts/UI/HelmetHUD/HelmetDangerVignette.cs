using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns 4 holographic red side panels (left/right/top/bottom) parented under
/// the helmet HUD canvas. Pulses red intensity in response to:
///   - HealthComponent.OnDamage events (full-screen flash that fades)
///   - Sustained "threat near" status pushed by HelmetHUDController (low-health
///     and direction-aware pulses)
///
/// All four panels can be addressed individually. Pulse phase is held in shader
/// uniform _Pulse and modulated here.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HelmetDangerVignette : MonoBehaviour
{
    public enum Side { Left = 0, Right = 1, Top = 2, Bottom = 3 }

    [Header("Look")]
    [SerializeField] private Color dangerColor = new Color(1f, 0.18f, 0.18f, 1f);
    [Range(0.05f, 0.6f)] [SerializeField] private float vignetteWidth = 0.32f;
    [Range(0.5f, 6f)] [SerializeField] private float falloff = 2.4f;
    [Range(0f, 6f)] [SerializeField] private float baseIntensity = 2.0f;

    [Header("Pulse")]
    [Tooltip("Pulse Hz when sustained-danger drives the panels.")]
    [SerializeField] private float pulseHz = 2.6f;
    [Tooltip("Min pulse value during sustained-danger.")]
    [Range(0f, 1f)] [SerializeField] private float pulseMin = 0.15f;

    [Header("Damage Flash")]
    [Tooltip("Peak intensity when a damage event hits. 1 = full bright pulse.")]
    [Range(0f, 1f)] [SerializeField] private float flashPeak = 1f;
    [Tooltip("Seconds for a damage flash to fade out.")]
    [SerializeField] private float flashFadeSeconds = 0.45f;

    private Material material;
    private RectTransform[] panels = new RectTransform[4];
    private Image[] images = new Image[4];

    // Per-panel runtime state
    private float[] flashLevel = new float[4];   // 0..1, decays each frame
    private float[] sustained = new float[4];    // 0..1, sustained danger level

    public bool IsAnyActive
    {
        get
        {
            for (int i = 0; i < 4; i++)
                if (flashLevel[i] > 0.01f || sustained[i] > 0.01f) return true;
            return false;
        }
    }

    private void Awake()
    {
        BuildPanels();
    }

    private void BuildPanels()
    {
        var shader = Shader.Find("UI/HelmetHUDDangerVignette");
        if (shader == null)
        {
            Debug.LogError("[HelmetDangerVignette] Missing shader UI/HelmetHUDDangerVignette");
            return;
        }
        material = new Material(shader) { hideFlags = HideFlags.DontSave };

        for (int i = 0; i < 4; i++)
            panels[i] = CreatePanel((Side)i);
    }

    private RectTransform CreatePanel(Side side)
    {
        var go = new GameObject($"DangerPanel_{side}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);

        // Stretch full screen — the shader masks the band based on _Side.
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        // Each panel needs its own material instance so shader sides differ.
        var perPanelMat = new Material(material) { hideFlags = HideFlags.DontSave };
        perPanelMat.SetFloat("_Side", (float)side);
        perPanelMat.SetColor("_Color", dangerColor);
        perPanelMat.SetFloat("_Width", vignetteWidth);
        perPanelMat.SetFloat("_Falloff", falloff);
        perPanelMat.SetFloat("_Intensity", baseIntensity);
        perPanelMat.SetFloat("_Pulse", 0f);
        img.material = perPanelMat;
        img.color = Color.white;
        img.raycastTarget = false;

        images[(int)side] = img;
        return rt;
    }

    /// <summary>Trigger a flash on a specific side (or all sides if Side.None mapping by passing -1).</summary>
    public void TriggerFlash(Side side, float strength = 1f)
    {
        flashLevel[(int)side] = Mathf.Max(flashLevel[(int)side], Mathf.Clamp01(strength));
    }

    /// <summary>Flash all four sides — used for omni-damage events with no clear direction.</summary>
    public void TriggerFlashAll(float strength = 1f)
    {
        for (int i = 0; i < 4; i++)
            flashLevel[i] = Mathf.Max(flashLevel[i], Mathf.Clamp01(strength));
    }

    /// <summary>
    /// Sets sustained danger 0..1 on a side (e.g. low health, threat to that side).
    /// 0 disables, 1 is maximum sustained brightness with active pulsing.
    /// </summary>
    public void SetSustained(Side side, float value01)
    {
        sustained[(int)side] = Mathf.Clamp01(value01);
    }

    public void ClearSustained()
    {
        for (int i = 0; i < 4; i++) sustained[i] = 0f;
    }

    private void Update()
    {
        if (images == null) return;

        float pulseWave = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f);
        // Map pulseWave [0..1] into [pulseMin..1] so it never fully blacks out
        float sustainedPulse = Mathf.Lerp(pulseMin, 1f, pulseWave);

        for (int i = 0; i < 4; i++)
        {
            // Decay flash
            if (flashLevel[i] > 0f)
            {
                flashLevel[i] -= Time.deltaTime / Mathf.Max(0.01f, flashFadeSeconds);
                if (flashLevel[i] < 0f) flashLevel[i] = 0f;
            }

            // Combine flash (instant peak, decays) and sustained (pulses)
            float flashPart = flashLevel[i] * flashPeak;
            float sustainedPart = sustained[i] * sustainedPulse;
            float pulseValue = Mathf.Max(flashPart, sustainedPart);

            var img = images[i];
            if (img == null || img.material == null) continue;

            img.material.SetFloat("_Pulse", pulseValue);
            img.material.SetColor("_Color", dangerColor);
            img.material.SetFloat("_Intensity", baseIntensity);
            img.material.SetFloat("_Width", vignetteWidth);
            img.material.SetFloat("_Falloff", falloff);
        }
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        for (int i = 0; i < 4; i++)
            if (images[i] != null && images[i].material != null) Destroy(images[i].material);
    }
}
