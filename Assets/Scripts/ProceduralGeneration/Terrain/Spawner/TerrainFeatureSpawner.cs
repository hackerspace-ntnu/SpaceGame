using UnityEngine;

/// <summary>
/// Scene-level driver for the terrain-feature system — the terrain analogue of <c>CaveSpawner</c>.
/// The designer picks a <see cref="featureType"/>, defines the footprint and tunes the sliders;
/// the editor bakes the mesh(es) to assets and runtime instantiates them. Feature meshes contribute
/// to the shared world NavMesh via their <see cref="MeshCollider"/> — a multi-mesh feature spawns
/// one collider per sub-mesh, all feeding the one unified walkable surface.
/// </summary>
[DisallowMultipleComponent]
public class TerrainFeatureSpawner : MonoBehaviour
{
    [Header("Feature")]
    [Tooltip("Which procedural terrain feature this spawner builds. The editor warns if the chosen type has no implementation registered yet.")]
    [SerializeField] private TerrainFeatureType featureType = TerrainFeatureType.FlatPad;

    [Header("Footprint — box bound (area features)")]
    [Tooltip("Half-extents of the local-space box that bounds an area feature and seeds the polygon. The polygon defines the actual organic outline; this box is the starting rectangle and the meshing extent.")]
    [SerializeField] private Vector3 boxHalfExtents = new Vector3(40f, 25f, 40f);

    [Tooltip("How the area footprint outline is defined.\n• Polygon — hand-edit vertices with the Scene-view handles.\n• Noise — the outline is auto-generated from noise; no hand-editing.")]
    [SerializeField] private FootprintShape footprintShape = FootprintShape.Polygon;

    [Tooltip("Noise mode: footprint complexity. 1 = a clean circle. ~4 = a soft organic blob. " +
             "~7 = pronounced lobes. 10 = a wild, spiky, multi-armed silhouette. One dial drives " +
             "lobe strength, detail octaves, vertex count and directional asymmetry together.")]
    [Range(1f, 10f)] [SerializeField] private float footprintComplexity = 3f;

    [Tooltip("The closed polygon footprint (local space) — the organic outline of an area feature. In Polygon mode you edit it with the Scene-view handles; in Noise mode it is generated. Not shown as raw fields — edit it visually in the Scene view.")]
    [SerializeField] private FeaturePolygon footprint = new FeaturePolygon();

    [Header("Footprint — path (linear features)")]
    [Tooltip("Editable poly-line path, in local space. Linear features (canyons, paths, ridges, bridges, arches, cave entrances) sweep along this. Edit the points with the Scene-view handles.")]
    [SerializeField] private FeaturePath path = new FeaturePath();

    [Header("Per-feature settings")]
    [Tooltip("Extra knobs specific to the chosen feature type (e.g. cliff face width, butte taper). The inspector draws these automatically; switching feature type reseeds them with that feature's defaults.")]
    [SerializeReference] private object featureSettings;

    [Tooltip("Tracks which feature type 'featureSettings' was created for, so the inspector can reseed it when the type changes.")]
    [SerializeField] private TerrainFeatureType featureSettingsType = TerrainFeatureType.FlatPad;

    [Header("Tuning")]
    [Tooltip("The four shared knobs — noise, overlap, height, jaggedness — plus walkability. All nine features read these.")]
    [SerializeField] private TerrainFeatureTuning tuning = new TerrainFeatureTuning();

    [Tooltip("Mesh resolution + smoothing settings shared by the marching-cubes pass.")]
    [SerializeField] private TerrainMeshSettings meshSettings = new TerrainMeshSettings();

    [Header("Determinism")]
    [Tooltip("Seed for this feature. Same seed + same tuning + same footprint = identical mesh.")]
    [SerializeField] private int seed = 0;

    [Header("Terrain reference")]
    [Tooltip("The Unity Terrain the feature blends onto. If left null the spawner searches for the active Terrain at bake time. Density and skirt-blend sample this.")]
    [SerializeField] private Terrain targetTerrain;

    [Tooltip("Fallback ground height (local Y) used when no Terrain is found — lets the feature still bake in a terrain-less test scene.")]
    [SerializeField] private float fallbackGroundHeight = 0f;

    [Header("Rendering / NavMesh")]
    [Tooltip("Material applied to the spawned feature mesh. If null a neutral desert-rock material is generated.")]
    [SerializeField] private Material featureMaterial;

    [Tooltip("Layer the spawned mesh GameObject is placed on. Must be a layer the world NavMeshSurface collects, so the feature contributes to the shared NavMesh.")]
    [SerializeField] private string featureLayer = "Default";

    [Header("Baked asset (preferred at runtime)")]
    [Tooltip("Pre-baked feature mesh (single-mesh features). If set, runtime instantiates it instead of generating.")]
    [SerializeField] private Mesh bakedMesh;
    [Tooltip("Pre-baked sub-meshes for an internally-chunked MULTI-MESH feature (e.g. ArchingCave). If non-empty, runtime instantiates these. Single-mesh features leave this empty.")]
    [SerializeField] private Mesh[] bakedSubMeshes;

