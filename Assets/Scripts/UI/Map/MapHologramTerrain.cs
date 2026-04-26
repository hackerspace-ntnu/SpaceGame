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

    [Header("Placement (relative to player)")]
    [SerializeField] private Transform helmetAnchor;
    [SerializeField] private float helmetHeightFallback = 1.7f;
    [SerializeField] private float distance = 0.9f;
    [SerializeField] private float sideOffset = -0.55f;
    [SerializeField] private float height = 1.25f;
    [Range(-90, 90)]
    [SerializeField] private float yawTowardPlayer = 25f;
    [Range(-30, 30)]
    [SerializeField] private float baseTiltX = 0f;

    [Header("Hologram Scale")]
    [Tooltip("Width of the hologram footprint in meters.")]
    [SerializeField] private float footprint = 0.55f;
    [Tooltip("Vertical exaggeration. 1 = real proportions, <1 flattens. Lower = more readable, higher = dramatic peaks.")]
    [Range(0.05f, 10f)]
    [SerializeField] private float verticalExaggeration = 0.6f;
    [Tooltip("How many chunks around the player are shown. 1 = 3x3 area, 2 = 5x5, etc. Set very high to show the whole world.")]
    [Range(0, 12)]
    [SerializeField] private int viewRadius = 2;
    [Tooltip("If on, the hologram recenters on the player's current chunk each frame (mini-map style).")]
    [SerializeField] private bool centerOnPlayer = true;

    [Header("Hologram Look")]
    [SerializeField] private Color hologramTint = new Color(0.45f, 0.95f, 1.00f, 1f);
    [SerializeField] private Color valleyTint = new Color(0.10f, 0.45f, 0.65f, 1f);
    [SerializeField] private Color peakTint = new Color(0.85f, 1.00f, 1.00f, 1f);
    [Range(0, 5)] [SerializeField] private float intensity = 1.4f;
    [SerializeField] private float contourSpacing = 20f;
    [Range(0, 0.5f)] [SerializeField] private float contourThickness = 0.08f;
    [SerializeField] private float gridSpacing = 32f;
    [Range(0, 2)] [SerializeField] private float gridStrength = 0.4f;
    [Range(0, 8)] [SerializeField] private float fresnelPower = 2.5f;
    [Range(0, 4)] [SerializeField] private float fresnelStrength = 1.6f;

    [Header("Volumetric Beam")]
    [SerializeField] private bool showBeam = true;
    [Range(16, 96)] [SerializeField] private int beamSides = 48;
    [Tooltip("Beam base radius as a fraction of the hologram footprint. 0.5 = exactly cover the map width, 0.55 = slightly larger, 0.4 = inside the map.")]
    [Range(0.2f, 0.9f)]
    [SerializeField] private float beamRadiusFraction = 0.55f;
    [Range(0, 1)] [SerializeField] private float beamApexAlpha = 0.95f;
    [Range(0, 1)] [SerializeField] private float beamBaseAlpha = 0.20f;
    [SerializeField] private float beamOriginOffset = 0.05f;
    [Range(0, 32)] [SerializeField] private float beamRayCount = 14f;
    [Range(0, 1)] [SerializeField] private float beamRayStrength = 0.65f;
    [Range(1, 16)] [SerializeField] private float beamRaySharpness = 6f;
    [SerializeField] private float beamRayDrift = 0.12f;

    [Header("Layer (helps exclude from helmet/screen shaders)")]
    [Tooltip("All hologram visuals are placed on this layer at startup. Create a layer named 'Hologram' (Edit → Project Settings → Tags & Layers) and exclude it from helmet/screen-space shader cameras to stop bleed-through.")]
    [SerializeField] private string hologramLayerName = "Hologram";

    [Header("Markers")]
    [SerializeField] private float playerMarkerSize = 0.025f;
    [SerializeField] private float markerSize = 0.018f;
    [SerializeField] private float markerLift = 0.04f;
    [Tooltip("A vertical pillar/spike above each marker so it's visible against the terrain. 0 = off.")]
    [SerializeField] private float markerSpikeHeight = 0.06f;
    [SerializeField] private float markerSpikeWidth = 0.005f;
    [Range(0, 8)] [SerializeField] private float playerIntensity = 3.5f;
    [Range(0, 8)] [SerializeField] private float markerIntensity = 2.5f;
    [SerializeField] private bool showOnlyRevealedMarkers = true;
    [Tooltip("Spawn meshes for all chunks at startup, ignoring MapService reveal state. Useful for debugging or static maps.")]
    [SerializeField] private bool revealAllChunks;

    [Header("Animation")]
    [SerializeField] private float spawnRiseTime = 0.35f;
    [SerializeField] private float wobbleAmplitudeDeg = 0.8f;
    [SerializeField] private float wobbleSpeed = 0.4f;

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
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            SetVisible(!visible);

        if (!visible) return;

        UpdateRootTransform();
        UpdatePlayerMarker();
        UpdateMarkerPositions();
        UpdateBeamTransform();
        UpdateMaterialUniforms();
    }

    public void SetVisible(bool v)
    {
        visible = v;
        if (root != null) root.gameObject.SetActive(v);
        if (beamObject != null) beamObject.SetActive(v && showBeam);
        if (v) visibleSinceTime = Time.time;
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

        beamMeshFilter.sharedMesh = BuildUnitConeMesh(beamSides);

        var shader = Shader.Find("Hologram/Beam");
        if (shader != null)
        {
            beamMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            beamMaterial.SetColor("_Color", hologramTint);
            beamMaterial.SetFloat("_ApexFade", beamApexAlpha);
            beamMaterial.SetFloat("_BaseFade", beamBaseAlpha);
            renderer.sharedMaterial = beamMaterial;
        }
        else
        {
            Debug.LogError("[MapHologramTerrain] Shader 'Hologram/Beam' not found.", this);
        }
    }

    /// <summary>
    /// Unit cone: apex at (0,0,0), base disc at z=1 with radius 1.
    /// Side normals are perpendicular to the slant for fresnel softness.
    /// </summary>
    private static Mesh BuildUnitConeMesh(int sides)
    {
        var verts = new Vector3[1 + (sides + 1)];
        var norms = new Vector3[verts.Length];
        var uvs   = new Vector2[verts.Length];
        var tris  = new int[sides * 3];

        verts[0] = Vector3.zero;
        norms[0] = Vector3.back;
        uvs[0]   = new Vector2(0.5f, 0f);

        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[1 + i] = new Vector3(c, s, 1f);
            norms[1 + i] = new Vector3(c, s, 0f).normalized;
            uvs[1 + i]   = new Vector2((float)i / sides, 1f);
        }
        for (int i = 0; i < sides; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = 1 + i;
            tris[i * 3 + 2] = 1 + i + 1;
        }

        var mesh = new Mesh { name = "HologramBeamCone" };
        mesh.vertices  = verts;
        mesh.normals   = norms;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void BuildPlayerMarker()
    {
        playerMarker = BuildMarkerVisual("PlayerMarker", new Color(0.40f, 1.00f, 1.00f, 1f), playerIntensity, pulse: 0.25f);
    }

    /// <summary>
    /// Builds a marker visual: a small base cube + an upward spike, both using the
    /// solid additive hologram shader so they're visible from any angle.
    /// </summary>
    private GameObject BuildMarkerVisual(string name, Color color, float intensity, float pulse = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(markerContainer, false);

        var mat = MakeSolidMarkerMaterial(color, intensity, pulse);

        // Base cube
        var baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseCube.name = "Base";
        var bcol = baseCube.GetComponent<Collider>();
        if (bcol != null) Destroy(bcol);
        baseCube.transform.SetParent(go.transform, false);
        baseCube.transform.localScale = Vector3.one;
        ApplyMaterialAndStrip(baseCube, mat);

        // Vertical spike — thin tall cube above the base.
        if (markerSpikeHeight > 0.001f)
        {
            var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spike.name = "Spike";
            var scol = spike.GetComponent<Collider>();
            if (scol != null) Destroy(scol);
            spike.transform.SetParent(go.transform, false);
            // Width is relative to spike width as fraction of marker size,
            // height is the spike length above the base.
            float spikeRelW = markerSpikeWidth / Mathf.Max(0.0001f, markerSize);
            float spikeRelH = markerSpikeHeight / Mathf.Max(0.0001f, markerSize);
            spike.transform.localScale = new Vector3(spikeRelW, spikeRelH, spikeRelW);
            spike.transform.localPosition = new Vector3(0, spikeRelH * 0.5f + 0.5f, 0);
            ApplyMaterialAndStrip(spike, mat);
        }

        return go;
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

        if (revealAllChunks || svc == null)
        {
            // Spawn every chunk regardless of reveal state.
            if (config.chunks != null)
            {
                foreach (var c in config.chunks) OnChunkRevealed(c.gridCoord);
            }
            if (svc == null)
                Debug.LogWarning("[MapHologramTerrain] No MapService in scene — running with all chunks visible.", this);
        }
        else
        {
            svc.OnChunkRevealed += OnChunkRevealed;
            svc.OnMarkerAdded   += OnMarkerAdded;
            svc.OnMarkerRemoved += OnMarkerRemoved;

            foreach (var c in svc.RevealedChunks) OnChunkRevealed(c);
            foreach (var m in svc.Markers)        OnMarkerAdded(m);
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
                                   pulse: 0.15f);
        markerVisuals[marker] = go;
        EnsureLayer(go);
    }

    private void OnMarkerRemoved(MapService.Marker marker)
    {
        if (!markerVisuals.TryGetValue(marker, out var go)) return;
        markerVisuals.Remove(marker);
        if (go != null) Destroy(go);
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

        Vector3 fwd = player.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        Vector3 worldPos = player.position + fwd * distance + right * sideOffset + Vector3.up * height;

        float t = Mathf.Clamp01((Time.time - visibleSinceTime) / Mathf.Max(0.0001f, spawnRiseTime));
        float rise = Mathf.SmoothStep(0f, 1f, t);
        currentRise = rise;

        float wobX = Mathf.Sin(Time.time * wobbleSpeed * Mathf.PI * 2f) * wobbleAmplitudeDeg;
        float wobZ = Mathf.Sin(Time.time * wobbleSpeed * Mathf.PI * 2f * 0.73f) * wobbleAmplitudeDeg * 0.6f;

        root.position = worldPos - Vector3.up * (1f - rise) * 0.35f;
        root.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(baseTiltX + wobX, yawTowardPlayer, wobZ);

        // Determine the visible window — either the whole world or a chunk
        // radius around the player's current chunk.
        Vector2Int visMin, visMax;
        if (centerOnPlayer)
        {
            var pChunk = config.WorldToChunkCoord(player.position);
            visMin = new Vector2Int(pChunk.x - viewRadius,     pChunk.y - viewRadius);
            visMax = new Vector2Int(pChunk.x + viewRadius + 1, pChunk.y + viewRadius + 1);
        }
        else
        {
            visMin = Vector2Int.zero;
            visMax = config.gridDimensions;
        }

        Vector2 visWorldMin = new Vector2(visMin.x * config.chunkSize.x, visMin.y * config.chunkSize.y);
        Vector2 visWorldMax = new Vector2(visMax.x * config.chunkSize.x, visMax.y * config.chunkSize.y);
        Vector2 visSize = visWorldMax - visWorldMin;
        Vector2 visCenter = (visWorldMin + visWorldMax) * 0.5f;

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
        playerMarker.transform.localRotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
    }

    private void UpdateMarkerPositions()
    {
        if (!ContainerReady())
        {
            foreach (var kvp in markerVisuals)
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
            bool show = !showOnlyRevealedMarkers
                || (svc != null && svc.IsChunkRevealed(config.WorldToChunkCoord(worldPos)));
            go.SetActive(show);
            if (!show) continue;

            go.transform.localPosition = WorldToTerrainLocal(worldPos)
                + Vector3.up * (markerLift / s.y);
            go.transform.localScale = InverseContainerScale(markerSize);
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

        Vector3 origin = helmetAnchor != null
            ? helmetAnchor.position
            : (player != null ? player.position + Vector3.up * helmetHeightFallback : transform.position);

        Vector3 toRoot = root.position - origin;
        float length = toRoot.magnitude;
        if (length < 0.001f) return;

        Vector3 dir = toRoot / length;
        Vector3 start = origin + dir * beamOriginOffset;
        float effectiveLen = Mathf.Max(0.01f, length - beamOriginOffset);

        // Auto-fit cone base radius to the hologram footprint so rays land on the map.
        float radius = footprint * beamRadiusFraction;

        beamObject.transform.position = start;
        beamObject.transform.rotation = Quaternion.LookRotation(dir);
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
        }
        if (beamMaterial != null)
        {
            beamMaterial.SetColor("_Color", hologramTint);
            beamMaterial.SetFloat("_ApexFade", beamApexAlpha);
            beamMaterial.SetFloat("_BaseFade", beamBaseAlpha);
            beamMaterial.SetFloat("_RayCount", beamRayCount);
            beamMaterial.SetFloat("_RayStrength", beamRayStrength);
            beamMaterial.SetFloat("_RaySharpness", beamRaySharpness);
            beamMaterial.SetFloat("_RayDrift", beamRayDrift);
        }
    }
}
