using UnityEngine;

/// <summary>
/// Top-level controller for the helmet AR HUD overlay.
///
/// Drop this on a child of the PlayerHUD canvas (or any UI Canvas). It will:
///   - Spawn a HelmetDangerVignette child (4 red side-blink panels).
///   - Spawn a HelmetNavMarkers child (directional AR markers around the screen).
///   - Subscribe to the assigned HealthComponent's OnDamage event and trigger
///     directional damage flashes against the vignette.
///   - Each frame, ask the nav markers to compute the dominant threat level and
///     direction, and feed that to the vignette as a sustained pulse.
///
/// Marker sources (zero scene setup needed):
///   - Every entity with an EntityFaction component shows up automatically,
///     colored by its relationship to the player faction.
///   - Every MapPOI / MapService static marker shows up too.
///
/// References auto-resolve when not set:
///   - playerHealth -> first HealthComponent under Player tagged GameObject
///   - referenceCamera -> Camera.main
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HelmetHUDController : MonoBehaviour
{
    [Header("References (auto-resolve at runtime if null)")]
    [SerializeField] private HealthComponent playerHealth;
    [SerializeField] private Camera referenceCamera;

    [Header("Damage Response")]
    [Tooltip("Damage amount that maps to a full-strength flash. Smaller hits scale down linearly.")]
    [SerializeField] private int damageForFullFlash = 25;

    [Header("Sustained Threat Response")]
    [Tooltip("Health % below which the front vignette pulses red on its own.")]
    [Range(0f, 1f)] [SerializeField] private float lowHealthThreshold = 0.30f;
    [Tooltip("How loud (0..1) the low-health pulse gets at zero health.")]
    [Range(0f, 1f)] [SerializeField] private float lowHealthMaxPulse = 0.85f;

    [Header("Subsystems")]
    [SerializeField] private HelmetDangerVignette dangerVignette;
    [SerializeField] private HelmetNavMarkers navMarkers;

    private Canvas hudCanvas;

    private void Awake()
    {
        EnsureCanvas();
        ResolveReferences();
        EnsureSubsystems();
    }

    private void OnEnable()
    {
        SubscribeHealth();
    }

    private void OnDisable()
    {
        UnsubscribeHealth();
    }

    private void EnsureCanvas()
    {
        hudCanvas = GetComponentInParent<Canvas>();
        if (hudCanvas == null)
        {
            Debug.LogWarning("[HelmetHUDController] No parent Canvas found. Place this component under a UI Canvas (e.g. PlayerHUD).", this);
        }
    }

    private void ResolveReferences()
    {
        if (playerHealth == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerHealth = p.GetComponentInChildren<HealthComponent>();
        }
        if (referenceCamera == null) referenceCamera = Camera.main;
    }

    private void EnsureSubsystems()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        if (dangerVignette == null)
        {
            var go = new GameObject("DangerVignette", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var dvRt = (RectTransform)go.transform;
            dvRt.anchorMin = Vector2.zero;
            dvRt.anchorMax = Vector2.one;
            dvRt.offsetMin = Vector2.zero;
            dvRt.offsetMax = Vector2.zero;
            dangerVignette = go.AddComponent<HelmetDangerVignette>();
        }

        if (navMarkers == null)
        {
            var go = new GameObject("NavMarkers", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var nmRt = (RectTransform)go.transform;
            nmRt.anchorMin = Vector2.zero;
            nmRt.anchorMax = Vector2.one;
            nmRt.offsetMin = Vector2.zero;
            nmRt.offsetMax = Vector2.zero;
            navMarkers = go.AddComponent<HelmetNavMarkers>();
        }
    }

    private void SubscribeHealth()
    {
        if (playerHealth == null) return;
        playerHealth.OnDamage += HandleDamage;
    }

    private void UnsubscribeHealth()
    {
        if (playerHealth == null) return;
        playerHealth.OnDamage -= HandleDamage;
    }

    private void HandleDamage(int amount)
    {
        if (dangerVignette == null) return;

        float strength = Mathf.Clamp01((float)amount / Mathf.Max(1, damageForFullFlash));

        // Direction-aware: prefer LastDamageSource if present.
        var src = playerHealth != null ? playerHealth.LastDamageSource : null;
        var cam = referenceCamera != null ? referenceCamera : Camera.main;
        if (src != null && cam != null)
        {
            Vector3 toSrc = (src.position - cam.transform.position).normalized;
            Vector3 local = cam.transform.InverseTransformDirection(toSrc);
            FlashLocalDir(local, strength);
        }
        else
        {
            dangerVignette.TriggerFlashAll(strength);
        }
    }

    private void FlashLocalDir(Vector3 localDir, float strength)
    {
        // localDir.x = right, localDir.y = up, localDir.z = forward
        // Behind us → flash all sides for surround-warning.
        if (localDir.z < -0.2f)
        {
            dangerVignette.TriggerFlashAll(strength * 0.85f);
            return;
        }

        float ax = Mathf.Abs(localDir.x);
        float ay = Mathf.Abs(localDir.y);
        if (ax >= ay)
        {
            if (localDir.x >= 0f) dangerVignette.TriggerFlash(HelmetDangerVignette.Side.Right, strength);
            else                  dangerVignette.TriggerFlash(HelmetDangerVignette.Side.Left, strength);
        }
        else
        {
            if (localDir.y >= 0f) dangerVignette.TriggerFlash(HelmetDangerVignette.Side.Top, strength);
            else                  dangerVignette.TriggerFlash(HelmetDangerVignette.Side.Bottom, strength);
        }
    }

    private void Update()
    {
        // Keep the camera fresh in case Main was switched at runtime.
        if (referenceCamera == null) referenceCamera = Camera.main;

        // Update markers and harvest threat level/direction
        float threat = 0f;
        Vector3 threatLocalDir = Vector3.zero;
        if (navMarkers != null)
            navMarkers.Tick(out threat, out threatLocalDir);

        if (dangerVignette == null) return;

        // Reset sustained then re-apply this frame's contributors.
        dangerVignette.ClearSustained();

        // Threat-based sustained pulse on the side closest to the threat
        if (threat > 0.05f)
            ApplySustainedFromLocalDir(threatLocalDir, threat);

        // Low-health: pulse the front (top edge as a "warning headline")
        if (playerHealth != null && playerHealth.GetMaxHealth > 0)
        {
            float pct = (float)playerHealth.GetHealth / playerHealth.GetMaxHealth;
            if (pct <= lowHealthThreshold)
            {
                float t = 1f - (pct / Mathf.Max(0.001f, lowHealthThreshold));
                float v = Mathf.Lerp(0.2f, lowHealthMaxPulse, t);
                dangerVignette.SetSustained(HelmetDangerVignette.Side.Top,
                    Mathf.Max(v, 0f));
                // Also gently echo on bottom for that wraparound feel
                dangerVignette.SetSustained(HelmetDangerVignette.Side.Bottom,
                    Mathf.Max(v * 0.6f, 0f));
            }
        }
    }

    private void ApplySustainedFromLocalDir(Vector3 localDir, float intensity)
    {
        if (localDir.z < -0.2f)
        {
            // Threat behind: split between left+right for "surround"
            dangerVignette.SetSustained(HelmetDangerVignette.Side.Left, intensity * 0.7f);
            dangerVignette.SetSustained(HelmetDangerVignette.Side.Right, intensity * 0.7f);
            return;
        }
        float ax = Mathf.Abs(localDir.x);
        float ay = Mathf.Abs(localDir.y);
        if (ax >= ay)
        {
            var side = localDir.x >= 0f ? HelmetDangerVignette.Side.Right : HelmetDangerVignette.Side.Left;
            dangerVignette.SetSustained(side, intensity);
        }
        else
        {
            var side = localDir.y >= 0f ? HelmetDangerVignette.Side.Top : HelmetDangerVignette.Side.Bottom;
            dangerVignette.SetSustained(side, intensity);
        }
    }

    /// <summary>
    /// Manual helper: trigger a directional damage flash without a real damage
    /// event. Useful for cinematics, environmental warnings, or testing.
    /// </summary>
    public void TriggerDirectionalAlert(Vector3 worldPosOfThreat, float strength = 1f)
    {
        var cam = referenceCamera != null ? referenceCamera : Camera.main;
        if (cam == null || dangerVignette == null) return;
        Vector3 dir = (worldPosOfThreat - cam.transform.position).normalized;
        FlashLocalDir(cam.transform.InverseTransformDirection(dir), strength);
    }
}
