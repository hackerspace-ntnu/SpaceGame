using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Real 3D terrain hologram. Spawns one mesh per revealed chunk (loaded from
/// Resources/MapMeshes/ at runtime), parents them under a centered, scaled root
/// that floats next to the player. Adds a soft volumetric cone from the
/// player's helmet to the hologram, plus a player marker and POI markers.
/// </summary>
public class MapHologramTerrain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WorldStreamingConfig config;
    [SerializeField] private string toggleActionName = "Map";
    [SerializeField] private bool startVisible;

    [Header("Placement (relative to projector anchor)")]
    [Tooltip("Transform that defines where the hologram floats. The hologram is placed at this anchor's position plus the offsets below, using the anchor's forward/right/up axes. If null, falls back to the player.")]
    [SerializeField] private Transform projectorAnchor;
    [SerializeField] private Transform helmetAnchor;
    [SerializeField] private float helmetHeightFallback = 1.7f;
    [SerializeField] private float distance = 0.9f;
    [SerializeField] private float sideOffset = -0.55f;
    [SerializeField] private float height = 1.25f;
    [Tooltip("Small twist around the hologram's local up axis (degrees), purely for readability. Applied on top of the camera-facing orientation.")]
    [Range(-90, 90)]
    [SerializeField] private float yawTowardPlayer = 25f;
    [Tooltip("Fixed lean (degrees) of the map's surface from world horizontal, tilting its top edge in the world-fixed lean direction. Higher values protect against seeing the underside when looking up — geometrically, this is the maximum upward camera pitch (from horizontal) that still shows the top.")]
    [Range(0f, 89f)]
    [SerializeField] private float leanTowardPlayer = 45f;

    [Header("Hologram Scale")]
    [Tooltip("Width of the hologram footprint in meters.")]
    [SerializeField] private float footprint = 0.55f;
    [Tooltip("Vertical exaggeration. 1 = real proportions, <1 flattens. Lower = more readable, higher = dramatic peaks.")]
    [Range(0.05f, 10f)]
    [SerializeField] private float verticalExaggeration = 0.6f;
    [Tooltip("How many chunks around the player are shown. 1 = 3x3 area, 2 = 5x5, etc. Set very high to show the whole world.")]
    [Range(0, 12)]
    [SerializeField] private int viewRadius = 6;
    [Tooltip("If on, the hologram recenters on the player's current chunk each frame (mini-map style).")]
    [SerializeField] private bool centerOnPlayer = true;

    [Header("Hologram Look")]
    [SerializeField] private Color hologramTint = new Color(0.45f, 0.95f, 1.00f, 1f);
    [SerializeField] private Color valleyTint = new Color(0.10f, 0.45f, 0.65f, 1f);
    [SerializeField] private Color peakTint = new Color(0.85f, 1.00f, 1.00f, 1f);
    [Range(0, 5)] [SerializeField] private float intensity = 0.7f;
    [SerializeField] private float contourSpacing = 20f;
    [Range(0, 0.5f)] [SerializeField] private float contourThickness = 0.08f;
    [SerializeField] private float gridSpacing = 32f;
    [Range(0, 2)] [SerializeField] private float gridStrength = 0.4f;
    [Range(0, 8)] [SerializeField] private float fresnelPower = 2.5f;
    [Range(0, 4)] [SerializeField] private float fresnelStrength = 1.6f;

    [Header("Sunray Fan")]
    [SerializeField] private bool showBeam = true;
    [Tooltip("Reach radius as a fraction of the hologram footprint. Determines how wide the ray fan spreads at the base.")]
    [Range(0.2f, 0.9f)] [SerializeField] private float beamRadiusFraction = 0.449f;
    [SerializeField] private float beamOriginOffset = 0.05f;
    [Tooltip("Number of streak quads in the cone fan. Each quad is one straight ray.")]
    [Range(3, 96)] [SerializeField] private int sunrayCount = 86;
    [Tooltip("Width of each streak at the base ring. Smaller = thinner rays.")]
    [Range(0.005f, 0.3f)] [SerializeField] private float sunrayBaseWidth = 0.3f;
    [Tooltip("Slow rotation of the whole fan around the cone axis (revolutions per second). 0 = static.")]
    [SerializeField] private float sunraySpinSpeed = 6f;

    [Tooltip("Brightness at the apex of each ray.")]
    [Range(0, 2)] [SerializeField] private float sunrayApexAlpha = 0.14f;
    [Tooltip("Brightness at the base of each ray.")]
    [Range(0, 1)] [SerializeField] private float sunrayBaseAlpha = 0.0f;
    [Tooltip("How sharply each streak fades at its left/right edges. Higher = harder edge.")]
    [Range(1f, 8f)] [SerializeField] private float sunrayEdgeSharpness = 7.02f;
    [Tooltip("Per-ray independent brightness shimmer strength.")]
    [Range(0f, 1f)] [SerializeField] private float sunrayShimmer = 0.333f;
    [Tooltip("How fast individual rays shimmer (Hz).")]
    [SerializeField] private float sunrayShimmerSpeed = 0.6f;

    [Header("Layer (helps exclude from helmet/screen shaders)")]
    [Tooltip("All hologram visuals are placed on this layer at startup. Create a layer named 'Hologram' (Edit → Project Settings → Tags & Layers) and exclude it from helmet/screen-space shader cameras to stop bleed-through.")]
    [SerializeField] private string hologramLayerName = "Hologram";
    [Tooltip("Temporarily disables GlassDistortionRenderFeature while the map is open to prevent bleed-through onto hologram visuals.")]
    [SerializeField] private bool disableGlassDistortionWhileOpen = false;

    [Header("Markers")]
    [SerializeField] private float playerMarkerSize = 0.025f;
    [SerializeField] private float markerSize = 0.018f;
    [SerializeField] private float markerLift = 0.04f;
    [Tooltip("A vertical pillar/spike above each marker so it's visible against the terrain. 0 = off.")]
    [SerializeField] private float markerSpikeHeight = 0.06f;
    [SerializeField] private float markerSpikeWidth = 0.005f;
    [Range(0, 8)] [SerializeField] private float playerIntensity = 1.75f;
    [Range(0, 8)] [SerializeField] private float markerIntensity = 1.25f;
    [SerializeField] private bool showOnlyRevealedMarkers = true;
    [Tooltip("Spawn meshes for all chunks at startup, ignoring MapService reveal state. Useful for debugging or static maps.")]
    [SerializeField] private bool revealAllChunks;

    [Header("Marker Labels")]
    [Tooltip("If on, POI labels (the marker's text) are rendered above each marker, billboarded to face the camera.")]
    [SerializeField] private bool showMarkerLabels = true;
    [Tooltip("Marker label visual size. Higher = bigger text. Roughly equivalent to TMP font-size units.")]
    [SerializeField] private float markerLabelFontSize = 64f;
    [Tooltip("Vertical offset of the label above the marker, in marker-local units (1 = markerSize).")]
    [SerializeField] private float markerLabelHeight = 2.5f;
    [SerializeField] private Color markerLabelColor = new Color(0.85f, 1.00f, 1.00f, 1f);

    [Header("Animation")]
    [SerializeField] private float spawnRiseTime = 0.35f;
    [SerializeField] private float wobbleAmplitudeDeg = 0.8f;
    [SerializeField] private float wobbleSpeed = 0.4f;

    [Header("Fog of War")]
    [Tooltip("If on, terrain only renders in detail near places the player has been. Other areas are obscured by an animated fog. POIs remain visible.")]
    [SerializeField] private bool enableFogOfWar = true;
    [Tooltip("World-space radius around each recorded discovery point that counts as 'revealed'. Smaller = more fog left to discover.")]
    [SerializeField] private float discoveryRadius = 700f;
    [Tooltip("Soft edge width (m) at the boundary between revealed and fogged terrain.")]
    [SerializeField] private float discoveryFalloff = 280f;
    [Tooltip("Minimum world distance the player must move before a new discovery point is sampled.")]
    [SerializeField] private float discoveryPointSpacing = 120f;
    [Tooltip("Maximum number of discovery points kept. Once full, oldest is dropped (matches shader's MAX_DISCOVERY_POINTS = 256).")]
    [SerializeField] private int maxDiscoveryPoints = 256;
    [SerializeField] private Color fogColor = new Color(0.20f, 0.40f, 0.55f, 1f);
    [Range(0f, 4f)] [SerializeField] private float fogIntensity = 0.6f;
    [SerializeField] private float fogNoiseScale = 0.015f;
    [SerializeField] private float fogNoiseSpeed = 0.08f;
    [Tooltip("How much to darken fog on steep slopes. 0 = no dim (peaks pop through fog). 1 = vertical faces fully dark. ~0.85 hides peaks well.")]
    [Range(0f, 1f)] [SerializeField] private float fogSlopeDim = 0.85f;
    [Tooltip("How much to darken fog on view-grazing surfaces. Helps hide silhouette stacking when you look at the map from a low angle.")]
    [Range(0f, 1f)] [SerializeField] private float fogViewDim = 0.6f;

    [Header("Map Shape (round vignette)")]
    [Tooltip("Radius (sim-world meters) of the circular map area at full opacity. Beyond this the hologram fades out with a fuzzy edge.")]
    [SerializeField] private float mapRadius = 1100f;
    [Tooltip("Width (m) of the soft fade-out at the map's edge. Larger = fuzzier rim.")]
    [SerializeField] private float mapEdgeFalloff = 450f;

    // Runtime
    private Transform root;
    private Transform terrainContainer; // children: per-chunk mesh GOs
    private Transform markerContainer;
    private GameObject playerMarker;
    private GameObject beamObject;
    private MeshFilter beamMeshFilter;
    private Material terrainMaterial;
    private Material beamMaterial;
    private InputAction toggleAction;
    private Transform player;
    private bool visible;
    private float visibleSinceTime = -999f;

    private readonly Dictionary<Vector2Int, GameObject> chunkMeshes = new();
    private readonly Dictionary<MapService.Marker, GameObject> markerVisuals = new();
    private readonly Dictionary<MapService.Marker, GameObject> markerLabels = new();

    // Fog of war: rolling buffer of revealed world-XZ centers.
    private readonly List<Vector4> discoveryPoints = new();
    private Vector3 lastDiscoverySamplePos;
    private bool hasDiscoverySample;
    private static readonly Vector4[] discoveryUploadBuffer = new Vector4[256];

    private float MinTerrainY = 0f;
    private float MaxTerrainY = 200f;
    private int hologramLayer = -1;

    private void Start()
    {
        if (config == null)
        {
            Debug.LogError("[MapHologramTerrain] WorldStreamingConfig not assigned.", this);
            enabled = false;
            return;
        }

        BuildRoot();
        BuildBeam();
        BuildPlayerMarker();
        SubscribeToMapService();
        ApplyHologramLayer();

        toggleAction = InputSystem.actions?.FindAction(toggleActionName);
        if (toggleAction == null)
            Debug.LogWarning($"[MapHologramTerrain] Input action '{toggleActionName}' not found.", this);

        SetVisible(startVisible);
    }

    private void OnDestroy()
    {
        var svc = MapService.Instance;
        if (svc != null)
        {
            svc.OnChunkRevealed -= OnChunkRevealed;
            svc.OnMarkerAdded   -= OnMarkerAdded;
            svc.OnMarkerRemoved -= OnMarkerRemoved;
        }
        if (terrainMaterial != null) Destroy(terrainMaterial);
        if (beamMaterial != null) Destroy(beamMaterial);
        if (beamMesh != null) Destroy(beamMesh);
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            SetVisible(!visible);
    }

    /// <summary>
    /// Run transform updates in LateUpdate so they read the player's interpolated
    /// position (after physics + animation), avoiding stutter when the player is
    /// driven by a Rigidbody on FixedUpdate.
    /// </summary>
    private void LateUpdate()
    {
        SampleDiscovery();
        if (!visible) return;
        UpdateRootTransform();
        UpdatePlayerMarker();
        UpdateMarkerPositions();
        UpdateBeamTransform();
        UpdateMaterialUniforms();
    }

    private void SampleDiscovery()
    {
        if (!enableFogOfWar) return;
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            if (player == null) return;
        }

        Vector3 pos = player.position;
        if (!hasDiscoverySample)
        {
            AddDiscoveryPoint(pos);
            lastDiscoverySamplePos = pos;
            hasDiscoverySample = true;
            return;
        }

        float dx = pos.x - lastDiscoverySamplePos.x;
        float dz = pos.z - lastDiscoverySamplePos.z;
        float spacing = Mathf.Max(0.01f, discoveryPointSpacing);
        if (dx * dx + dz * dz >= spacing * spacing)
        {
            AddDiscoveryPoint(pos);
            lastDiscoverySamplePos = pos;
        }
    }

    private void AddDiscoveryPoint(Vector3 worldPos)
    {
        int cap = Mathf.Clamp(maxDiscoveryPoints, 1, discoveryUploadBuffer.Length);
        var v = new Vector4(worldPos.x, 0f, worldPos.z, 0f);
        if (discoveryPoints.Count >= cap)
            discoveryPoints.RemoveAt(0);
        discoveryPoints.Add(v);
    }

    public void SetVisible(bool v)
    {
        visible = v;
        if (root != null) root.gameObject.SetActive(v);
        if (beamObject != null) beamObject.SetActive(v && showBeam);
        if (v) visibleSinceTime = Time.time;

        // Temporarily disable the player's glass-lens chromatic distortion while
        // the map is open to prevent red/blue fringes from forming around the
        // hologram's edges as the camera moves.
        if (disableGlassDistortionWhileOpen)
            GlassDistortionRenderFeature.RuntimeEnabled = !v;
    }

    private void OnDisable()
    {
        // Make sure we re-enable distortion if the map component is disabled mid-session.
        if (disableGlassDistortionWhileOpen)
            GlassDistortionRenderFeature.RuntimeEnabled = true;
    }

    // ─────────────────────────────────────────────
    //  Build
    // ─────────────────────────────────────────────

    private void BuildRoot()
    {
        var rootGo = new GameObject("MapHologram_Root");
        rootGo.transform.SetParent(transform, false);
        root = rootGo.transform;

        var terrainGo = new GameObject("Terrain");
        terrainGo.transform.SetParent(root, false);
        terrainContainer = terrainGo.transform;

        var markersGo = new GameObject("Markers");
        markersGo.transform.SetParent(root, false);
        markerContainer = markersGo.transform;

        var shader = Shader.Find("Hologram/Terrain");
        if (shader != null)
        {
            terrainMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
        else
        {
            Debug.LogError("[MapHologramTerrain] Shader 'Hologram/Terrain' not found.", this);
        }
    }

    private Mesh beamMesh;
    private int beamMeshRayCount;
    private float beamMeshBuiltWidth;

    private void BuildBeam()
    {
        beamObject = new GameObject("MapHologram_Beam");
        beamObject.transform.SetParent(transform, false);

        beamMeshFilter = beamObject.AddComponent<MeshFilter>();
        var renderer = beamObject.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        BuildSunrayFanMesh();

        var shader = Shader.Find("Hologram/Beam");
        if (shader != null)
        {
            beamMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            beamMaterial.SetColor("_Color", hologramTint);
            renderer.sharedMaterial = beamMaterial;
        }
        else
        {
            Debug.LogError("[MapHologramTerrain] Shader 'Hologram/Beam' not found.", this);
        }
    }

    /// <summary>
    /// Builds a fan of N flat quads radiating from the apex. Each quad is
    /// 1 ray: a thin trapezoid with apex narrow and base wide. UV.x = across
    /// width (0..1), UV.y = apex (0) → base (1). Vertex color R encodes
    /// the per-ray index normalized to 0..1, so the shader can derive a
    /// stable per-ray seed for shimmer.
    /// </summary>
    private void BuildSunrayFanMesh()
    {
        int rays = Mathf.Max(3, sunrayCount);
        // Each quad has 4 verts and 2 tris.
        var verts  = new Vector3[rays * 4];
        var uvs    = new Vector2[rays * 4];
        var colors = new Color32[rays * 4];
        var tris   = new int[rays * 6];

        float baseHalfWidth = Mathf.Max(0.001f, sunrayBaseWidth) * 0.5f;
        // Apex narrows to a point but with a tiny width so rasterization
        // doesn't collapse the triangle to nothing.
        float apexHalfWidth = baseHalfWidth * 0.05f;

        for (int r = 0; r < rays; r++)
        {
            float angle = (r / (float)rays) * Mathf.PI * 2f;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            // Local axes for this ray:
            //   forward = (c, s, 0) — radial outward in XY plane
            //   right   = (-s, c, 0) — perpendicular (across width)
            // The cone's length axis is +Z, but a flat fan doesn't have any
            // Z extent — so we lay each ray in a plane perpendicular to the
            // cone's axis is wrong. Instead, the ray is a quad lying in the
            // plane spanned by Z (length, apex→base) and the radial outward
            // direction (c, s, 0) at base. At apex, length=0, so the quad
            // tapers to the apex point.
            //
            // Vertices in local cone space (apex=z=0, base=z=1):
            //   apex-left, apex-right, base-left, base-right
            // The "left/right" is in the radial-tangent direction, scaled by
            // half-width × radial offset (so the quad widens as it leaves apex).
            float ax = -s, ay = c, az = 0f;        // tangent at angle
            // Apex-left and apex-right: same point at origin, slight offset for
            // non-zero rasterization.
            int v0 = r * 4;
            verts[v0 + 0] = new Vector3(ax * apexHalfWidth, ay * apexHalfWidth, 0f);
            verts[v0 + 1] = new Vector3(-ax * apexHalfWidth, -ay * apexHalfWidth, 0f);
            // Base-left and base-right: at radial distance 1 from axis, offset
            // by ±baseHalfWidth in the tangent direction.
            verts[v0 + 2] = new Vector3(c + ax * baseHalfWidth, s + ay * baseHalfWidth, 1f);
            verts[v0 + 3] = new Vector3(c - ax * baseHalfWidth, s - ay * baseHalfWidth, 1f);

            uvs[v0 + 0] = new Vector2(0f, 0f);
            uvs[v0 + 1] = new Vector2(1f, 0f);
            uvs[v0 + 2] = new Vector2(0f, 1f);
            uvs[v0 + 3] = new Vector2(1f, 1f);

            // Per-ray seed in vertex.color.r: index/count gives a unique stable
            // value 0..1 the shader can hash for independent shimmer.
            byte seed = (byte)Mathf.Min(255, Mathf.RoundToInt(((r + 0.5f) / rays) * 255f));
            colors[v0 + 0] = new Color32(seed, 0, 0, 255);
            colors[v0 + 1] = new Color32(seed, 0, 0, 255);
            colors[v0 + 2] = new Color32(seed, 0, 0, 255);
            colors[v0 + 3] = new Color32(seed, 0, 0, 255);

            int t0 = r * 6;
            tris[t0 + 0] = v0 + 0; tris[t0 + 1] = v0 + 2; tris[t0 + 2] = v0 + 3;
            tris[t0 + 3] = v0 + 0; tris[t0 + 4] = v0 + 3; tris[t0 + 5] = v0 + 1;
        }

        if (beamMesh == null)
        {
            beamMesh = new Mesh { name = "HologramSunrayFan", hideFlags = HideFlags.DontSave };
            beamMeshFilter.sharedMesh = beamMesh;
        }
        beamMesh.Clear();
        beamMesh.vertices  = verts;
        beamMesh.uv        = uvs;
        beamMesh.colors32  = colors;
        beamMesh.triangles = tris;
        beamMesh.RecalculateBounds();
        beamMeshRayCount = rays;
        beamMeshBuiltWidth = sunrayBaseWidth;
    }

    private void BuildPlayerMarker()
    {
        playerMarker = BuildTriangleMarkerVisual("PlayerMarker", new Color(1.00f, 0.55f, 0.10f, 1f), playerIntensity, pulse: 0.25f);
    }

    /// <summary>
    /// Flat triangular arrow marker pointing along +Z (forward), so it rotates
    /// with the player's yaw to indicate facing direction.
    /// </summary>
    private GameObject BuildTriangleMarkerVisual(string name, Color color, float intensity, float pulse = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(markerContainer, false);

        var mat = MakeSolidMarkerMaterial(color, intensity, pulse);

        var tri = new GameObject("Arrow");
        tri.transform.SetParent(go.transform, false);

        var mesh = new Mesh { name = "PlayerArrow" };
        // Arrow lies in the XZ plane, tip pointing +Z. Elongated arrowhead +
        // narrow shaft + tail notch so the forward direction reads at a glance.
        Vector3 tip       = new Vector3( 0.00f, 0.0f,  1.20f);
        Vector3 headLeft  = new Vector3(-0.55f, 0.0f,  0.10f);
        Vector3 headRight = new Vector3( 0.55f, 0.0f,  0.10f);
        Vector3 neckLeft  = new Vector3(-0.18f, 0.0f,  0.10f);
        Vector3 neckRight = new Vector3( 0.18f, 0.0f,  0.10f);
        Vector3 tailLeft  = new Vector3(-0.18f, 0.0f, -0.55f);
        Vector3 tailRight = new Vector3( 0.18f, 0.0f, -0.55f);
        Vector3 tailNotch = new Vector3( 0.00f, 0.0f, -0.35f); // concave back edge
        mesh.vertices = new[]
        {
            tip,        // 0
            headLeft,   // 1
            headRight,  // 2
            neckLeft,   // 3
            neckRight,  // 4
            tailLeft,   // 5
            tailRight,  // 6
            tailNotch,  // 7
        };
        mesh.triangles = new[]
        {
            // Arrowhead (front, double-sided)
            0, 2, 1,
            0, 1, 2,
            // Shaft left half
            3, 5, 7,
            3, 7, 5,
            // Shaft right half
            4, 7, 6,
            4, 6, 7,
            // Bridge between shaft tops
            3, 4, 7,
            3, 7, 4,
        };
        mesh.normals = new[]
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            Vector3.up, Vector3.up, Vector3.up, Vector3.up,
        };
        mesh.RecalculateBounds();

        var mf = tri.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = tri.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.sharedMaterial = mat;

        // Lift the arrow slightly above the terrain so it doesn't z-fight.
        tri.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        return go;
    }

    /// <summary>
    /// Builds a sci-fi map marker: a thin vertical stalk anchored on a flat
    /// diamond at terrain level, with a downward-pointing pin head at the top
    /// and a ground ring around the base. Optional billboarded text label is
    /// spawned as a child so callers can position/scale it independently.
    /// All geometry uses the additive `Hologram/Solid` shader.
    /// </summary>
    private GameObject BuildMarkerVisual(string name, Color color, float intensity, string label = null, float pulse = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(markerContainer, false);

        var mat = MakeSolidMarkerMaterial(color, intensity, pulse);

        // 1) Ground diamond (flat octahedron) — sits flush with the terrain.
        var basePin = new GameObject("BasePin");
        basePin.transform.SetParent(go.transform, false);
        AttachMesh(basePin, BuildDiamondMesh(0.55f, 0.12f), mat);

        // 2) Stalk — thin tall quad-tube from terrain up to the pin head.
        float stalkH = Mathf.Max(0.001f, markerSpikeHeight / Mathf.Max(0.0001f, markerSize));
        float stalkW = Mathf.Max(0.0001f, markerSpikeWidth / Mathf.Max(0.0001f, markerSize));
        var stalk = new GameObject("Stalk");
        stalk.transform.SetParent(go.transform, false);
        AttachMesh(stalk, BuildBoxMesh(stalkW, stalkH, stalkW), mat);
        stalk.transform.localPosition = new Vector3(0f, stalkH * 0.5f, 0f);

        // 3) Pin head — inverted teardrop / downward diamond at the top of the stalk.
        var head = new GameObject("Head");
        head.transform.SetParent(go.transform, false);
        AttachMesh(head, BuildDiamondMesh(0.55f, 0.55f), mat);
        head.transform.localPosition = new Vector3(0f, stalkH + 0.45f, 0f);

        // 4) Ground ring — flat torus to draw attention to the marker's footprint.
        var ring = new GameObject("Ring");
        ring.transform.SetParent(go.transform, false);
        AttachMesh(ring, BuildRingMesh(1.0f, 0.85f, 32), mat);
        ring.transform.localPosition = new Vector3(0f, 0.005f, 0f);

        return go;
    }

    /// <summary>
    /// Builds a world-space label as a sibling of the marker (not a child), so
    /// it doesn't inherit the marker's non-uniform vertical-exaggeration scale.
    /// Uses Unity's legacy `TextMesh` because runtime TMP setups in this project
    /// render fallback glyphs ("two T's") regardless of how we bind the font —
    /// `TextMesh` ships with a built-in font and is reliable.
    /// Position + uniform scale + camera billboarding are handled per-frame.
    /// </summary>
    private GameObject BuildMarkerLabel(string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(markerContainer, false);

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 64;          // raster resolution; visual size comes from characterSize × transform scale
        // characterSize directly controls visual size in label-local units, so we
        // route the inspector's font-size knob through it. Scale by 1/64 so a
        // value of 64 in the inspector gives roughly the old default.
        tm.characterSize = Mathf.Max(0.001f, markerLabelFontSize / 64f);
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = markerLabelColor;
        tm.fontStyle = FontStyle.Bold;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // TextMesh uses the Font's shared material by default. To tint it
            // per-label without mutating the shared asset (which would tint
            // every TextMesh in the project), give this renderer its own
            // material instance and a high render queue so it sits over the
            // additive hologram.
            if (tm.font != null && tm.font.material != null)
            {
                var matInstance = new Material(tm.font.material) { hideFlags = HideFlags.DontSave };
                matInstance.color = markerLabelColor;
                matInstance.renderQueue = 4001;
                mr.sharedMaterial = matInstance;
            }
        }
        return go;
    }

    private void AttachMesh(GameObject go, Mesh mesh, Material mat)
    {
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.sharedMaterial = mat;
    }

    /// <summary>Octahedral "diamond" mesh: 4 horizontal points + top + bottom apex.</summary>
    private static Mesh BuildDiamondMesh(float radius, float halfHeight)
    {
        var verts = new[]
        {
            new Vector3(0f,  halfHeight, 0f),       // 0 top
            new Vector3(0f, -halfHeight, 0f),       // 1 bottom
            new Vector3( radius, 0f,  0f),          // 2 +x
            new Vector3( 0f,    0f,  radius),       // 3 +z
            new Vector3(-radius, 0f,  0f),          // 4 -x
            new Vector3( 0f,    0f, -radius),       // 5 -z
        };
        var tris = new[]
        {
            // Upper pyramid (double-sided so additive shader looks right from any angle)
            0,2,3, 0,3,2,
            0,3,4, 0,4,3,
            0,4,5, 0,5,4,
            0,5,2, 0,2,5,
            // Lower pyramid
            1,3,2, 1,2,3,
            1,4,3, 1,3,4,
            1,5,4, 1,4,5,
            1,2,5, 1,5,2,
        };
        var mesh = new Mesh { name = "MarkerDiamond" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Axis-aligned box mesh, centered on origin.</summary>
    private static Mesh BuildBoxMesh(float w, float h, float d)
    {
        float x = w * 0.5f, y = h * 0.5f, z = d * 0.5f;
        var verts = new[]
        {
            new Vector3(-x,-y,-z), new Vector3( x,-y,-z),
            new Vector3( x, y,-z), new Vector3(-x, y,-z),
            new Vector3(-x,-y, z), new Vector3( x,-y, z),
            new Vector3( x, y, z), new Vector3(-x, y, z),
        };
        var tris = new[]
        {
            0,2,1, 0,3,2,    // back
            4,5,6, 4,6,7,    // front
            0,1,5, 0,5,4,    // bottom
            3,7,6, 3,6,2,    // top
            0,4,7, 0,7,3,    // left
            1,2,6, 1,6,5,    // right
        };
        var mesh = new Mesh { name = "MarkerBox" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat ring on the XZ plane. outerR > innerR. Double-sided.</summary>
    private static Mesh BuildRingMesh(float outerR, float innerR, int segments)
    {
        segments = Mathf.Max(8, segments);
        var verts = new Vector3[segments * 2];
        var tris = new int[segments * 12]; // 2 quads (front+back) per segment
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2 + 0] = new Vector3(c * outerR, 0f, s * outerR);
            verts[i * 2 + 1] = new Vector3(c * innerR, 0f, s * innerR);
        }
        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int o0 = i * 2, i0 = i * 2 + 1;
            int o1 = next * 2, i1 = next * 2 + 1;
            // top
            tris[t++] = o0; tris[t++] = o1; tris[t++] = i1;
            tris[t++] = o0; tris[t++] = i1; tris[t++] = i0;
            // bottom (flipped winding)
            tris[t++] = o0; tris[t++] = i1; tris[t++] = o1;
            tris[t++] = o0; tris[t++] = i0; tris[t++] = i1;
        }
        var mesh = new Mesh { name = "MarkerRing" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ApplyMaterialAndStrip(GameObject go, Material mat)
    {
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.sharedMaterial = mat;
    }

    private Material MakeSolidMarkerMaterial(Color color, float intensity, float pulse)
    {
        var shader = Shader.Find("Hologram/Solid");
        if (shader == null)
        {
            Debug.LogError("[MapHologramTerrain] Shader 'Hologram/Solid' not found.", this);
            return null;
        }
        var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        mat.SetColor("_Color", color);
        mat.SetFloat("_Intensity", intensity);
        mat.SetFloat("_Pulse", pulse);
        mat.SetFloat("_PulseSpeed", 2.0f);
        return mat;
    }

    // ─────────────────────────────────────────────
    //  MapService integration
    // ─────────────────────────────────────────────

    private void SubscribeToMapService()
    {
        var svc = MapService.Instance;

        // Marker subscription is independent of chunk-reveal mode: we want POIs
        // to render whether or not we're in the all-chunks-visible fallback.
        if (svc != null)
        {
            svc.OnMarkerAdded   += OnMarkerAdded;
            svc.OnMarkerRemoved += OnMarkerRemoved;
            foreach (var m in svc.Markers) OnMarkerAdded(m);
        }

        if (revealAllChunks || svc == null)
        {
            // Spawn every chunk regardless of reveal state.
            if (config.chunks != null)
            {
                foreach (var c in config.chunks) OnChunkRevealed(c.gridCoord);
            }
            if (svc == null)
                Debug.LogWarning("[MapHologramTerrain] No MapService in scene — running with all chunks visible. Add a MapService component to persistentScene to enable POIs and reveal-on-explore.", this);
        }
        else
        {
            svc.OnChunkRevealed += OnChunkRevealed;
            foreach (var c in svc.RevealedChunks) OnChunkRevealed(c);
        }

        Debug.Log($"[MapHologramTerrain] Spawned {chunkMeshes.Count} chunk mesh(es) at startup. " +
                  $"(config has {config.chunks?.Length ?? 0} chunks total)");
    }

    private void OnChunkRevealed(Vector2Int coord)
    {
        if (chunkMeshes.ContainsKey(coord)) return;
        var mesh = Resources.Load<Mesh>($"MapMeshes/MapMesh_{coord.x}_{coord.y}");
        if (mesh == null)
        {
            Debug.LogWarning($"[MapHologramTerrain] Missing baked mesh: Resources/MapMeshes/MapMesh_{coord.x}_{coord.y}.asset — run Tools → World Streaming → Bake Map Meshes.", this);
            return;
        }

        var go = new GameObject($"Chunk_{coord.x}_{coord.y}", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(terrainContainer, false);
        go.transform.localPosition = new Vector3(coord.x * config.chunkSize.x, 0f, coord.y * config.chunkSize.y);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.sharedMaterial = terrainMaterial;

        // Tell the shader which simulated world XZ this chunk represents, so fog
        // discovery distance compares against the player's real game-world position
        // instead of the tiny hologram render-world position.
        var mpb = new MaterialPropertyBlock();
        mpb.SetVector("_ChunkWorldOriginXZ", new Vector4(
            config.worldOrigin.x + coord.x * config.chunkSize.x,
            config.worldOrigin.z + coord.y * config.chunkSize.y,
            0f, 0f));
        r.SetPropertyBlock(mpb);

        chunkMeshes[coord] = go;
        EnsureLayer(go);

        // Track min/max heights across revealed chunks for shader tint range.
        var b = mesh.bounds;
        if (chunkMeshes.Count == 1)
        {
            MinTerrainY = b.min.y;
            MaxTerrainY = b.max.y;
        }
        else
        {
            MinTerrainY = Mathf.Min(MinTerrainY, b.min.y);
            MaxTerrainY = Mathf.Max(MaxTerrainY, b.max.y);
        }
    }

    private void OnMarkerAdded(MapService.Marker marker)
    {
        if (markerVisuals.ContainsKey(marker)) return;
        var go = BuildMarkerVisual($"Marker_{marker.label ?? marker.type.ToString()}",
                                   MapMarkerColors.For(marker.type),
                                   markerIntensity,
                                   label: marker.label,
                                   pulse: 0.15f);
        markerVisuals[marker] = go;
        EnsureLayer(go);

        if (showMarkerLabels && !string.IsNullOrEmpty(marker.label))
        {
            var labelGo = BuildMarkerLabel(marker.label);
            markerLabels[marker] = labelGo;
            EnsureLayer(labelGo);
        }
    }

    private void OnMarkerRemoved(MapService.Marker marker)
    {
        if (!markerVisuals.TryGetValue(marker, out var go)) return;
        markerVisuals.Remove(marker);
        if (go != null) Destroy(go);

        if (markerLabels.TryGetValue(marker, out var labelGo))
        {
            markerLabels.Remove(marker);
            if (labelGo != null) Destroy(labelGo);
        }
    }

    // ─────────────────────────────────────────────
    //  Update
    // ─────────────────────────────────────────────

    private void UpdateRootTransform()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            if (player == null) return;
        }

        // Anchor: prefer the live camera so the hologram tracks the screen
        // (yaw + pitch). projectorAnchor / player are fallbacks if no camera
        // resolves.
        Transform anchor = (Camera.main != null && Camera.main.isActiveAndEnabled)
            ? Camera.main.transform
            : (projectorAnchor != null ? projectorAnchor : player);

        Vector3 anchorPos = anchor.position;
        Vector3 anchorFwd = anchor.forward;
        Vector3 anchorRight = anchor.right;
        Vector3 anchorUp = anchor.up;

        Vector3 worldPos = anchorPos + anchorFwd * distance + anchorRight * sideOffset + anchorUp * height;

        float t = Mathf.Clamp01((Time.time - visibleSinceTime) / Mathf.Max(0.0001f, spawnRiseTime));
        float rise = Mathf.SmoothStep(0f, 1f, t);
        currentRise = rise;

        root.position = worldPos - Vector3.up * (1f - rise) * 0.35f;

        // Orientation: lean direction always tracks the camera (so the
        // top of the map always faces you — readable from any direction).
        // The map's "north" axis is built from a WORLD-FIXED reference
        // (world +Z) projected onto the tilted plane. As you turn, the
        // projection of world-north onto the always-toward-you plane
        // rotates around the surface normal — visually, the terrain
        // content spins around the map's own axis while the tilt stays
        // pointed at you.
        Vector3 viewFwd = anchorFwd; viewFwd.y = 0f;
        if (viewFwd.sqrMagnitude < 1e-6f)
        {
            viewFwd = player.forward; viewFwd.y = 0f;
            if (viewFwd.sqrMagnitude < 1e-6f) viewFwd = Vector3.forward;
        }
        viewFwd.Normalize();

        Vector3 leanDir = -viewFwd;
        Vector3 leanAxis = Vector3.Cross(Vector3.up, leanDir);
        Vector3 modelUp = Quaternion.AngleAxis(leanTowardPlayer, leanAxis) * Vector3.up;

        Vector3 modelFwd = Vector3.ProjectOnPlane(Vector3.forward, modelUp);
        if (modelFwd.sqrMagnitude < 1e-6f)
            modelFwd = Vector3.ProjectOnPlane(Vector3.right, modelUp);
        modelFwd.Normalize();
        Quaternion baseRot = Quaternion.LookRotation(modelFwd, modelUp);

        // Small wobble + readability twist applied in the model's local frame.
        float wobX = Mathf.Sin(Time.time * wobbleSpeed * Mathf.PI * 2f) * wobbleAmplitudeDeg;
        float wobZ = Mathf.Sin(Time.time * wobbleSpeed * Mathf.PI * 2f * 0.73f) * wobbleAmplitudeDeg * 0.6f;
        Quaternion localTweak = Quaternion.Euler(wobX, yawTowardPlayer, wobZ);

        root.rotation = baseRot * localTweak;

        // Visible chunk window — chunk coords are integer (used for show/hide),
        // but visCenter uses the player's continuous world position so the
        // hologram pans smoothly instead of snapping at chunk boundaries.
        Vector2Int visMin, visMax;
        Vector2 visCenter;
        Vector2 visSize;
        if (centerOnPlayer)
        {
            var pChunk = config.WorldToChunkCoord(player.position);
            visMin = new Vector2Int(pChunk.x - viewRadius,     pChunk.y - viewRadius);
            visMax = new Vector2Int(pChunk.x + viewRadius + 1, pChunk.y + viewRadius + 1);

            // Continuous center in container space (= world XZ minus worldOrigin).
            visCenter = new Vector2(
                player.position.x - config.worldOrigin.x,
                player.position.z - config.worldOrigin.z);
            visSize = new Vector2(
                (visMax.x - visMin.x) * config.chunkSize.x,
                (visMax.y - visMin.y) * config.chunkSize.y);
        }
        else
        {
            visMin = Vector2Int.zero;
            visMax = config.gridDimensions;
            Vector2 worldMin = Vector2.zero;
            Vector2 worldMax = new Vector2(visMax.x * config.chunkSize.x, visMax.y * config.chunkSize.y);
            visCenter = (worldMin + worldMax) * 0.5f;
            visSize   = worldMax - worldMin;
        }

        // Scale: fit visible area into footprint.
        float maxDim = Mathf.Max(visSize.x, visSize.y);
        float xz = footprint / Mathf.Max(0.0001f, maxDim);
        float y  = xz * verticalExaggeration;
        Vector3 scale = new Vector3(xz, y, xz) * rise;
        terrainContainer.localScale = scale;
        markerContainer.localScale  = scale;

        // Center the visible window on root.origin. Chunk meshes sit at
        // (coord * chunkSize) in container space (same units as visCenter), so
        // no worldOrigin subtraction needed here.
        Vector3 center = new Vector3(
            -visCenter.x * scale.x,
            -((MinTerrainY + MaxTerrainY) * 0.5f) * scale.y,
            -visCenter.y * scale.z);
        terrainContainer.localPosition = center;
        markerContainer.localPosition  = center;

        // Show/hide chunks based on the visible window.
        foreach (var kvp in chunkMeshes)
        {
            var c = kvp.Key;
            bool inWindow = c.x >= visMin.x && c.x < visMax.x
                         && c.y >= visMin.y && c.y < visMax.y;
            if (kvp.Value.activeSelf != inWindow)
                kvp.Value.SetActive(inWindow);
        }
    }

    private void ApplyHologramLayer()
    {
        if (string.IsNullOrEmpty(hologramLayerName)) { hologramLayer = -1; return; }
        hologramLayer = LayerMask.NameToLayer(hologramLayerName);
        if (hologramLayer < 0)
        {
            Debug.LogWarning($"[MapHologramTerrain] Layer '{hologramLayerName}' does not exist. Create it via Edit → Project Settings → Tags & Layers, then exclude it from helmet/screen-space camera culling masks to stop bleed-through.", this);
            return;
        }
        if (root != null) SetLayerRecursively(root.gameObject, hologramLayer);
        if (beamObject != null) SetLayerRecursively(beamObject, hologramLayer);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }

    private void EnsureLayer(GameObject go)
    {
        if (hologramLayer >= 0) SetLayerRecursively(go, hologramLayer);
    }

    private Vector3 InverseContainerScale(float worldSize)
    {
        // Returns a localScale that produces a uniform `worldSize`-sized cube
        // even when the container has non-uniform scale (e.g. vertical exaggeration).
        var s = terrainContainer.localScale;
        return new Vector3(
            worldSize / Mathf.Max(MinScaleEpsilon, s.x),
            worldSize / Mathf.Max(MinScaleEpsilon, s.y),
            worldSize / Mathf.Max(MinScaleEpsilon, s.z));
    }

    private const float MinScaleEpsilon = 1e-7f;

    private float currentRise;

    private bool ContainerReady()
    {
        var s = terrainContainer.localScale;
        // Defend against genuine zero/NaN, but allow legitimately small holograms.
        if (!float.IsFinite(s.x) || !float.IsFinite(s.y) || !float.IsFinite(s.z)) return false;
        if (s.x < MinScaleEpsilon || s.y < MinScaleEpsilon || s.z < MinScaleEpsilon) return false;
        // Hide while the rise-in animation hasn't really started.
        return currentRise > 0.05f;
    }

    private void UpdatePlayerMarker()
    {
        if (player == null || playerMarker == null) return;

        // During rise-in or initial frame, the container scale can be near-zero —
        // dividing by it produces Inf/NaN positions that render as giant streaks.
        if (!ContainerReady())
        {
            playerMarker.SetActive(false);
            return;
        }
        playerMarker.SetActive(true);

        var s = terrainContainer.localScale;
        playerMarker.transform.localPosition = WorldToTerrainLocal(player.position)
            + Vector3.up * (markerLift / s.y);
        playerMarker.transform.localScale = InverseContainerScale(playerMarkerSize);

        // Arrow direction = player's body yaw (world-flat forward). The marker
        // is nested under a hologram root that may be tilted toward the camera,
        // so we project the world-flat forward onto the model's terrain plane
        // (perpendicular to parent.up) before converting to local space —
        // otherwise camera pitch leaks into the arrow yaw.
        var parent = playerMarker.transform.parent;
        Vector3 bodyFwdWorld = player.forward; bodyFwdWorld.y = 0f;
        if (bodyFwdWorld.sqrMagnitude > 1e-6f)
        {
            bodyFwdWorld.Normalize();
            Vector3 onPlane = Vector3.ProjectOnPlane(bodyFwdWorld, parent.up);
            if (onPlane.sqrMagnitude > 1e-6f)
            {
                Vector3 localFwd = parent.InverseTransformDirection(onPlane.normalized);
                playerMarker.transform.localRotation = Quaternion.LookRotation(localFwd, Vector3.up);
            }
        }
    }

    private void UpdateMarkerPositions()
    {
        if (!ContainerReady())
        {
            foreach (var kvp in markerVisuals)
                if (kvp.Value != null) kvp.Value.SetActive(false);
            foreach (var kvp in markerLabels)
                if (kvp.Value != null) kvp.Value.SetActive(false);
            return;
        }

        var svc = MapService.Instance;
        var s = terrainContainer.localScale;
        foreach (var kvp in markerVisuals)
        {
            var marker = kvp.Key;
            var go = kvp.Value;
            if (go == null) continue;

            Vector3 worldPos = marker.GetWorldPosition();
            // Honor the marker's own opt-out (MapPOI's alwaysVisible sets this).
            // Falling through to the global toggle keeps the old behaviour for
            // markers that do require chunk reveal.
            bool gateOnReveal = showOnlyRevealedMarkers && marker.requiresRevealedChunk;
            bool show = !gateOnReveal
                || (svc != null && svc.IsChunkRevealed(config.WorldToChunkCoord(worldPos)));
            go.SetActive(show);
            if (!show) continue;

            go.transform.localPosition = WorldToTerrainLocal(worldPos)
                + Vector3.up * (markerLift / s.y);
            go.transform.localScale = InverseContainerScale(markerSize);

            // Update the separate label sibling: position above the marker in
            // terrain-local space, uniform world scale (so non-uniform vertical
            // exaggeration doesn't squish the text), and billboard toward the
            // camera each frame.
            if (markerLabels.TryGetValue(marker, out var labelGo) && labelGo != null)
            {
                labelGo.SetActive(true);
                Vector3 markerLocal = WorldToTerrainLocal(worldPos)
                    + Vector3.up * (markerLift / s.y);
                // Lift label above the pin head: stalk height (in marker-local) is
                // markerSpikeHeight/markerSize, plus the configured label offset.
                float liftMarkerLocal = (markerSpikeHeight / Mathf.Max(0.0001f, markerSize))
                                        + markerLabelHeight;
                // Convert that marker-local lift to terrain-local via scale ratio.
                float markerToTerrain = markerSize; // marker localScale is InverseContainerScale(markerSize)
                labelGo.transform.localPosition = markerLocal + Vector3.up * (liftMarkerLocal * markerToTerrain / s.y);

                // Uniform world scale: use the container's horizontal scale only
                // so vertical-exaggeration doesn't squish/stretch the text.
                float uniform = markerSize / Mathf.Max(MinScaleEpsilon, s.x);
                labelGo.transform.localScale = new Vector3(uniform, uniform, uniform);

                // Live-update font size + color from the inspector so tweaks take
                // effect without rebuilding the marker.
                var tm = labelGo.GetComponent<TextMesh>();
                if (tm != null)
                {
                    tm.characterSize = Mathf.Max(0.001f, markerLabelFontSize / 64f);
                    tm.color = markerLabelColor;
                    var lmr = labelGo.GetComponent<MeshRenderer>();
                    if (lmr != null && lmr.sharedMaterial != null)
                        lmr.sharedMaterial.color = markerLabelColor;
                }

                // Full billboard: face the camera straight-on, regardless of
                // camera tilt. The label's up axis tracks the camera's up, so
                // text is always readable head-on (side, top, anywhere).
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - labelGo.transform.position;
                    if (toCam.sqrMagnitude > 1e-6f)
                        labelGo.transform.rotation = Quaternion.LookRotation(-toCam.normalized, cam.transform.up);
                }
            }
        }

        // Hide labels for markers that aren't being shown (their parent marker
        // GO was set inactive above, but the label is a sibling).
        foreach (var kvp in markerLabels)
        {
            if (kvp.Value == null) continue;
            if (markerVisuals.TryGetValue(kvp.Key, out var mgo) && mgo != null)
                kvp.Value.SetActive(mgo.activeSelf);
        }
    }

    /// <summary>
    /// World position → local coordinates in the unscaled terrain space
    /// (i.e. the same space chunk meshes sit in).
    /// </summary>
    private Vector3 WorldToTerrainLocal(Vector3 worldPos)
    {
        return new Vector3(
            worldPos.x - config.worldOrigin.x,
            worldPos.y,
            worldPos.z - config.worldOrigin.z);
    }

    private void UpdateBeamTransform()
    {
        if (beamObject == null || !showBeam) return;

        // Rebuild the fan mesh if ray count or per-quad width changed.
        if (beamMeshRayCount != Mathf.Max(3, sunrayCount) ||
            !Mathf.Approximately(beamMeshBuiltWidth, sunrayBaseWidth))
            BuildSunrayFanMesh();

        Vector3 origin = helmetAnchor != null
            ? helmetAnchor.position
            : (player != null ? player.position + Vector3.up * helmetHeightFallback : transform.position);

        Vector3 toRoot = root.position - origin;
        float length = toRoot.magnitude;
        if (length < 0.001f) return;

        Vector3 dir = toRoot / length;
        Vector3 start = origin + dir * beamOriginOffset;
        float effectiveLen = Mathf.Max(0.01f, length - beamOriginOffset);
        float radius = footprint * beamRadiusFraction;

        // Roll the whole fan slowly around the cone's forward axis.
        float spinDeg = (Time.time * sunraySpinSpeed * 360f) % 360f;

        beamObject.transform.position = start;
        beamObject.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, spinDeg);
        beamObject.transform.localScale = new Vector3(radius, radius, effectiveLen);
    }

    private void UpdateMaterialUniforms()
    {
        if (terrainMaterial != null)
        {
            terrainMaterial.SetColor("_Color", hologramTint);
            terrainMaterial.SetColor("_LowColor", valleyTint);
            terrainMaterial.SetColor("_HighColor", peakTint);
            terrainMaterial.SetFloat("_Intensity", intensity);
            terrainMaterial.SetFloat("_MinY", MinTerrainY);
            terrainMaterial.SetFloat("_MaxY", MaxTerrainY);
            terrainMaterial.SetFloat("_ContourSpacing", contourSpacing);
            terrainMaterial.SetFloat("_ContourThickness", contourThickness);
            terrainMaterial.SetFloat("_GridSize", gridSpacing);
            terrainMaterial.SetFloat("_GridStrength", gridStrength);
            terrainMaterial.SetFloat("_Fresnel", fresnelPower);
            terrainMaterial.SetFloat("_FresnelStrength", fresnelStrength);

            // Fog of war
            terrainMaterial.SetFloat("_FogEnabled", enableFogOfWar ? 1f : 0f);
            terrainMaterial.SetColor("_FogColor", fogColor);
            terrainMaterial.SetFloat("_FogIntensity", fogIntensity);
            terrainMaterial.SetFloat("_FogNoiseScale", fogNoiseScale);
            terrainMaterial.SetFloat("_FogNoiseSpeed", fogNoiseSpeed);
            terrainMaterial.SetFloat("_FogSlopeDim", fogSlopeDim);
            terrainMaterial.SetFloat("_FogViewDim", fogViewDim);
            terrainMaterial.SetFloat("_DiscoveryRadius", Mathf.Max(0.01f, discoveryRadius));
            terrainMaterial.SetFloat("_DiscoveryFalloff", Mathf.Max(0.0001f, discoveryFalloff));

            int count = Mathf.Min(discoveryPoints.Count, discoveryUploadBuffer.Length);
            for (int i = 0; i < count; i++) discoveryUploadBuffer[i] = discoveryPoints[i];
            // Zero the rest so stale entries don't reveal anything if count shrinks.
            for (int i = count; i < discoveryUploadBuffer.Length; i++) discoveryUploadBuffer[i] = Vector4.zero;
            terrainMaterial.SetVectorArray("_DiscoveryPoints", discoveryUploadBuffer);
            terrainMaterial.SetInt("_DiscoveryCount", count);

            // Round map vignette centered on the player's sim-world XZ.
            Vector3 centerWorld = player != null ? player.position : Vector3.zero;
            terrainMaterial.SetVector("_MapCenterXZ", new Vector4(centerWorld.x, centerWorld.z, 0f, 0f));
            terrainMaterial.SetFloat("_MapRadius", Mathf.Max(0.01f, mapRadius));
            terrainMaterial.SetFloat("_MapEdgeFalloff", Mathf.Max(0.0001f, mapEdgeFalloff));
        }
        if (beamMaterial != null)
        {
            beamMaterial.SetColor("_Color", hologramTint);
            beamMaterial.SetFloat("_Intensity", intensity);
            beamMaterial.SetFloat("_ApexFade", sunrayApexAlpha);
            beamMaterial.SetFloat("_BaseFade", sunrayBaseAlpha);
            beamMaterial.SetFloat("_EdgeSharpness", sunrayEdgeSharpness);
            beamMaterial.SetFloat("_Shimmer", sunrayShimmer);
            beamMaterial.SetFloat("_ShimmerSpeed", sunrayShimmerSpeed);
        }
    }
}