    [Header("Lifecycle")]
    [Tooltip("Spawn the feature automatically on Awake. Turn off if another system drives spawning.")]
    [SerializeField] private bool spawnOnAwake = true;
    [Tooltip("Draw the footprint gizmo when selected.")]
    [SerializeField] private bool drawFootprintGizmo = true;

    GameObject _featureRoot;
    TerrainFeatureResult _lastResult;
    /// <summary>Prefix on every spawned feature-root GameObject — used for robust cleanup.</summary>
    const string FeatureRootPrefix = "TerrainFeature_";

    // --- Public surface (inspector / editor use these) -----------------------

    public TerrainFeatureType FeatureType => featureType;
    public Vector3 BoxHalfExtents { get => boxHalfExtents; set => boxHalfExtents = value; }
    public FeaturePolygon Footprint => footprint;
    public FeaturePath Path => path;

    /// <summary>Whether the area footprint is hand-edited (Polygon) or noise-generated (Noise).</summary>
    public FootprintShape FootprintShapeMode => footprintShape;

    /// <summary>True while the footprint outline is noise-generated — vertex handles are then read-only.</summary>
    public bool FootprintIsGenerated => !UsesPath && footprintShape == FootprintShape.Noise;

    /// <summary>Rebuilds the noise-generated footprint from the current box + knobs (editor live
    /// update). No-op for linear features or Polygon mode.</summary>
    public void RefreshGeneratedFootprint()
    {
        if (UsesPath || footprintShape != FootprintShape.Noise) return;
        footprint ??= new FeaturePolygon();
        footprint.GenerateFromNoise(boxHalfExtents, seed, footprintComplexity);
    }
    public TerrainFeatureTuning Tuning => tuning;
    public int Seed => seed;

    /// <summary>The designer-tuned per-feature settings object (editor reads/writes this).</summary>
    public object FeatureSettings { get => featureSettings; set => featureSettings = value; }

    /// <summary>Which feature type <see cref="FeatureSettings"/> was last seeded for.</summary>
    public TerrainFeatureType FeatureSettingsType { get => featureSettingsType; set => featureSettingsType = value; }

    /// <summary>Ensures <see cref="FeatureSettings"/> holds a settings object matching the current
    /// feature type — reseeding with that feature's defaults when the type changed or none exists.
    /// Returns the (possibly new) settings object.</summary>
    public object SyncFeatureSettings()
    {
        if (featureSettings != null && featureSettingsType == featureType)
            return featureSettings;
        TerrainFeature probe = TerrainFeatureRegistry.Create(featureType);
        featureSettings = probe != null ? probe.CreateDefaultSettings() : null;
        featureSettingsType = featureType;
        return featureSettings;
    }

    /// <summary>True when a baked single mesh OR baked sub-meshes exist.</summary>
    public bool HasBakedMesh => bakedMesh != null || HasBakedSubMeshes;

    /// <summary>True when pre-baked sub-meshes exist for a multi-mesh feature.</summary>
    public bool HasBakedSubMeshes => bakedSubMeshes != null && bakedSubMeshes.Length > 0;

    public TerrainFeatureResult LastResult => _lastResult;

    /// <summary>True if the chosen feature type sweeps the spline rather than filling the box.
    /// ArchingCave is an AREA feature and is deliberately NOT listed here.</summary>
    public bool UsesPath =>
        featureType == TerrainFeatureType.Canyon
        || featureType == TerrainFeatureType.CanyonPath
        || featureType == TerrainFeatureType.Ridge
        || featureType == TerrainFeatureType.NaturalBridge
        || featureType == TerrainFeatureType.StoneArch
        || featureType == TerrainFeatureType.CaveEntrance;

    void Awake()
    {
        if (!spawnOnAwake) return;
        if (HasBakedMesh) SpawnBaked();
        else GenerateNow();
    }

    // --- Generation ----------------------------------------------------------

    /// <summary>Builds the local <see cref="FeatureContext"/> from this spawner's serialized state.
    /// Shared by the live-generate and editor bake paths so they are guaranteed identical.</summary>
    public FeatureContext BuildContext()
    {
        Terrain terrain = targetTerrain != null ? targetTerrain : Terrain.activeTerrain;
        var ground = new UnityTerrainHeightSampler(terrain, fallbackGroundHeight);

        // Resolve the area footprint outline.
        if (!UsesPath)
        {
            footprint ??= new FeaturePolygon();
            if (footprintShape == FootprintShape.Noise)
            {
                // Generated mode: rebuild the outline from noise every time, so the complexity
                // dial live-updates and the bake is deterministic off the seed.
                footprint.GenerateFromNoise(boxHalfExtents, seed, footprintComplexity);
            }
            else if (!footprint.IsValid)
            {
                // Polygon mode: seed from the box the first time so there is always an outline.
                footprint.ResetToBox(boxHalfExtents);
            }
        }

        // Meshing bounds: XZ from the polygon's outline (so the voxel walk covers the real shape),
        // Y from the box half-extent. Linear features keep the plain box.
        Bounds bounds;
        if (!UsesPath && footprint != null && footprint.IsValid)
        {
            Bounds poly = footprint.ComputeLocalBounds();
            bounds = new Bounds(
                new Vector3(poly.center.x, 0f, poly.center.z),
                new Vector3(poly.size.x, boxHalfExtents.y * 2f, poly.size.z));
        }
        else
        {
            bounds = new Bounds(Vector3.zero, boxHalfExtents * 2f);
        }

        return new FeatureContext(seed, bounds, footprint, path, tuning, ground,
            transform.localToWorldMatrix, meshSettings.voxelSize);
    }

