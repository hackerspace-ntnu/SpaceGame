using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Quick-pick palette of the project's sandstone triplanar materials, surfaced as a dropdown on
    /// <see cref="TerrainFeatureSpawner"/> so a designer can choose a terrain look without hunting for
    /// the material asset. <see cref="Custom"/> leaves the explicit <c>featureMaterial</c> field in
    /// charge. The enum-to-asset mapping lives in <see cref="TerrainFeatureSpawnerVisuals"/>.
    /// </summary>
    public enum TerrainMaterialPreset
    {
        Custom = 0,
        SandstoneLight,
        RedDesert,
        GoldenDune,
        SaltFlat,
        SandstoneDark,
    }

    /// <summary>
    /// Scene-level driver for the terrain-feature system — the terrain analogue of <c>CaveSpawner</c>.
    /// The designer picks a <see cref="featureType"/>, defines the footprint and tunes the sliders;
    /// the editor bakes the mesh(es) to assets and runtime instantiates them. Feature meshes contribute
    /// to the shared world NavMesh via their <see cref="MeshCollider"/> — a multi-mesh feature spawns
    /// one collider per sub-mesh, all feeding the one unified walkable surface.
    /// </summary>
    [DisallowMultipleComponent]
    public class TerrainFeatureSpawner : MonoBehaviour, ISerializationCallbackReceiver
    {
        [Header("Feature")]
        [Tooltip("Which procedural terrain feature this spawner builds. The editor warns if the chosen type has no implementation registered yet.")]
        [SerializeField] private TerrainFeatureType featureType = TerrainFeatureType.FlatPad;

        [Header("Footprint (area features)")]
        [Tooltip("The area footprint: box dimensions (Width / Height / Breadth), the Polygon/Noise " +
                 "mode, the outline polygon and the noise knobs. Edit the outline visually in the " +
                 "Scene view; tune size and noise here.")]
        [SerializeField] private FeatureFootprint area = new FeatureFootprint();

        // --- Legacy fields (pre-rewrite). Kept ONLY so old scenes deserialize; migrated into 'area'
        //     in OnAfterDeserialize, then never written again. Hidden from the inspector.
        [HideInInspector] [SerializeField] private Vector3 boxHalfExtents = Vector3.zero;
        [HideInInspector] [SerializeField] private int footprintShape = -1;
        [HideInInspector] [SerializeField] private float footprintComplexity = -1f;
        [HideInInspector] [SerializeField] private FeaturePolygon footprint;

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
        [Tooltip("Quick-pick terrain look. Choosing any preset other than 'Custom' auto-assigns the matching " +
                 "sandstone material into 'Feature Material' below. Pick 'Custom' to drag in your own material.")]
        [SerializeField] private TerrainMaterialPreset materialPreset = TerrainMaterialPreset.Custom;

        [Tooltip("Material applied to the spawned feature mesh. If null a neutral desert-rock material is generated. " +
                 "Set automatically when a Material Preset is chosen.")]
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

        /// <summary>The Unity Terrain this feature blends onto. Null falls back to the active Terrain
        /// at bake time. Exposed so <see cref="TerrainGenManager"/> can assign one terrain across a
        /// whole folder of features in a single click.</summary>
        public Terrain TargetTerrain { get => targetTerrain; set => targetTerrain = value; }

        /// <summary>Name of the layer the spawned mesh GameObject is placed on. Exposed so the manager
        /// can set a consistent NavMesh-collected layer across every feature in a folder.</summary>
        public string FeatureLayer { get => featureLayer; set => featureLayer = value; }

        /// <summary>Quick-pick terrain material preset. <see cref="TerrainMaterialPreset.Custom"/> means
        /// the explicit <see cref="featureMaterial"/> is used as-is.</summary>
        public TerrainMaterialPreset MaterialPreset { get => materialPreset; set => materialPreset = value; }

        /// <summary>The explicitly-assigned feature material (also the slot a preset writes into).</summary>
        public Material FeatureMaterial { get => featureMaterial; set => featureMaterial = value; }

        /// <summary>The area footprint authority — box dimensions, mode, outline polygon, noise knobs.</summary>
        public FeatureFootprint Area => area ??= new FeatureFootprint();

        /// <summary>Box half-extents of the area footprint (X = half-Width, Y = half-Height,
        /// Z = half-Breadth). Setting it writes the three dimensions back. Compatibility shim for the
        /// Scene-view handles, which work in half-extents.</summary>
        public Vector3 BoxHalfExtents
        {
            get => Area.BoxHalfExtents;
            set
            {
                Area.width   = Mathf.Max(2f, value.x * 2f);
                Area.height  = Mathf.Max(2f, value.y * 2f);
                Area.breadth = Mathf.Max(2f, value.z * 2f);
            }
        }

        /// <summary>The hand-editable / generated outline polygon of the area footprint.</summary>
        public FeaturePolygon Footprint => Area.polygon;
        public FeaturePath Path => path;

        /// <summary>Whether the area footprint is hand-edited (Polygon) or noise-generated (Noise).</summary>
        public FootprintMode FootprintModeValue => Area.mode;

        /// <summary>True while the footprint outline is noise-generated — vertex handles are then read-only.</summary>
        public bool FootprintIsGenerated => !UsesPath && Area.mode == FootprintMode.Noise;

        /// <summary>Rebuilds the noise-generated footprint from the current box + knobs (editor live
        /// update). No-op for linear features or Polygon mode.</summary>
        public void RefreshGeneratedFootprint()
        {
            if (UsesPath) return;
            Area.Refresh(seed);
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
            {
                // Heal nested [SerializeReference] blocks that an older scene deserialised as null,
                // so newly-added knobs materialise (and draw in the inspector) on existing spawners.
                TerrainFeature healProbe = TerrainFeatureRegistry.Create(featureType);
                if (healProbe != null)
                    featureSettings = healProbe.HealSettings(featureSettings);
                return featureSettings;
            }
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

            // The chunk's load callback scanned the scene for NavMesh sources BEFORE this Awake
            // ran, so the mesh(es) just spawned were not collected. Tell the streamer to
            // re-collect this chunk so the new terrain geometry is baked into the world NavMesh.
            // Without this, agents standing on a runtime-generated feature have no NavMesh.
            if (Application.isPlaying)
            {
                var streamer = FindFirstObjectByType<WorldStreamer>();
                if (streamer != null)
                    streamer.NotifyChunkGeometryChanged(transform.position);
            }
        }

        // --- Generation ----------------------------------------------------------

        /// <summary>Builds the local <see cref="FeatureContext"/> from this spawner's serialized state.
        /// Shared by the live-generate and editor bake paths so they are guaranteed identical.</summary>
        public FeatureContext BuildContext()
        {
            Terrain terrain = targetTerrain != null ? targetTerrain : Terrain.activeTerrain;
            var ground = new UnityTerrainHeightSampler(terrain, fallbackGroundHeight);

            area ??= new FeatureFootprint();

            // Resolve the area footprint outline (regenerates from noise / seeds an ellipse as needed).
            // Linear features ignore it but it stays valid so a feature switch is seamless.
            area.Refresh(seed);

            // Meshing bounds: from the footprint for area features, from the path box for linear ones.
            Bounds bounds = UsesPath
                ? new Bounds(Vector3.zero, area.BoxHalfExtents * 2f)
                : area.ComputeLocalBounds();

            return new FeatureContext(seed, bounds, area, path, tuning, ground,
                transform.localToWorldMatrix, meshSettings.voxelSize);
        }

        // --- Legacy migration ----------------------------------------------------

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        /// <summary>Upgrades a scene authored before the footprint rewrite: the old boxHalfExtents +
        /// FootprintShape + complexity + loose polygon are folded into <see cref="area"/> once, then
        /// the legacy fields are cleared so they are never written back.</summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // A legacy scene is detected by the value-type sentinels (boxHalfExtents/shape/complexity)
            // or a still-VALID legacy polygon. The polygon is tested for validity (>=3 verts) rather
            // than mere non-null: a plain [Serializable] field deserialises as a non-null empty object,
            // and treating that as "legacy" would re-run migration forever and revert footprint edits.
            bool hasLegacy = footprintShape >= 0 || boxHalfExtents != Vector3.zero
                             || footprintComplexity >= 0f
                             || (footprint != null && footprint.IsValid);
            if (!hasLegacy) return;

            area ??= new FeatureFootprint();
            area.MigrateFromLegacy(
                boxHalfExtents != Vector3.zero ? boxHalfExtents : new Vector3(40f, 25f, 40f),
                footprintShape, footprintComplexity >= 0f ? footprintComplexity : 3f, footprint);

            boxHalfExtents = Vector3.zero;
            footprintShape = -1;
            footprintComplexity = -1f;
            footprint = null;
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
}
