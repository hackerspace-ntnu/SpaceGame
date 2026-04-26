using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Renders the MapUI to an off-screen RenderTexture and projects it on a
/// world-space 3D quad in front of the local player. The HUD canvas is left
/// untouched — the map becomes a real holographic object in the scene.
///
/// Setup:
/// 1. Add this component to a GameObject in the persistent scene.
/// 2. Assign your existing MapUI panel (the GameObject with the MapUI script).
/// 3. Press Play. Press M to toggle.
///
/// The projector reparents the MapUI panel into a hidden render-target canvas
/// at runtime, so you can keep the panel in your HUD canvas in the editor.
/// </summary>
public class MapHologramProjector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MapUI mapPanel;

    [Header("Projection Transform (relative to player)")]
    [Tooltip("How far in front of the player the hologram floats (meters).")]
    [SerializeField] private float distance = 0.9f;
    [Tooltip("Side offset from the player. Negative = left, positive = right.")]
    [SerializeField] private float sideOffset = -0.55f;
    [Tooltip("Vertical offset from the player's feet (meters). Positive = up.")]
    [SerializeField] private float height = 1.25f;
    [Tooltip("Tilt of the projected quad around its X axis (degrees). 0 = vertical, 60 = nearly flat (table-top look).")]
    [Range(0, 90)]
    [SerializeField] private float tiltX = 25f;
    [Tooltip("Yaw rotation toward/away from player. Positive turns the quad toward the player's view.")]
    [Range(-60, 60)]
    [SerializeField] private float yawTowardPlayer = 25f;
    [Tooltip("Width and height of the world-space quad (meters).")]
    [SerializeField] private Vector2 quadSize = new Vector2(0.9f, 0.9f);
    [Tooltip("If true, the quad's base orientation follows the player's facing.")]
    [SerializeField] private bool faceCameraYaw = true;

    [Header("Helmet Beams")]
    [Tooltip("Optional source transform for the projection rays (e.g. a head/helmet bone). If null, falls back to player position + helmet height.")]
    [SerializeField] private Transform helmetAnchor;
    [Tooltip("Approximate helmet height above player root if no anchor is set.")]
    [SerializeField] private float helmetHeightFallback = 1.7f;
    [SerializeField] private bool showBeams = true;
    [SerializeField] private Color beamColor = new Color(0.30f, 0.95f, 1.00f, 1f);
    [Range(0.001f, 0.05f)]
    [SerializeField] private float beamWidth = 0.012f;
    [Tooltip("Where the beam starts: 0 = exactly at helmet anchor, 0.05 = a few cm out (lets you avoid clipping the helmet mesh).")]
    [SerializeField] private float beamOriginOffset = 0.05f;

    [Header("Render Texture")]
    [SerializeField] private int renderTextureSize = 1024;

    [Header("Hologram Look")]
    [SerializeField] private Color hologramTint = new Color(0.45f, 0.95f, 1.00f, 1f);
    [Range(0.1f, 5f)]
    [SerializeField] private float intensity = 1.4f;
    [SerializeField] private float scanlineSpeed = 1.0f;
    [SerializeField] private float scanlineDensity = 70f;
    [Range(0, 1)] [SerializeField] private float scanlineStrength = 0.10f;
    [Range(0, 0.5f)] [SerializeField] private float edgeFade = 0.18f;
    [Range(0, 1)] [SerializeField] private float flicker = 0.03f;
    [Tooltip("Soft volumetric glow halo around the projection. 0 = off.")]
    [Range(0, 1)] [SerializeField] private float haloStrength = 0.6f;
    [Tooltip("How big the halo extends beyond the quad (multiplier of quad size).")]
    [Range(1, 3)] [SerializeField] private float haloSize = 1.6f;

    [Header("Animation")]
    [SerializeField] private float wobbleAmplitudeDeg = 1.5f;
    [SerializeField] private float wobbleSpeed = 0.7f;
    [SerializeField] private float spawnRiseTime = 0.25f;

    [Header("Input")]
    [SerializeField] private string toggleActionName = "Map";
    [SerializeField] private bool startVisible;

    private RenderTexture renderTexture;
    private Canvas hiddenCanvas;
    private Camera rtCamera;
    private GameObject worldQuad;
    private GameObject haloQuad;
    private Material quadMaterial;
    private Material haloMaterial;
    private Material beamMaterial;
    private LineRenderer[] beams;
    private Transform player;
    private InputAction toggleAction;
    private Transform originalPanelParent;
    private bool visible;
    private float visibleSinceTime = -999f;

    private void Start()
    {
        if (mapPanel == null)
        {
            Debug.LogError("[MapHologramProjector] mapPanel reference is missing.", this);
            enabled = false;
            return;
        }

        BuildRenderTarget();
        BuildHiddenCanvas();
        BuildWorldQuad();
        BuildHalo();
        BuildBeams();
        ReparentMapPanel();

        toggleAction = InputSystem.actions?.FindAction(toggleActionName);
        if (toggleAction == null)
            Debug.LogWarning($"[MapHologramProjector] Input action '{toggleActionName}' not found.", this);

        SetVisible(startVisible);
    }

    private void OnDestroy()
    {
        // Return the panel to its original parent so the editor scene isn't orphaned.
        if (mapPanel != null && originalPanelParent != null)
            mapPanel.transform.SetParent(originalPanelParent, false);

        if (renderTexture != null) renderTexture.Release();
        if (quadMaterial != null) Destroy(quadMaterial);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (beamMaterial != null) Destroy(beamMaterial);
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            SetVisible(!visible);

        if (!visible) return;
        UpdateProjectionTransform();
        UpdateMaterial();
    }

    public void SetVisible(bool v)
    {
        visible = v;
        if (worldQuad != null) worldQuad.SetActive(v);
        if (haloQuad != null) haloQuad.SetActive(v && haloStrength > 0.001f);
        if (rtCamera != null) rtCamera.enabled = v;
        if (hiddenCanvas != null) hiddenCanvas.gameObject.SetActive(v);
        if (beams != null)
        {
            foreach (var beam in beams)
                if (beam != null) beam.gameObject.SetActive(v && showBeams);
        }
        if (mapPanel != null) mapPanel.SetVisible(v);
        if (v) visibleSinceTime = Time.time;
    }

    // ─────────────────────────────────────────────
    //  Build
    // ─────────────────────────────────────────────

    private void BuildRenderTarget()
    {
        renderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 0, RenderTextureFormat.ARGB32)
        {
            name = "MapHologram_RT",
            antiAliasing = 1,
            useMipMap = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        renderTexture.Create();
    }

    private void BuildHiddenCanvas()
    {
        var go = new GameObject("MapHologram_HiddenCanvas");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0, -10000, 0); // offscreen safety

        hiddenCanvas = go.AddComponent<Canvas>();
        hiddenCanvas.renderMode = RenderMode.ScreenSpaceCamera;

        // Dedicated camera that renders ONLY this canvas to the RT.
        var camGo = new GameObject("MapHologram_RTCamera");
        camGo.transform.SetParent(go.transform, false);
        rtCamera = camGo.AddComponent<Camera>();
        rtCamera.orthographic = true;
        rtCamera.orthographicSize = 5f;
        rtCamera.clearFlags = CameraClearFlags.SolidColor;
        rtCamera.backgroundColor = Color.black;
        rtCamera.cullingMask = 0;
        rtCamera.targetTexture = renderTexture;
        rtCamera.nearClipPlane = 0.01f;
        rtCamera.farClipPlane = 100f;
        rtCamera.depth = -100;          // render before any other camera
        rtCamera.useOcclusionCulling = false;
        rtCamera.allowHDR = false;
        rtCamera.allowMSAA = false;
        rtCamera.tag = "Untagged";       // never become Camera.main
        rtCamera.enabled = false;        // off until SetVisible(true)

        hiddenCanvas.worldCamera = rtCamera;
        hiddenCanvas.planeDistance = 1f;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Layer the canvas's children on a layer the rtCamera will see, and only that.
        // We use a dedicated bit on existing UI layer to keep things simple.
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 5; // Unity's default UI layer index
        go.layer = uiLayer;
        rtCamera.cullingMask = 1 << uiLayer;
    }

    private void BuildWorldQuad()
    {
        worldQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        worldQuad.name = "MapHologram_Quad";
        // Strip the collider; it's purely visual.
        var col = worldQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        worldQuad.transform.localScale = new Vector3(quadSize.x, quadSize.y, 1f);

        var shader = Shader.Find("Hologram/MapProjection");
        if (shader == null)
        {
            Debug.LogError("[MapHologramProjector] Shader 'Hologram/MapProjection' not found.", this);
            return;
        }

        quadMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        quadMaterial.SetTexture("_MainTex", renderTexture);

        var renderer = worldQuad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = quadMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void BuildHalo()
    {
        haloQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        haloQuad.name = "MapHologram_Halo";
        var col = haloQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Reuse the projection shader but feed it a soft white texture and
        // crank the edge fade so it looks like a glow around the projection.
        var shader = Shader.Find("Hologram/MapProjection");
        if (shader == null) return;

        haloMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        haloMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
        haloMaterial.SetFloat("_Intensity", haloStrength);
        haloMaterial.SetFloat("_EdgeFade", 0.45f);
        haloMaterial.SetFloat("_RimWidth", 0.0f);
        haloMaterial.SetFloat("_RimStrength", 0.0f);
        haloMaterial.SetFloat("_ScanlineStrength", 0.0f);
        haloMaterial.SetFloat("_FlickerAmount", 0.0f);

        var renderer = haloQuad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = haloMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void BuildBeams()
    {
        beams = new LineRenderer[4];
        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject($"MapHologram_Beam_{i}");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.startWidth = beamWidth;
            lr.endWidth = beamWidth * 1.5f; // slightly wider at quad end for "spread" feel
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            if (beamMaterial == null)
                beamMaterial = CreateBeamMaterial();

            lr.sharedMaterial = beamMaterial;
            lr.startColor = new Color(beamColor.r, beamColor.g, beamColor.b, 0.05f);
            lr.endColor   = new Color(beamColor.r, beamColor.g, beamColor.b, 0.55f);
            beams[i] = lr;
        }
    }

    private Material CreateBeamMaterial()
    {
        // Reuse the same hologram shader for additive beams. Texture = white,
        // single-color tint via _Color, no scanlines/flicker.
        var shader = Shader.Find("Hologram/MapProjection");
        if (shader == null) return null;
        var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        mat.SetTexture("_MainTex", Texture2D.whiteTexture);
        mat.SetColor("_Color", beamColor);
        mat.SetFloat("_Intensity", 1.2f);
        mat.SetFloat("_EdgeFade", 0.0f);
        mat.SetFloat("_RimWidth", 0.0f);
        mat.SetFloat("_RimStrength", 0.0f);
        mat.SetFloat("_ScanlineStrength", 0.0f);
        mat.SetFloat("_FlickerAmount", 0.0f);
        return mat;
    }

    private void ReparentMapPanel()
    {
        originalPanelParent = mapPanel.transform.parent;
        mapPanel.transform.SetParent(hiddenCanvas.transform, false);

        // Ensure the panel and all its children are on the UI layer the rtCamera sees.
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 5;
        SetLayerRecursively(mapPanel.gameObject, uiLayer);

        // The map panel's RectTransform: anchor to canvas center, fit fully.
        var rt = (RectTransform)mapPanel.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
    }

    // ─────────────────────────────────────────────
    //  Update
    // ─────────────────────────────────────────────

    private void UpdateProjectionTransform()
    {
        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
            if (player == null) return;
        }

        Vector3 forward = faceCameraYaw ? player.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 basePos = player.position
                          + forward * distance
                          + right * sideOffset
                          + Vector3.up * height;

        float wobbleX = Mathf.Sin(Time.time * wobbleSpeed * Mathf.PI * 2f) * wobbleAmplitudeDeg;
        float wobbleZ = Mathf.Sin(Time.time * wobbleSpeed * Mathf.PI * 2f * 0.73f) * wobbleAmplitudeDeg * 0.5f;

        // Rise/scale-in animation when first shown.
        float t = Mathf.Clamp01((Time.time - visibleSinceTime) / Mathf.Max(0.0001f, spawnRiseTime));
        float rise = Mathf.SmoothStep(0f, 1f, t);

        Quaternion baseRot = Quaternion.LookRotation(forward) * Quaternion.Euler(tiltX + wobbleX, yawTowardPlayer, wobbleZ);

        worldQuad.transform.position = basePos - Vector3.up * (1f - rise) * 0.35f;
        worldQuad.transform.rotation = baseRot;
        worldQuad.transform.localScale = new Vector3(quadSize.x * rise, quadSize.y * rise, 1f);

        if (haloQuad != null)
        {
            haloQuad.transform.position = worldQuad.transform.position - worldQuad.transform.forward * 0.005f;
            haloQuad.transform.rotation = baseRot;
            haloQuad.transform.localScale = new Vector3(quadSize.x * haloSize * rise, quadSize.y * haloSize * rise, 1f);
        }

        UpdateBeams(rise);
    }

    private void UpdateBeams(float rise)
    {
        if (beams == null || !showBeams) return;

        Vector3 origin = helmetAnchor != null
            ? helmetAnchor.position
            : player.position + Vector3.up * helmetHeightFallback;

        // Push the start point a bit forward so beams don't begin inside the helmet.
        Vector3 toQuad = (worldQuad.transform.position - origin).normalized;
        Vector3 start = origin + toQuad * beamOriginOffset;

        // Compute the four corners of the quad in world space.
        Vector3 r = worldQuad.transform.right * (quadSize.x * 0.5f) * rise;
        Vector3 u = worldQuad.transform.up    * (quadSize.y * 0.5f) * rise;
        Vector3 c = worldQuad.transform.position;
        Vector3 cTL = c - r + u;
        Vector3 cTR = c + r + u;
        Vector3 cBR = c + r - u;
        Vector3 cBL = c - r - u;

        Vector3[] corners = { cTL, cTR, cBR, cBL };
        for (int i = 0; i < beams.Length; i++)
        {
            beams[i].SetPosition(0, start);
            beams[i].SetPosition(1, corners[i]);
            beams[i].startWidth = beamWidth * rise;
            beams[i].endWidth   = beamWidth * 1.5f * rise;
        }
    }

    private void UpdateMaterial()
    {
        if (quadMaterial != null)
        {
            quadMaterial.SetColor("_Color", hologramTint);
            quadMaterial.SetFloat("_Intensity", intensity);
            quadMaterial.SetFloat("_ScanlineSpeed", scanlineSpeed);
            quadMaterial.SetFloat("_ScanlineDensity", scanlineDensity);
            quadMaterial.SetFloat("_ScanlineStrength", scanlineStrength);
            quadMaterial.SetFloat("_EdgeFade", edgeFade);
            quadMaterial.SetFloat("_FlickerAmount", flicker);
        }
        if (haloMaterial != null)
        {
            haloMaterial.SetColor("_Color", hologramTint);
            haloMaterial.SetFloat("_Intensity", haloStrength);
        }
        if (beamMaterial != null)
        {
            beamMaterial.SetColor("_Color", beamColor);
        }
    }

    // ─────────────────────────────────────────────

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }
}
