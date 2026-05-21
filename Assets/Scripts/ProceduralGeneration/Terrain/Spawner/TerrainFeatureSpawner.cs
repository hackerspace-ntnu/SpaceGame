using UnityEngine;

/// <summary>
/// Scene-level driver for the terrain-feature system — the terrain analogue of <c>CaveSpawner</c>.
///
/// The designer drops one of these on a GameObject, drags the box/spline gizmo (handled by
/// <c>TerrainFeatureSpawnerEditor</c>) to define the footprint, picks a <see cref="featureType"/>,
/// and tunes the shared <see cref="tuning"/> sliders. The editor's "Bake &amp; Save" button writes
/// the finished mesh to an asset; at runtime the spawner just instantiates that baked mesh — zero
/// runtime generation cost, exactly like <c>CaveSpawner</c>'s baked path.
///
/// NAVMESH: the feature mesh CONTRIBUTES to the shared world NavMesh. The spawner gives the spawned
/// mesh a <see cref="MeshCollider"/> on the configured layer; the world's <c>NavMeshSourceCache</c>
/// / <c>NavMeshSurface</c> rebuild picks it up as one source among many. The feature gets NO
/// isolated NavMeshData of its own — one unified walkable surface is the design rule.
/// </summary>
[DisallowMultipleComponent]
public class TerrainFeatureSpawner : MonoBehaviour
{
    [Header("Feature")]
    [Tooltip("Which procedural terrain feature this spawner builds. The editor warns if the chosen type has no implementation registered yet.")]
    [SerializeField] private TerrainFeatureType featureType = TerrainFeatureType.FlatPad;

    [Header("Footprint — box (area features)")]
    [Tooltip("Half-extents of the resizable box footprint, in local space. Drag the gizmo handles in the Scene view to resize. Area features (dunes, mesas, buttes, cliffs) mesh inside this box.")]
    [SerializeField] private Vector3 boxHalfExtents = new Vector3(40f, 25f, 40f);

    [Header("Footprint — path (linear features)")]
    [Tooltip("Editable poly-line path, in local space. Linear features (canyons, paths, ridges, bridges, arches, cave entrances) sweep along this. Edit the points with the Scene-view handles.")]
    [SerializeField] private FeaturePath path = new FeaturePath();

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
    [Tooltip("Pre-baked feature mesh. If set, runtime instantiates this instead of generating. Produce via the inspector 'Bake & Save' button.")]
    [SerializeField] private Mesh bakedMesh;

    [Header("Lifecycle")]
    [Tooltip("Spawn the feature automatically on Awake. Turn off if another system drives spawning.")]
    [SerializeField] private bool spawnOnAwake = true;

    [Header("Gizmo")]
    [SerializeField] private bool drawFootprintGizmo = true;

    GameObject _featureRoot;
    TerrainFeatureResult _lastResult;

    /// <summary>Prefix on every spawned feature-root GameObject — used for robust cleanup.</summary>
    const string FeatureRootPrefix = "TerrainFeature_";

    // -------------------------------------------------------------------------
    // Public surface (inspector / editor use these)
    // -------------------------------------------------------------------------

    public TerrainFeatureType FeatureType => featureType;
    public Vector3 BoxHalfExtents { get => boxHalfExtents; set => boxHalfExtents = value; }
    public FeaturePath Path => path;
    public TerrainFeatureTuning Tuning => tuning;
    public int Seed => seed;
    public bool HasBakedMesh => bakedMesh != null;
    public TerrainFeatureResult LastResult => _lastResult;

    /// <summary>True if the chosen feature type sweeps the spline rather than filling the box.</summary>
    public bool UsesPath
    {
        get
        {
            return featureType == TerrainFeatureType.Canyon
                || featureType == TerrainFeatureType.CanyonPath
                || featureType == TerrainFeatureType.Ridge
                || featureType == TerrainFeatureType.NaturalBridge
                || featureType == TerrainFeatureType.StoneArch
                || featureType == TerrainFeatureType.CaveEntrance;
        }
    }

