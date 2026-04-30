using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Procedurally builds a holographic-style tile-grid map UI from a
/// WorldStreamingConfig and the PNGs baked by MapTileBaker.
///
/// Place this on a panel (with a CanvasGroup) inside the persistent HUD canvas.
/// Provide two child RectTransforms — mapArea (tiles) and overlay (player + markers).
/// All visuals are generated. No prefabs required.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MapUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WorldStreamingConfig config;
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private RectTransform overlay;

    [Header("Layout")]
    [Tooltip("Maximum size in pixels for the map area along its longest axis.")]
    [SerializeField] private float maxMapPixelSize = 600f;

    [Header("Toggle")]
    [SerializeField] private string toggleActionName = "Map";
    [SerializeField] private bool startVisible;

    [Header("Holographic Style")]
    [Tooltip("If true, all map graphics use an additive UI shader so dark areas become transparent and bright areas glow through.")]
    [SerializeField] private bool holographic = true;
    [Range(0.5f, 4f)]
    [SerializeField] private float holographicGlow = 1.6f;

    [Header("3D Hologram")]
    [Tooltip("Legacy 3D-tilt mode applied to the panel itself.")]
    [SerializeField] private bool use3DPerspective = false;
    [Range(0f, 75f)]
    [SerializeField] private float tiltAngleX = 35f;
    [Tooltip("Vertical bob amplitude (degrees) for an unsteady-hologram feel. 0 disables.")]
    [SerializeField] private float wobbleAmplitude = 1.5f;
    [SerializeField] private float wobbleSpeed = 0.7f;
    [Tooltip("How far in front of the map plane (toward the camera) markers and the player icon float, in panel units.")]
    [SerializeField] private float overlayLift = 18f;
    [Tooltip("Subtle background fill behind tiles. Keep alpha low (or 0) for full see-through.")]
    [SerializeField] private Color backgroundColor = new Color(0.05f, 0.30f, 0.40f, 0.10f);
    [SerializeField] private Color unrevealedColor = new Color(0.04f, 0.18f, 0.25f, 0.35f);
    [SerializeField] private Color tileTint = new Color(0.55f, 0.95f, 1.00f, 1f);
    [SerializeField] private Color borderColor = new Color(0.20f, 0.85f, 1.00f, 1f);
    [SerializeField] private Color gridColor = new Color(0.20f, 0.85f, 1.00f, 0.30f);
    [SerializeField] private Color readoutColor = new Color(0.20f, 0.95f, 1.00f, 0.95f);
    [SerializeField] private Color playerColor = new Color(0.30f, 1.00f, 1.00f, 1f);
    [SerializeField] private float borderThickness = 2f;
    [SerializeField] private float playerIconSize = 18f;
    [SerializeField] private float markerIconSize = 16f;
    [SerializeField] private float markerLabelSize = 11f;
    [SerializeField] private float readoutFontSize = 12f;
    [SerializeField] private float scanlineScrollSpeed = 0.25f;
    [SerializeField] private float borderPulseSpeed = 1.2f;

    private CanvasGroup canvasGroup;
    private InputAction toggleAction;
    private float pixelsPerWorldUnit;
    private Vector2 worldOriginXZ;
    private Material holoMaterial;

    private readonly Dictionary<Vector2Int, Image> tileImages = new();
    private readonly Dictionary<MapService.Marker, MarkerVisual> markerVisuals = new();
    private RectTransform playerIcon;
    private Image playerImage;
    private RawImage hexGridRawImage;
    private readonly List<Image> borderStrips = new();
    private TMP_Text sectorReadout;
    private TMP_Text scanReadout;

    private struct MarkerVisual
    {
        public RectTransform root;
        public TMP_Text label;
    }

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        SetVisible(startVisible);
        toggleAction = InputSystem.actions?.FindAction(toggleActionName);
        if (toggleAction == null)
            Debug.LogWarning($"[MapUI] Input action '{toggleActionName}' not found.", this);
    }

    private void Start()
    {
        if (!ValidateRefs()) return;

        if (use3DPerspective) Setup3DCanvas();

        if (holographic)
        {
            var shader = Shader.Find("UI/MapHolographic");
            if (shader != null)
            {
                holoMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                holoMaterial.SetFloat("_Glow", holographicGlow);
            }
            else
            {
                Debug.LogWarning("[MapUI] Shader 'UI/MapHolographic' not found — falling back to standard UI blending.");
            }
        }

        BuildTiles();
        BuildHexOverlay();
        BuildScanlines();
        BuildBorder();
        BuildReadouts();
        BuildPlayerIcon();
        ApplyHologramTilt();

        var svc = MapService.Instance;
        if (svc != null)
        {
            svc.OnChunkRevealed += HandleChunkRevealed;
            svc.OnMarkerAdded   += HandleMarkerAdded;
            svc.OnMarkerRemoved += HandleMarkerRemoved;

            foreach (var c in svc.RevealedChunks) HandleChunkRevealed(c);
            foreach (var m in svc.Markers)        HandleMarkerAdded(m);
        }
    }

    private void OnDestroy()
    {
        var svc = MapService.Instance;
        if (svc != null)
        {
            svc.OnChunkRevealed -= HandleChunkRevealed;
            svc.OnMarkerAdded   -= HandleMarkerAdded;
            svc.OnMarkerRemoved -= HandleMarkerRemoved;
        }
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            SetVisible(canvasGroup.alpha < 0.5f);

        if (canvasGroup.alpha < 0.01f) return;

        UpdateAnimation();
        UpdateReadouts();
    }

    private void LateUpdate()
    {
        if (canvasGroup == null || canvasGroup.alpha < 0.01f) return;
        // Sample positions in LateUpdate so we read the player's interpolated
        // transform after physics interpolation and any camera/animator rigs
        // have settled — eliminates icon jitter relative to the world.
        UpdatePlayerIcon();
        UpdateMarkers();
    }

    public void SetVisible(bool visible)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private bool ValidateRefs()
    {
        if (config == null) { Debug.LogError("[MapUI] WorldStreamingConfig not assigned.", this); return false; }
        if (mapArea == null) { Debug.LogError("[MapUI] mapArea not assigned.", this); return false; }
        if (overlay == null) { Debug.LogError("[MapUI] overlay not assigned.", this); return false; }
        return true;
    }

    // ─────────────────────────────────────────────
    //  Build
    // ─────────────────────────────────────────────

    private void BuildTiles()
    {
        var worldSize = new Vector2(
            config.gridDimensions.x * config.chunkSize.x,
            config.gridDimensions.y * config.chunkSize.y);
        float longest = Mathf.Max(worldSize.x, worldSize.y);
        pixelsPerWorldUnit = maxMapPixelSize / Mathf.Max(0.0001f, longest);
        worldOriginXZ = new Vector2(config.worldOrigin.x, config.worldOrigin.z);

        Vector2 mapPixelSize = worldSize * pixelsPerWorldUnit;
        mapArea.sizeDelta = mapPixelSize;
        overlay.sizeDelta = mapPixelSize;

        SetBottomLeftPivot(mapArea);
        SetBottomLeftPivot(overlay);

        // Subtle background fill — alpha is low so the world shows through.
        var bg = CreateImage(mapArea, "Background", backgroundColor);
        bg.transform.SetAsFirstSibling();
        var bgRt = (RectTransform)bg.transform;
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = mapPixelSize;

        Vector2 tilePixel = config.chunkSize * pixelsPerWorldUnit;
        for (int cx = 0; cx < config.gridDimensions.x; cx++)
        {
            for (int cy = 0; cy < config.gridDimensions.y; cy++)
            {
                var coord = new Vector2Int(cx, cy);
                var img = CreateImage(mapArea, $"Tile_{cx}_{cy}", unrevealedColor);
                var rt = (RectTransform)img.transform;
                rt.anchoredPosition = new Vector2(cx * tilePixel.x, cy * tilePixel.y);
                rt.sizeDelta = tilePixel;
                tileImages[coord] = img;
            }
        }
    }

    private void BuildHexOverlay()
    {
        // RawImage so we can tile the texture across the whole map area.
        var go = new GameObject("HexGrid", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(mapArea, false);
        SetBottomLeftPivot(rt);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = mapArea.sizeDelta;

        hexGridRawImage = go.GetComponent<RawImage>();
        hexGridRawImage.texture = MapTextures.HexGrid.texture;
        hexGridRawImage.color = gridColor;
        hexGridRawImage.raycastTarget = false;
        if (holoMaterial != null) hexGridRawImage.material = holoMaterial;
        // Tile the grid: uvRect width/height = mapSize / textureSize.
        Vector2 tile = mapArea.sizeDelta / 128f;
        hexGridRawImage.uvRect = new Rect(0, 0, tile.x, tile.y);
    }

    private void BuildScanlines()
    {
        var go = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(mapArea, false);
        SetBottomLeftPivot(rt);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = mapArea.sizeDelta;

        scanlinesRaw = go.GetComponent<RawImage>();
        scanlinesRaw.texture = MapTextures.Scanlines.texture;
        scanlinesRaw.color = new Color(0.30f, 0.85f, 1.00f, 0.45f);
        scanlinesRaw.raycastTarget = false;
        if (holoMaterial != null) scanlinesRaw.material = holoMaterial;
        Vector2 scan = mapArea.sizeDelta / 4f;
        scanlinesRaw.uvRect = new Rect(0, 0, scan.x, scan.y);
    }

    private RawImage scanlinesRaw;

    private void BuildBorder()
    {
        Vector2 size = mapArea.sizeDelta;
        borderStrips.Add(SpawnStrip("Border_T", new Vector2(0, size.y - borderThickness), new Vector2(size.x, borderThickness)));
        borderStrips.Add(SpawnStrip("Border_B", Vector2.zero,                              new Vector2(size.x, borderThickness)));
        borderStrips.Add(SpawnStrip("Border_L", Vector2.zero,                              new Vector2(borderThickness, size.y)));
        borderStrips.Add(SpawnStrip("Border_R", new Vector2(size.x - borderThickness, 0), new Vector2(borderThickness, size.y)));

        // Bracket corners — short orthogonal strips offset slightly outward.
        float bracketLen = Mathf.Min(20f, size.x * 0.08f);
        float off = 4f;
        SpawnStrip("BR_TL_h", new Vector2(-off, size.y - borderThickness),               new Vector2(bracketLen, borderThickness));
        SpawnStrip("BR_TL_v", new Vector2(-off, size.y - bracketLen),                    new Vector2(borderThickness, bracketLen));
        SpawnStrip("BR_TR_h", new Vector2(size.x - bracketLen + off, size.y - borderThickness), new Vector2(bracketLen, borderThickness));
        SpawnStrip("BR_TR_v", new Vector2(size.x + off - borderThickness, size.y - bracketLen), new Vector2(borderThickness, bracketLen));
        SpawnStrip("BR_BL_h", new Vector2(-off, 0),                                       new Vector2(bracketLen, borderThickness));
        SpawnStrip("BR_BL_v", new Vector2(-off, 0),                                       new Vector2(borderThickness, bracketLen));
        SpawnStrip("BR_BR_h", new Vector2(size.x - bracketLen + off, 0),                  new Vector2(bracketLen, borderThickness));
        SpawnStrip("BR_BR_v", new Vector2(size.x + off - borderThickness, 0),             new Vector2(borderThickness, bracketLen));
    }

    private Image SpawnStrip(string name, Vector2 anchoredPos, Vector2 size)
    {
        var img = CreateImage(mapArea, name, borderColor);
        var rt = (RectTransform)img.transform;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return img;
    }

    private void BuildReadouts()
    {
        sectorReadout = CreateText(overlay, "Readout_Sector", "SECTOR --,--", TextAlignmentOptions.TopLeft);
        var rtA = (RectTransform)sectorReadout.transform;
        rtA.pivot = new Vector2(0, 1);
        rtA.anchorMin = rtA.anchorMax = new Vector2(0, 1);
        rtA.anchoredPosition = new Vector2(6, -4);
        rtA.sizeDelta = new Vector2(160, 18);

        scanReadout = CreateText(overlay, "Readout_Scan", "● LIVE", TextAlignmentOptions.TopRight);
        var rtB = (RectTransform)scanReadout.transform;
        rtB.pivot = new Vector2(1, 1);
        rtB.anchorMin = rtB.anchorMax = new Vector2(1, 1);
        rtB.anchoredPosition = new Vector2(-6, -4);
        rtB.sizeDelta = new Vector2(120, 18);
    }

    private void BuildPlayerIcon()
    {
        var img = CreateImage(overlay, "PlayerIcon", playerColor);
        img.sprite = MapTextures.Triangle;
        var rt = (RectTransform)img.transform;
        rt.pivot = new Vector2(0.5f, 0.3f);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(playerIconSize, playerIconSize * 1.2f);
        playerIcon = rt;
        playerImage = img;
    }

    // ─────────────────────────────────────────────
    //  Update
    // ─────────────────────────────────────────────

    private void UpdatePlayerIcon()
    {
        var svc = MapService.Instance;
        if (svc == null || svc.LocalPlayer == null || playerIcon == null) return;
        playerIcon.anchoredPosition = WorldToMap(svc.LocalPlayer.position);

        // Derive yaw from the camera's forward when available — it reflects
        // what the player is actually looking at and avoids Euler-decomposition
        // quirks on the player rigidbody.
        Vector3 fwd = svc.LocalPlayer.forward;
        var cam = Camera.main;
        if (cam != null) fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return;
        float yawDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        playerIcon.localEulerAngles = new Vector3(0, 0, -yawDeg);
    }

    private void UpdateMarkers()
    {
        var svc = MapService.Instance;
        if (svc == null) return;

        foreach (var kvp in markerVisuals)
        {
            var m = kvp.Key;
            var v = kvp.Value;
            if (v.root == null) continue;

            Vector3 worldPos = m.GetWorldPosition();
            bool show = !m.requiresRevealedChunk
                || svc.IsChunkRevealed(config.WorldToChunkCoord(worldPos));

            v.root.gameObject.SetActive(show);
            if (!show) continue;

            v.root.anchoredPosition = WorldToMap(worldPos);
        }
    }

    private void ApplyHologramTilt()
    {
        if (!use3DPerspective) return;
        transform.localRotation = Quaternion.Euler(tiltAngleX, 0f, 0f);

        if (overlay != null && overlayLift > 0f)
        {
            var p = overlay.localPosition;
            overlay.localPosition = new Vector3(p.x, p.y, -overlayLift);
        }
    }

    /// <summary>
    /// Wraps this MapUI in a child Canvas set to Screen Space - Camera with a
    /// perspective camera, so the 3D tilt actually renders with perspective.
    /// Skips setup if a suitable canvas is already in place.
    /// </summary>
    private void Setup3DCanvas()
    {
        var existingCanvas = GetComponent<Canvas>();
        if (existingCanvas == null)
        {
            existingCanvas = gameObject.AddComponent<Canvas>();
            existingCanvas.overrideSorting = true;
            existingCanvas.sortingOrder = 100;
        }

        existingCanvas.renderMode = RenderMode.ScreenSpaceCamera;

        // Find or create a dedicated UI camera for the map.
        var camGo = GameObject.Find("MapHologramCamera");
        Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;

        if (cam == null)
        {
            camGo = new GameObject("MapHologramCamera");
            camGo.transform.SetParent(transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;     // never clear color — only depth
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << LayerMask.NameToLayer("UI");
            cam.orthographic = false;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 5000f;
            cam.depth = 50;
            cam.allowMSAA = true;
            cam.tag = "Untagged";
            // Leave the camera at the canvas's anchor — Unity positions it automatically.
        }

        existingCanvas.worldCamera = cam;
        existingCanvas.planeDistance = 500f;

        // Make sure this object renders on the UI layer so the camera sees it.
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) SetLayerRecursively(gameObject, uiLayer);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }

    private void UpdateAnimation()
    {
        // Hologram wobble — subtle unsteady oscillation around the tilt axis.
        if (use3DPerspective && wobbleAmplitude > 0.01f)
        {
            float t = Time.time * wobbleSpeed * Mathf.PI * 2f;
            float wx = Mathf.Sin(t) * wobbleAmplitude;
            float wy = Mathf.Sin(t * 0.73f) * wobbleAmplitude * 0.6f;
            transform.localRotation = Quaternion.Euler(tiltAngleX + wx, wy, 0f);
        }

        // Scroll the scanlines downward over time.
        if (scanlinesRaw != null)
        {
            var uv = scanlinesRaw.uvRect;
            uv.y -= Time.deltaTime * scanlineScrollSpeed;
            scanlinesRaw.uvRect = uv;
        }

        // Pulse the border alpha.
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * borderPulseSpeed * Mathf.PI * 2f);
        var bc = borderColor;
        bc.a *= pulse;
        foreach (var strip in borderStrips)
            if (strip != null) strip.color = bc;

        // Player icon subtle pulse
        if (playerImage != null)
        {
            var pc = playerColor;
            pc.a *= 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
            playerImage.color = pc;
        }
    }

    private void UpdateReadouts()
    {
        var svc = MapService.Instance;
        if (svc == null || svc.LocalPlayer == null || sectorReadout == null) return;
        var c = config.WorldToChunkCoord(svc.LocalPlayer.position);
        sectorReadout.text = $"SECTOR {c.x:00},{c.y:00}";

        if (scanReadout != null)
        {
            int total = (config.gridDimensions.x * config.gridDimensions.y);
            int known = svc.RevealedChunks.Count;
            int pct = total > 0 ? Mathf.RoundToInt(100f * known / total) : 0;
            bool blink = (Time.time % 1f) < 0.7f;
            scanReadout.text = (blink ? "● " : "○ ") + $"SCAN {pct:00}%";
        }
    }

    // ─────────────────────────────────────────────
    //  Markers / chunks
    // ─────────────────────────────────────────────

    private void HandleChunkRevealed(Vector2Int coord)
    {
        if (!tileImages.TryGetValue(coord, out var img)) return;
        var sprite = LoadTileSprite(coord);
        if (sprite != null)
        {
            img.sprite = sprite;
            img.color = tileTint;
        }
        else
        {
            img.color = Color.Lerp(unrevealedColor, tileTint, 0.15f);
        }
    }

    private void HandleMarkerAdded(MapService.Marker marker)
    {
        if (markerVisuals.ContainsKey(marker)) return;

        var go = new GameObject("Marker", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(overlay, false);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(markerIconSize, markerIconSize);

        var iconImg = CreateImage(rt, "Icon", MapMarkerColors.For(marker.type));
        iconImg.sprite = MapTextures.RingMarker;
        var iconRt = (RectTransform)iconImg.transform;
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(markerIconSize, markerIconSize);

        TMP_Text label = null;
        if (!string.IsNullOrEmpty(marker.label))
        {
            label = CreateText(rt, "Label", marker.label, TextAlignmentOptions.Top);
            label.fontSize = markerLabelSize;
            label.color = MapMarkerColors.For(marker.type);
            var lrt = (RectTransform)label.transform;
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0, -2);
            lrt.sizeDelta = new Vector2(120, 16);
        }

        markerVisuals[marker] = new MarkerVisual { root = rt, label = label };
    }

    private void HandleMarkerRemoved(MapService.Marker marker)
    {
        if (!markerVisuals.TryGetValue(marker, out var v)) return;
        markerVisuals.Remove(marker);
        if (v.root != null) Destroy(v.root.gameObject);
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    private Vector2 WorldToMap(Vector3 worldPos)
    {
        return new Vector2(
            (worldPos.x - worldOriginXZ.x) * pixelsPerWorldUnit,
            (worldPos.z - worldOriginXZ.y) * pixelsPerWorldUnit);
    }

    private static Sprite LoadTileSprite(Vector2Int coord)
    {
        return Resources.Load<Sprite>($"MapTiles/Tile_{coord.x}_{coord.y}");
    }

    private Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        SetBottomLeftPivot(rt);
        var img = go.GetComponent<Image>();
        img.sprite = MapTextures.White;
        img.color = color;
        img.raycastTarget = false;
        if (holoMaterial != null) img.material = holoMaterial;
        return img;
    }

    private TMP_Text CreateText(Transform parent, string name, string content, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = readoutFontSize;
        t.color = readoutColor;
        t.alignment = align;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.fontStyle = FontStyles.Bold;
        return t;
    }

    private static void SetBottomLeftPivot(RectTransform rt)
    {
        rt.pivot = new Vector2(0, 0);
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
    }
}
