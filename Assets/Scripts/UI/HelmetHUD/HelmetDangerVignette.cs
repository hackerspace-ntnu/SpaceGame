using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Two thin curved warning lines (left + right) that appear when the player
/// takes damage. Each hit grows the affected side's arc length, then both
/// length and opacity decay over time when no further damage arrives.
///
/// Driven entirely by HitSide()/HitBoth(): no sustained, threat, or low-health
/// behavior — keep it simple.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HelmetDangerVignette : MonoBehaviour
{
    public enum Side { Left = 0, Right = 1 }
    private const int SideCount = 2;

    [Header("Look")]
    [SerializeField] private Color dangerColor = new Color(0.788f, 0.247f, 0.247f, 1f);
    [Range(0f, 4f)] [SerializeField] private float intensity = 1.4f;

    [Header("Arc shape")]
    [Tooltip("How far outside the screen the arc center sits. Larger = gentler curve.")]
    [Range(0.5f, 4f)] [SerializeField] private float arcCenterOffset = 1.27f;
    [Tooltip("Radius of the arc line from its (off-screen) center.")]
    [Range(0.5f, 4f)] [SerializeField] private float arcRadius = 2.27f;
    [Tooltip("Thickness of the arc line, in UV units.")]
    [Range(0.001f, 0.05f)] [SerializeField] private float arcThickness = 0.012f;
    [Tooltip("Soft fade at each end of the arc.")]
    [Range(0f, 0.3f)] [SerializeField] private float spanFeather = 0.249f;

    [Header("Damage response")]
    [Tooltip("Arc half-span (0..0.5) added per full-strength hit.")]
    [Range(0f, 0.5f)] [SerializeField] private float spanPerHit = 0.198f;
    [Tooltip("Maximum arc half-span.")]
    [Range(0f, 0.5f)] [SerializeField] private float maxSpan = 0.323f;
    [Tooltip("Extra span added on hit that snaps back instantly — gives the line a punchy 'overshoot' on impact.")]
    [Range(0f, 0.2f)] [SerializeField] private float spanOvershoot = 0.06f;
    [Tooltip("Time (s) for the overshoot to settle back to the baseline span.")]
    [SerializeField] private float overshootSettle = 0.18f;
    [Tooltip("Time (s) the impact spike (extra brightness + thickening) lasts.")]
    [SerializeField] private float spikeDecay = 0.22f;

    [Header("Holographic motion")]
    [Tooltip("Soft outer glow halo around the line.")]
    [Range(0f, 0.2f)] [SerializeField] private float haloThickness = 0.05f;
    [Range(0f, 1.5f)] [SerializeField] private float haloStrength = 0.55f;
    [Tooltip("How fast the bright shimmer bead scrolls along the line.")]
    [Range(-3f, 3f)] [SerializeField] private float shimmerSpeed = 0.7f;
    [Tooltip("Width of the shimmer bead (UV units).")]
    [Range(0.02f, 0.6f)] [SerializeField] private float shimmerWidth = 0.18f;
    [Tooltip("Brightness of the shimmer bead.")]
    [Range(0f, 2f)] [SerializeField] private float shimmerStrength = 0.85f;
    [Tooltip("Fast random brightness jitter for the holographic flicker.")]
    [Range(0f, 1f)] [SerializeField] private float flickerStrength = 0.18f;
    [Range(0f, 60f)] [SerializeField] private float flickerSpeed = 22f;
    [Tooltip("How much the line thickens during the impact spike.")]
    [Range(0f, 4f)] [SerializeField] private float spikeThicken = 1.6f;
    [Tooltip("How much the line brightens during the impact spike.")]
    [Range(0f, 4f)] [SerializeField] private float spikeBrighten = 1.8f;

    [Header("Decay")]
    [Tooltip("Seconds with no damage before the arc starts to fade.")]
    [SerializeField] private float fadeDelay = 0.6f;
    [Tooltip("How long, after the delay, the arc fades from full to invisible (alpha).")]
    [SerializeField] private float fadeDuration = 4f;
    [Tooltip("How long, after the delay, the arc shrinks back to zero span.")]
    [SerializeField] private float shrinkDuration = 2f;

    private Material material;
    private readonly Image[] images = new Image[SideCount];
    private readonly float[] currentSpan = new float[SideCount];
    private readonly float[] currentAlpha = new float[SideCount];
    private readonly float[] lastHitTime = new float[SideCount];
    // Animation channels driven instantaneously on hit, decayed in Update.
    private readonly float[] currentSpike = new float[SideCount];     // 0..1, fast decay, drives _Spike
    private readonly float[] currentOvershoot = new float[SideCount]; // extra span, fast decay

    private void Awake()
    {
        BuildPanels();
    }

    private void BuildPanels()
    {
        var shader = Shader.Find("UI/HelmetHUDDangerVignette");
        if (shader == null)
        {
            Debug.LogWarning("[HelmetDangerVignette] Shader 'UI/HelmetHUDDangerVignette' not found. Falling back to UI/Default.");
            shader = Shader.Find("UI/Default");
            if (shader == null) return;
        }
        material = new Material(shader) { hideFlags = HideFlags.DontSave };

        for (int i = 0; i < SideCount; i++)
            CreatePanel((Side)i);

        // Initialize state to "invisible" and far in the past.
        for (int i = 0; i < SideCount; i++)
        {
            currentSpan[i] = 0f;
            currentAlpha[i] = 0f;
            lastHitTime[i] = -999f;
        }
    }

    private void CreatePanel(Side side)
    {
        var go = new GameObject($"DangerArc_{side}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        var perPanelMat = new Material(material) { hideFlags = HideFlags.DontSave };
        perPanelMat.SetFloat("_Side", (float)side);
        perPanelMat.SetColor("_Color", dangerColor);
        ApplyShapeUniforms(perPanelMat);
        perPanelMat.SetFloat("_Span", 0f);
        perPanelMat.SetFloat("_Pulse", 0f);
        img.material = perPanelMat;
        img.color = Color.white;
        img.raycastTarget = false;

        images[(int)side] = img;
    }

    /// <summary>
    /// Register a hit on one side. Grows that side's arc and resets its fade timer.
    /// strength 0..1 scales how much span is added (1 = spanPerHit).
    /// </summary>
    public void HitSide(Side side, float strength = 1f)
    {
        int i = (int)side;
        float s = Mathf.Clamp01(strength);
        currentSpan[i] = Mathf.Min(maxSpan, currentSpan[i] + spanPerHit * s);
        currentAlpha[i] = 1f;
        currentSpike[i] = Mathf.Max(currentSpike[i], s);
        currentOvershoot[i] = Mathf.Max(currentOvershoot[i], spanOvershoot * s);
        lastHitTime[i] = Time.time;
    }

    /// <summary>Hit both sides — for omni-damage with no clear direction.</summary>
    public void HitBoth(float strength = 1f)
    {
        HitSide(Side.Left, strength);
        HitSide(Side.Right, strength);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < SideCount; i++)
        {
            float since = Time.time - lastHitTime[i];

            if (since > fadeDelay)
            {
                float t = since - fadeDelay;
                // Fade alpha (linear from 1 to 0 over fadeDuration).
                float alphaT = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeDuration));
                currentAlpha[i] = Mathf.Max(0f, 1f - alphaT);
                // Shrink span (linear, clamped at zero).
                currentSpan[i] = Mathf.Max(0f,
                    currentSpan[i] - (maxSpan * dt / Mathf.Max(0.01f, shrinkDuration)));
            }

            // Spike: exponential decay over spikeDecay seconds (~63% gone per spikeDecay).
            currentSpike[i] = Mathf.Max(0f,
                currentSpike[i] - currentSpike[i] * dt / Mathf.Max(0.01f, spikeDecay));

            // Overshoot: settles to zero exponentially over overshootSettle.
            currentOvershoot[i] = Mathf.Max(0f,
                currentOvershoot[i] - currentOvershoot[i] * dt / Mathf.Max(0.01f, overshootSettle));

            var img = images[i];
            if (img == null || img.material == null) continue;
            img.material.SetColor("_Color", dangerColor);
            ApplyShapeUniforms(img.material);
            float renderedSpan = Mathf.Min(maxSpan + spanOvershoot, currentSpan[i] + currentOvershoot[i]);
            img.material.SetFloat("_Span", renderedSpan);
            img.material.SetFloat("_Pulse", currentAlpha[i]);
            img.material.SetFloat("_Spike", currentSpike[i]);
        }
    }

    private void ApplyShapeUniforms(Material m)
    {
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_ArcCenterOffset", arcCenterOffset);
        m.SetFloat("_ArcRadius", arcRadius);
        m.SetFloat("_ArcThickness", arcThickness);
        m.SetFloat("_SpanFeather", spanFeather);
        m.SetFloat("_HaloThickness", haloThickness);
        m.SetFloat("_HaloStrength", haloStrength);
        m.SetFloat("_ShimmerSpeed", shimmerSpeed);
        m.SetFloat("_ShimmerWidth", shimmerWidth);
        m.SetFloat("_ShimmerStrength", shimmerStrength);
        m.SetFloat("_FlickerStrength", flickerStrength);
        m.SetFloat("_FlickerSpeed", flickerSpeed);
        m.SetFloat("_SpikeThicken", spikeThicken);
        m.SetFloat("_SpikeBrighten", spikeBrighten);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        for (int i = 0; i < SideCount; i++)
            if (images[i] != null && images[i].material != null) Destroy(images[i].material);
    }
}