    void Awake()
    {
        if (!spawnOnAwake) return;
        if (HasBakedMesh) SpawnBaked();
        else GenerateNow();
    }

    // -------------------------------------------------------------------------
    // Generation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the local <see cref="FeatureContext"/> from this spawner's serialized state. Shared
    /// by the live-generate path and the editor bake path so they are guaranteed identical.
    /// </summary>
    public FeatureContext BuildContext()
    {
        Terrain terrain = targetTerrain != null ? targetTerrain : Terrain.activeTerrain;
        var ground = new UnityTerrainHeightSampler(terrain, fallbackGroundHeight);
        var bounds = new Bounds(Vector3.zero, boxHalfExtents * 2f);
        return new FeatureContext(seed, bounds, path, tuning, ground,
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
        _lastResult = TerrainFeatureGenerator.Generate(feature, BuildContext(), meshSettings);
        return _lastResult;
    }

    /// <summary>Live-generation path: generate, then instantiate the mesh into the scene.</summary>
    [ContextMenu("Generate Now")]
    public void GenerateNow()
    {
        var result = Generate();
        if (!result.IsValid)
        {
            Debug.LogWarning("[TerrainFeatureSpawner] generation produced no mesh.");
            return;
        }
        SpawnMesh(result.Mesh);
    }

    /// <summary>Runtime path for shipping: instantiate the pre-baked mesh, no generation cost.</summary>
    public void SpawnBaked()
    {
        if (bakedMesh == null)
        {
            Debug.LogWarning("[TerrainFeatureSpawner] SpawnBaked called with no baked mesh.");
            return;
        }
        SpawnMesh(bakedMesh);
    }

    /// <summary>
    /// Instantiates the given feature-local mesh as a child GameObject with a MeshFilter, a
    /// MeshRenderer and a MeshCollider. The collider on the configured layer is what makes the
    /// feature contribute to the shared world NavMesh — no isolated NavMeshData is created.
    /// </summary>
    void SpawnMesh(Mesh mesh)
    {
        ClearSpawned();

        _featureRoot = new GameObject($"{FeatureRootPrefix}{featureType}_seed{seed}");
        _featureRoot.transform.SetParent(transform, worldPositionStays: false);

        int layer = LayerMask.NameToLayer(featureLayer);
        if (layer >= 0) _featureRoot.layer = layer;

        _featureRoot.AddComponent<MeshFilter>().sharedMesh = mesh;
        _featureRoot.AddComponent<MeshRenderer>().sharedMaterial = ResolveMaterial();

        // MeshCollider — picked up by NavMeshSourceCache (PhysicsColliders geometry) so the
        // feature joins the one unified world NavMesh on the next rebuild.
        _featureRoot.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>Destroys any previously spawned feature root. Sweeps by name prefix so a stale
    /// root surviving a domain reload cannot leave a duplicate behind.</summary>
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

    /// <summary>Assigns the baked mesh asset (called by the editor after a bake).</summary>
    public void AssignBakedMesh(Mesh mesh) => bakedMesh = mesh;

    Material ResolveMaterial()
    {
        if (featureMaterial != null) return featureMaterial;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = "DefaultTerrainFeatureMaterial" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.78f, 0.66f, 0.46f));
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    // -------------------------------------------------------------------------
    // Gizmo — the editor draws the interactive handles; this is the passive draw.
    // -------------------------------------------------------------------------

    void OnDrawGizmosSelected()
    {
        if (!drawFootprintGizmo) return;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (UsesPath)
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            if (path != null && path.IsValid)
            {
                var spline = new FeatureSpline(path);
                Vector3 prev = spline.Evaluate(0f);
                for (int i = 1; i <= 48; i++)
                {
                    Vector3 cur = spline.Evaluate(i / 48f);
                    Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }
        }
        else
        {
            Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, boxHalfExtents * 2f);
        }
    }
}
