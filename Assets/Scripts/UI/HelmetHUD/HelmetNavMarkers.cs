using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Draws holographic AR markers around the helmet HUD pointing at world targets.
///
/// Two display modes per marker:
///   - On-screen (target is in front and within frustum): marker is drawn at the
///     projected screen position with a ring + label + distance.
///   - Off-screen (target is behind or outside the frustum): marker is clamped
///     to the screen rectangle's edge and shows a triangular arrow rotated to
///     point inward toward the target's true direction.
///
/// Sources of markers:
///   - EntityTargetRegistry: every entity with an EntityFaction is automatically
///     visible. Color is derived from its relationship to the local player
///     faction (Hostile/Allied/Neutral) — no MapMarker component required.
///   - MapService: POIs (static markers via MapPOI). Entities that *also* have
///     a MapMarker registered with MapService are skipped here so we never
///     double-render. The faction registry wins for anything with a faction.
///
/// Threats (player-hostile factions in range) are reported back to the danger
/// vignette through the parent HUD controller.
/// </summary>
public class HelmetNavMarkers : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Camera used for world->screen projection. If null, Camera.main is used at runtime.")]
    [SerializeField] private Camera referenceCamera;
    [Tooltip("Player's EntityFaction. If null, auto-resolves via Player tag at runtime. Used to color enemies/allies and to compute threat levels.")]
    [SerializeField] private EntityFaction playerFaction;

    [Header("Layout")]
    [Tooltip("Padding (px) inside screen edges where off-screen markers clamp.")]
    [SerializeField] private float edgePadding = 64f;
    [Tooltip("Marker icon size in px.")]
    [SerializeField] private float markerSize = 56f;
    [Tooltip("Hide markers closer than this (m).")]
    [SerializeField] private float minDistance = 1.5f;
    [Tooltip("Hide markers farther than this (m). 0 = no max.")]
    [SerializeField] private float maxDistance = 0f;

    [Header("Look")]
    [SerializeField] private Color neutralColor = new Color(0.85f, 0.95f, 1f, 1f);
    [SerializeField] private Color hostileColor = new Color(1f, 0.30f, 0.30f, 1f);
    [SerializeField] private Color friendlyColor = new Color(0.30f, 1f, 0.50f, 1f);
    [SerializeField] private Color questColor = new Color(1f, 0.85f, 0.20f, 1f);
    [SerializeField] private Color discoveryColor = new Color(0.85f, 0.55f, 1f, 1f);

    [Tooltip("Marker alpha falls from 1 at this distance to 0.4 at maxDistance.")]
    [SerializeField] private float fadeStartDistance = 80f;

    [Header("Arrow look (off-screen)")]
    [Tooltip("Color used for all off-screen directional arrows, regardless of faction. Matches the map hologram tint.")]
    [SerializeField] private Color arrowColor = new Color(0.45f, 0.95f, 1.0f, 0.45f);
    [Tooltip("Multiplier on markerSize when rendering an off-screen arrow.")]
    [SerializeField] private float arrowSizeMultiplier = 0.55f;
    [Tooltip("Hostile off-screen arrows pulse this much (0 = no pulse, 0.4 = strong).")]
    [SerializeField, Range(0f, 0.6f)] private float hostilePulseAmount = 0.25f;
    [Tooltip("Pulse speed in Hz for hostile off-screen arrows.")]
    [SerializeField] private float hostilePulseHz = 2.4f;
    [Tooltip("Soft Perlin breathing scale (0.04 = ±4%). Matches the slow pulse of the map hologram.")]
    [SerializeField, Range(0f, 0.2f)] private float arrowScaleWobble = 0.04f;
    [Tooltip("Speed of the scale wobble breathing.")]
    [SerializeField, Range(0.1f, 4f)] private float arrowScaleWobbleHz = 1.2f;

    [Header("Threat detection (forwards info to vignette)")]
    [Tooltip("Distance at which a hostile entity registers full threat (1.0).")]
    [SerializeField] private float threatRange = 35f;
    [Tooltip("Distance at which a hostile entity is the faintest threat (>0).")]
    [SerializeField] private float threatRangeMax = 80f;

    [Header("Debug")]
    [Tooltip("Log marker counts and player-faction resolution status each second.")]
    [SerializeField] private bool debugLog = false;
    [Tooltip("If true, ignore the holographic shader and render with the default UI material — useful to verify visibility.")]
    [SerializeField] private bool debugForceDefaultMaterial = false;
    [Tooltip("If true, render every entity even when player faction can't resolve (will appear Neutral).")]
    [SerializeField] private bool showEvenWithoutPlayerFaction = true;
    [Tooltip("Spawns 4 debug markers at +/- 10m on N/E/S/W around the camera. Use to verify rendering works regardless of factions/POIs in your scene.")]
    [SerializeField] private bool debugSelfTestMarkers = false;
    private float nextDebugLog;
    private readonly List<Transform> selfTestTransforms = new();

    private RectTransform rect;
    private readonly Dictionary<object, MarkerView> views = new();
    // Reused per-frame to track which keys were seen so we can prune stale ones.
    private readonly HashSet<object> seenThisFrame = new();

    public IReadOnlyDictionary<object, MarkerView> Views => views;

    public sealed class MarkerView
    {
        public RectTransform root;
        public Image ring;       // visible when on-screen
        public Image pip;        // inner dot — visible when on-screen
        public Image arrow;      // visible when off-screen
        public TextMeshProUGUI label;
        public TextMeshProUGUI distance;
        public bool onScreen;
        public Color tint;
    }

    private void Awake()
    {
        rect = (RectTransform)transform;
    }

    private void OnEnable()
    {
        ResolvePlayerFaction();
    }

    private void OnDisable()
    {
        foreach (var v in views.Values)
            if (v.root != null) Destroy(v.root.gameObject);
        views.Clear();
        seenThisFrame.Clear();
    }

    private void ResolvePlayerFaction()
    {
        if (playerFaction != null) return;
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerFaction = p.GetComponentInChildren<EntityFaction>();
    }

    private MarkerView EnsureView(object key, string labelText, Color tint)
    {
        if (views.TryGetValue(key, out var existing))
        {
            // Allow live-relabel/recolor (e.g. faction relationship changed).
            if (existing.tint != tint)
            {
                existing.ring.color = tint;
                existing.label.color = tint;
                existing.distance.color = tint;
                existing.tint = tint;
            }
            existing.arrow.color = arrowColor;
            if (existing.label.text != labelText) existing.label.text = labelText;
            return existing;
        }
        var v = HelmetMarkerFactory.Build(transform, markerSize, debugForceDefaultMaterial);
        v.label.text = labelText;
        v.label.color = tint;
        v.distance.color = tint;
        v.ring.color = tint;
        v.arrow.color = arrowColor;
        v.tint = tint;
        views[key] = v;
        return v;
    }

    private Color ColorForPOI(MapMarkerType t) => t switch
    {
        MapMarkerType.Hostile   => hostileColor,
        MapMarkerType.Friendly  => friendlyColor,
        MapMarkerType.Quest     => questColor,
        MapMarkerType.Discovery => discoveryColor,
        _ => neutralColor,
    };

    private Color ColorForRelationship(FactionRelationship rel) => rel switch
    {
        FactionRelationship.Hostile => hostileColor,
        FactionRelationship.Allied  => friendlyColor,
        _                           => neutralColor,
    };

    /// <summary>
    /// Update marker positions. Returns the maximum threat level (0..1) and the
    /// approximate side it is coming from (left/right/front/back) so the vignette
    /// can react. Called by HelmetHUDController each frame.
    /// </summary>
    public void Tick(out float threatLevel, out Vector3 threatLocalDir)
    {
        threatLevel = 0f;
        threatLocalDir = Vector3.zero;

        var cam = referenceCamera != null ? referenceCamera : Camera.main;
        if (cam == null) return;

        if (playerFaction == null) ResolvePlayerFaction();

        // Use our own rect for projection space — we are anchor-stretched to fill
        // the canvas, so rect.size is always the screen size in canvas-pixel units.
        Vector2 size = rect.rect.size;
        if (size.x < 1f || size.y < 1f)
        {
            // Canvas not laid out yet (first frame on Awake-spawned UI). Skip.
            return;
        }
        float halfW = size.x * 0.5f;
        float halfH = size.y * 0.5f;
        float left   = -halfW + edgePadding;
        float right  =  halfW - edgePadding;
        float bottom = -halfH + edgePadding;
        float top    =  halfH - edgePadding;

        Vector3 camPos = cam.transform.position;
        seenThisFrame.Clear();

        if (debugLog && Time.unscaledTime >= nextDebugLog)
        {
            nextDebugLog = Time.unscaledTime + 1f;
            int poiCount = MapService.Instance != null ? MapService.Instance.Markers.Count : 0;
            Debug.Log($"[HelmetNavMarkers] entities={EntityTargetRegistry.All.Count} pois={poiCount} playerFaction={(playerFaction == null ? "NULL" : playerFaction.name)} canvasSize={size}");
        }

        // -------- 0) Self-test markers --------
        if (debugSelfTestMarkers)
        {
            EnsureSelfTestTransforms(camPos);
            for (int i = 0; i < selfTestTransforms.Count; i++)
            {
                var t = selfTestTransforms[i];
                if (t == null) continue;
                object key = t;
                seenThisFrame.Add(key);
                ProcessTarget(key, t.position, $"TEST{i}", new Color(1f, 0.4f, 1f, 1f),
                    cam, camPos, size, left, right, bottom, top,
                    relForThreat: FactionRelationship.Neutral,
                    ref threatLevel, ref threatLocalDir);
            }
        }

        // -------- 1) Faction-driven entities --------
        if (playerFaction == null && !showEvenWithoutPlayerFaction)
        {
            // No player faction yet, and configured to suppress unrelated entities.
        }
        var ents = EntityTargetRegistry.All;
        for (int i = 0; i < ents.Count; i++)
        {
            var ent = ents[i];
            if (ent == null) continue;
            if (playerFaction != null && ent == playerFaction) continue; // skip self
            if (playerFaction == null && !showEvenWithoutPlayerFaction) continue;

            FactionRelationship rel = playerFaction != null
                ? playerFaction.GetRelationshipWith(ent)
                : FactionRelationship.Neutral;

            string label = ent.Faction != null && !string.IsNullOrEmpty(ent.Faction.factionName)
                ? ent.Faction.factionName
                : ent.name;

            object key = ent;
            seenThisFrame.Add(key);
            ProcessTarget(key, ent.transform.position, label, ColorForRelationship(rel),
                cam, camPos, size, left, right, bottom, top,
                relForThreat: rel,
                ref threatLevel, ref threatLocalDir);
        }

        // -------- 2) MapService POIs (skip ones that have an EntityFaction) --------
        if (MapService.Instance != null)
        {
            var markers = MapService.Instance.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                // If this marker is following a transform that already has an
                // EntityFaction, the faction loop above already drew it.
                if (m.follow != null && m.follow.GetComponentInParent<EntityFaction>() != null)
                    continue;

                bool eligible = !m.requiresRevealedChunk || m.discovered ||
                    (MapService.Instance.Config != null && MapService.Instance.IsChunkRevealed(
                        MapService.Instance.Config.WorldToChunkCoord(m.GetWorldPosition())));
                if (!eligible) continue;

                string label = !string.IsNullOrEmpty(m.label) ? m.label : m.type.ToString().ToUpperInvariant();
                object key = m;
                seenThisFrame.Add(key);
                ProcessTarget(key, m.GetWorldPosition(), label, ColorForPOI(m.type),
                    cam, camPos, size, left, right, bottom, top,
                    relForThreat: m.type == MapMarkerType.Hostile ? FactionRelationship.Hostile : FactionRelationship.Neutral,
                    ref threatLevel, ref threatLocalDir);
            }
        }

        // -------- 3) Reap stale views --------
        if (views.Count > seenThisFrame.Count)
        {
            // Build a small temp list of keys to remove (avoid mutating during iteration).
            List<object> toRemove = null;
            foreach (var kvp in views)
            {
                if (!seenThisFrame.Contains(kvp.Key))
                {
                    toRemove ??= new List<object>();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    if (views.TryGetValue(toRemove[i], out var v))
                    {
                        if (v.root != null) Destroy(v.root.gameObject);
                        views.Remove(toRemove[i]);
                    }
                }
            }
        }

        threatLevel = Mathf.Clamp01(threatLevel);
    }

    private void ProcessTarget(object key, Vector3 worldPos, string label, Color tint,
        Camera cam, Vector3 camPos, Vector2 size,
        float left, float right, float bottom, float top,
        FactionRelationship relForThreat,
        ref float threatLevel, ref Vector3 threatLocalDir)
    {
        float dist = Vector3.Distance(camPos, worldPos);
        bool tooNear = dist < minDistance;
        bool tooFar = maxDistance > 0f && dist > maxDistance;

        var view = EnsureView(key, label, tint);

        if (tooNear || tooFar)
        {
            view.root.gameObject.SetActive(false);
            return;
        }
        view.root.gameObject.SetActive(true);

        // Threat aggregation
        if (relForThreat == FactionRelationship.Hostile)
        {
            float t = Mathf.InverseLerp(threatRangeMax, threatRange, dist);
            if (t > threatLevel)
            {
                threatLevel = t;
                threatLocalDir = cam.transform.InverseTransformDirection((worldPos - camPos).normalized);
            }
        }

        Vector3 viewport = cam.WorldToViewportPoint(worldPos);
        bool inFront = viewport.z > 0f;
        bool insideRect = inFront && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;

        Vector2 anchored;
        float arrowAngle = 0f;
        bool onScreen;

        if (insideRect)
        {
            onScreen = true;
            anchored = new Vector2((viewport.x - 0.5f) * size.x, (viewport.y - 0.5f) * size.y);
            anchored.x = Mathf.Clamp(anchored.x, left, right);
            anchored.y = Mathf.Clamp(anchored.y, bottom, top);
        }
        else
        {
            onScreen = false;
            Vector2 dir;
            if (!inFront)
            {
                Vector2 v = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
                if (v.sqrMagnitude < 0.0001f) v = Vector2.down;
                dir = -v.normalized;
            }
            else
            {
                dir = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
                dir.Normalize();
            }
            float tx = dir.x > 0 ? right / dir.x : (dir.x < 0 ? left / dir.x : float.PositiveInfinity);
            float ty = dir.y > 0 ? top / dir.y : (dir.y < 0 ? bottom / dir.y : float.PositiveInfinity);
            float tEdge = Mathf.Min(Mathf.Abs(tx), Mathf.Abs(ty));
            anchored = dir * tEdge;
            arrowAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        }

        view.root.anchoredPosition = anchored;
        view.onScreen = onScreen;
        view.ring.gameObject.SetActive(false);
        if (view.pip != null) view.pip.gameObject.SetActive(false);
        view.arrow.gameObject.SetActive(!onScreen);
        view.label.gameObject.SetActive(false);
        view.distance.gameObject.SetActive(false);

        if (!onScreen)
        {
            view.arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, arrowAngle);

            // Optional hostile pulse on top of the breathing wobble.
            float pulse = 1f;
            if (relForThreat == FactionRelationship.Hostile && hostilePulseAmount > 0f)
            {
                float tp = Mathf.Sin(Time.unscaledTime * hostilePulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
                pulse = 1f + hostilePulseAmount * tp;
            }

            // Slow shared breathing scale (matches map hologram cadence).
            float wobble = 1f + (Mathf.PerlinNoise(Time.unscaledTime * arrowScaleWobbleHz, 0f) - 0.5f) * 2f * arrowScaleWobble;
            view.arrow.rectTransform.localScale = Vector3.one * arrowSizeMultiplier * pulse * wobble;
        }
        else
        {
            view.arrow.rectTransform.localScale = Vector3.one;
        }

        float a = 1f;
        if (maxDistance > 0f && fadeStartDistance > 0f && dist > fadeStartDistance)
            a = Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(fadeStartDistance, maxDistance, dist));
        SetGroupAlpha(view, a);
    }

    private static void SetGroupAlpha(MarkerView v, float a)
    {
        SetA(v.ring, a);
        SetA(v.pip, a);
        SetA(v.arrow, a);
        v.label.alpha = a;
        v.distance.alpha = a * 0.85f;
    }

    private static void SetA(Image img, float a)
    {
        if (img == null) return;
        var c = img.color; c.a = a; img.color = c;
    }

    private void EnsureSelfTestTransforms(Vector3 around)
    {
        if (selfTestTransforms.Count == 4) return;
        // Clear leftovers
        for (int i = 0; i < selfTestTransforms.Count; i++)
            if (selfTestTransforms[i] != null) Destroy(selfTestTransforms[i].gameObject);
        selfTestTransforms.Clear();

        Vector3[] offsets = {
            new Vector3( 0, 0,  10),  // forward
            new Vector3(10, 0,  0),   // right
            new Vector3( 0, 0, -10),  // back
            new Vector3(-10, 0, 0),   // left
        };
        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject($"HelmetHUD_SelfTest_{i}");
            go.transform.position = around + offsets[i];
            selfTestTransforms.Add(go.transform);
        }
    }

    private static string FormatDistance(float meters)
    {
        if (meters >= 1000f) return $"{meters / 1000f:0.0}km";
        return $"{Mathf.RoundToInt(meters)}m";
    }
}
