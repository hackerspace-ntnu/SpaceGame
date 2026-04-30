using UnityEngine;

/// <summary>
/// Top-level controller for the helmet AR HUD overlay.
///
/// Drop this on a child of the PlayerHUD canvas (or any UI Canvas). It will:
///   - Spawn a HelmetDangerVignette child (curved warning line on each side).
///   - Spawn a HelmetNavMarkers child (AR markers around the screen).
///   - Subscribe to the assigned HealthComponent's OnDamage event and grow
///     both warning lines whenever the player takes damage.
///
/// Marker sources (zero scene setup needed):
///   - Every entity with an EntityFaction component shows up automatically,
///     colored by its relationship to the player faction.
///   - Every MapPOI / MapService static marker shows up too.
///
/// References auto-resolve when not set:
///   - playerHealth -> first HealthComponent under Player-tagged GameObject
///   - referenceCamera -> Camera.main
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HelmetHUDController : MonoBehaviour
{
    [Header("References (auto-resolve at runtime if null)")]
    [SerializeField] private HealthComponent playerHealth;
    [SerializeField] private Camera referenceCamera;

    [Header("Damage Response")]
    [Tooltip("Damage amount that maps to a full-strength hit. Smaller hits scale down linearly.")]
    [SerializeField] private int damageForFullFlash = 25;

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
        dangerVignette.HitBoth(strength);
    }

    private void Update()
    {
        if (referenceCamera == null) referenceCamera = Camera.main;

        // Nav markers still need their per-frame projection update.
        if (navMarkers != null)
            navMarkers.Tick(out _, out _);
    }
}