    /// <summary>Runs the full generate pipeline and returns the result (mesh not yet instantiated).</summary>
    public TerrainFeatureResult Generate()
    {
        TerrainFeature feature = TerrainFeatureRegistry.Create(featureType);
        if (feature == null)
        {
            Debug.LogWarning($"[TerrainFeatureSpawner] feature type '{featureType}' is not implemented yet — " +
                             "no class registered in TerrainFeatureRegistry.");
            return new TerrainFeatureResult();
        }
        // Inject the designer-tuned per-feature knobs before the feature describes its shape.
        feature.ApplySettings(SyncFeatureSettings());
        _lastResult = TerrainFeatureGenerator.Generate(feature, BuildContext(), meshSettings);
        return _lastResult;
    }

    /// <summary>Live-generation path: generate, then instantiate the mesh(es) into the scene.</summary>
    [ContextMenu("Generate Now")]
    public void GenerateNow()
    {
        var result = Generate();
        if (!result.IsValid)
        {
            Debug.LogWarning("[TerrainFeatureSpawner] generation produced no mesh.");
            return;
        }
        // Multi-mesh feature → spawn one child per sub-mesh; single-mesh → the original path.
        if (result.IsMultiMesh) SpawnMeshes(result.SubMeshes.ToArray());
        else SpawnMesh(result.Mesh);
    }

    /// <summary>Runtime path for shipping: instantiate the pre-baked mesh(es), no generation cost.</summary>
    public void SpawnBaked()
    {
        if (HasBakedSubMeshes) { SpawnMeshes(bakedSubMeshes); return; }
        if (bakedMesh == null)
        {
            Debug.LogWarning("[TerrainFeatureSpawner] SpawnBaked called with no baked mesh.");
            return;
        }
        SpawnMesh(bakedMesh);
    }

    /// <summary>Instantiates one feature-local mesh under a fresh feature root.</summary>
    void SpawnMesh(Mesh mesh)
    {
        ClearSpawned();
        _featureRoot = TerrainFeatureMeshSpawn.CreateRoot(transform, RootName(), FeatureLayerIndex());
        TerrainFeatureMeshSpawn.AttachMesh(_featureRoot, mesh, "Mesh", ResolveMaterial());
    }

    /// <summary>Instantiates a MULTI-MESH feature: one feature root with one child per sub-mesh.
    /// Every sub-mesh collider feeds the world NavMesh as one continuous walkable surface.</summary>
    void SpawnMeshes(Mesh[] meshes)
    {
        ClearSpawned();
        _featureRoot = TerrainFeatureMeshSpawn.CreateRoot(transform, RootName(), FeatureLayerIndex());
        TerrainFeatureMeshSpawn.AttachMeshes(_featureRoot, meshes, ResolveMaterial());
    }

    string RootName() => $"{FeatureRootPrefix}{featureType}_seed{seed}";
    int FeatureLayerIndex() => LayerMask.NameToLayer(featureLayer);

    /// <summary>Destroys any previously spawned feature root, sweeping by name prefix so a stale
    /// root surviving a domain reload cannot leave a duplicate.</summary>
    public void ClearSpawned()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith(FeatureRootPrefix)) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
        _featureRoot = null;
    }

    /// <summary>Assigns the baked single-mesh asset (editor, after a single-mesh bake). Clears any
    /// baked sub-meshes so the two baked paths never conflict.</summary>
    public void AssignBakedMesh(Mesh mesh)
    {
        bakedMesh = mesh;
        if (mesh != null) bakedSubMeshes = null;
    }

    /// <summary>Assigns the baked sub-mesh assets for a multi-mesh feature (editor, after a
    /// multi-mesh bake). Clears the single baked mesh so the two paths never conflict.</summary>
    public void AssignBakedSubMeshes(Mesh[] meshes)
    {
        bakedSubMeshes = meshes;
        if (meshes != null && meshes.Length > 0) bakedMesh = null;
    }

    /// <summary>The default desert-rock material if none was assigned (see <see cref="TerrainFeatureSpawnerVisuals"/>).</summary>
    Material ResolveMaterial() => TerrainFeatureSpawnerVisuals.ResolveMaterial(featureMaterial);

    /// <summary>Passive footprint gizmo — the interactive handles are drawn by the editor.</summary>
    void OnDrawGizmosSelected()
    {
        if (drawFootprintGizmo) TerrainFeatureSpawnerVisuals.DrawFootprintGizmo(this);
    }
}
