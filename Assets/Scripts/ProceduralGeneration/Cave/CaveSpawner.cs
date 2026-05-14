using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Scene-level driver for the cave system. Two modes:
///
///   • Live generation (designer iteration) — calls <see cref="CaveGenerator"/> at runtime, instantiates
///     the mesh as a child, and (optionally) bakes a NavMesh on the spot. Slow on transition.
///   • Baked assets (shipping) — assign <see cref="bakedMesh"/> + <see cref="bakedNavMeshData"/> in the
///     inspector. At runtime we just spawn the mesh and attach the prebuilt navmesh. No generation,
///     no bake. Use the Editor "Bake &amp; Save" button on the inspector to produce these assets.
/// </summary>
[DisallowMultipleComponent]
public class CaveSpawner : MonoBehaviour
{
    [SerializeField] private CaveGenerationSettings settings = new CaveGenerationSettings();

    [Header("Baked assets (preferred at runtime)")]
    [Tooltip("Pre-baked cave mesh. If set, runtime uses this instead of generating a new cave. Produce via the inspector 'Bake & Save' button.")]
    [SerializeField] private Mesh bakedMesh;

    [Tooltip("Pre-baked NavMeshData matching bakedMesh. If set, attached instead of running a fresh bake.")]
    [SerializeField] private NavMeshData bakedNavMeshData;

    [Header("Visuals")]
    [Tooltip("Optional material applied to the generated mesh. Falls back to URP-lit default if null.")]
    [SerializeField] private Material caveMaterial;

    [Header("Cluster lights")]
    [Tooltip("Number of point lights placed at random spots along the cave walls. The shader handles the visible algae specs; these lights add real illumination so nearby surfaces glow.")]
    [SerializeField] private int clusterLightCount = 20;
    [Tooltip("Color of each cluster light.")]
    [SerializeField] private Color clusterLightColor = new Color(0.35f, 0.75f, 1.0f, 1f);
    [Tooltip("Intensity of each cluster light.")]
    [SerializeField] private float clusterLightIntensity = 1.2f;
    [Tooltip("Range of each cluster light in world units.")]
    [SerializeField] private float clusterLightRange = 5f;
    [Tooltip("Hit normals where |normal.y| exceeds this are treated as floor/ceiling and skipped (kept on walls only).")]
    [Range(0f, 1f)]
    [SerializeField] private float clusterLightMaxNormalY = 0.7f;

    [Header("Lifecycle")]
    [SerializeField] private bool generateOnStart = true;
    [Tooltip("Randomise the seed on every Start. Ignored when baked assets are assigned.")]
    [SerializeField] private bool randomSeedOnStart = false;

    [Header("Debug")]
    [SerializeField] private bool drawGraphGizmos = true;
    [SerializeField] private bool drawBoundsGizmo = true;

    GameObject _caveRoot;
    CaveGenerationResult _last;
    NavMeshDataInstance _navMeshInstance;

    public CaveGenerationResult LastResult => _last;
    public CaveGenerationSettings Settings => settings;
    public bool HasBakedAssets => bakedMesh != null && bakedNavMeshData != null;

    void Start()
    {
        if (!generateOnStart) return;
        if (HasBakedAssets) SpawnBaked();
        else GenerateNow();
    }

    void OnDestroy()
    {
        if (_navMeshInstance.valid) _navMeshInstance.Remove();
    }

    /// <summary>
    /// Runtime path for shipping: instantiate the prebaked mesh and attach the prebaked navmesh.
    /// O(milliseconds) regardless of cave size.
    /// </summary>
    public void SpawnBaked()
    {
        ClearPrevious();

        _caveRoot = new GameObject("CaveMesh_baked");
        _caveRoot.transform.SetParent(transform, worldPositionStays: false);

        var mf = _caveRoot.AddComponent<MeshFilter>();
        mf.sharedMesh = bakedMesh;

        var mr = _caveRoot.AddComponent<MeshRenderer>();
        mr.sharedMaterial = caveMaterial != null ? caveMaterial : DefaultCaveMaterial();

        var mc = _caveRoot.AddComponent<MeshCollider>();
        mc.sharedMesh = bakedMesh;

        _navMeshInstance = NavMesh.AddNavMeshData(bakedNavMeshData);

        ScatterWallDecor(mc);
    }

    [ContextMenu("Generate Now")]
    public void GenerateNow()
    {
        if (randomSeedOnStart) settings.seed = Random.Range(int.MinValue, int.MaxValue);

        ClearPrevious();

        float t0 = Time.realtimeSinceStartup;
        _last = CaveGenerator.Generate(settings);
        float genMs = (Time.realtimeSinceStartup - t0) * 1000f;

        _caveRoot = new GameObject($"CaveMesh_seed{_last.Seed}");
        _caveRoot.transform.SetParent(transform, worldPositionStays: false);

        var mf = _caveRoot.AddComponent<MeshFilter>();
        mf.sharedMesh = _last.Mesh;

        var mr = _caveRoot.AddComponent<MeshRenderer>();
        mr.sharedMaterial = caveMaterial != null ? caveMaterial : DefaultCaveMaterial();

        var mc = _caveRoot.AddComponent<MeshCollider>();
        mc.sharedMesh = _last.Mesh;

        if (settings.bakeNavMeshOnGenerate) BakeNavMeshLive();

        ScatterWallDecor(mc);

        Debug.Log($"[CaveSpawner] generated cave: {_last.Graph.Rooms.Count} rooms, " +
                  $"{_last.Graph.Corridors.Count} corridors, " +
                  $"{_last.Mesh.vertexCount} verts, {_last.Mesh.triangles.Length / 3} tris " +
                  $"in {genMs:F0} ms (seed {_last.Seed}).");
    }

    /// <summary>
    /// Editor-only entry point. Runs the full generate + bake pipeline and exposes the produced
    /// mesh + NavMeshData so the editor script can save them as assets. Returns true on success.
    /// </summary>
    public bool EditorBuildForBaking(out Mesh mesh, out NavMeshData navMeshData)
    {
        mesh = null;
        navMeshData = null;

        ClearPrevious();

        _last = CaveGenerator.Generate(settings);
        if (_last == null || _last.Mesh == null) return false;

        _caveRoot = new GameObject($"CaveMesh_seed{_last.Seed}");
        _caveRoot.transform.SetParent(transform, worldPositionStays: false);
        _caveRoot.AddComponent<MeshFilter>().sharedMesh = _last.Mesh;
        var mr = _caveRoot.AddComponent<MeshRenderer>();
        mr.sharedMaterial = caveMaterial != null ? caveMaterial : DefaultCaveMaterial();
        _caveRoot.AddComponent<MeshCollider>().sharedMesh = _last.Mesh;

        var surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        mesh = _last.Mesh;
        navMeshData = surface.navMeshData;
        return navMeshData != null;
    }

    public void AssignBakedAssets(Mesh mesh, NavMeshData data)
    {
        bakedMesh = mesh;
        bakedNavMeshData = data;
    }

    void BakeNavMeshLive()
    {
        var surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    void ClearPrevious()
    {
        if (_caveRoot != null)
        {
            if (Application.isPlaying) Destroy(_caveRoot);
            else DestroyImmediate(_caveRoot);
        }
        if (_navMeshInstance.valid)
        {
            _navMeshInstance.Remove();
            _navMeshInstance = default;
        }
        var surface = GetComponent<NavMeshSurface>();
        if (surface != null && surface.navMeshData != null) surface.RemoveData();
    }

    void ScatterWallDecor(MeshCollider caveCollider)
    {
        if (clusterLightCount <= 0 || caveCollider == null) return;
        var mesh = caveCollider.sharedMesh;
        if (mesh == null) return;

        var verts = mesh.vertices;
        var tris  = mesh.triangles;
        var normals = mesh.normals;
        if (tris.Length == 0) return;

        int triCount = tris.Length / 3;
        var rng = new System.Random(_last != null ? _last.Seed : settings.seed);
        var caveTransform = _caveRoot.transform;
        Vector3 worldCenter = caveCollider.bounds.center;

        // Collect wall triangles (skip floor/ceiling)
        var wallTris = new System.Collections.Generic.List<(Vector3 center, Vector3 inwardNormal)>();
        for (int t = 0; t < triCount; t++)
        {
            int i0 = tris[t * 3 + 0], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
            Vector3 localCenter = (verts[i0] + verts[i1] + verts[i2]) / 3f;
            Vector3 localNormal = normals != null && normals.Length == verts.Length
                ? (normals[i0] + normals[i1] + normals[i2]).normalized
                : Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
            Vector3 worldPos = caveTransform.TransformPoint(localCenter);
            Vector3 worldNormal = caveTransform.TransformDirection(localNormal).normalized;
            Vector3 toCenter = (worldCenter - worldPos).normalized;
            Vector3 inward = Vector3.Dot(worldNormal, toCenter) > 0f ? worldNormal : -worldNormal;
            if (Mathf.Abs(inward.y) <= clusterLightMaxNormalY)
                wallTris.Add((worldPos, inward));
        }
        if (wallTris.Count == 0) return;

        var lightsRoot = new GameObject("AlgaeLights");
        lightsRoot.transform.SetParent(caveTransform, worldPositionStays: false);
        int interiorLayer = LayerMask.NameToLayer("Interior");

        int placed = 0;
        for (int i = 0; i < clusterLightCount; i++)
        {
            var pick = wallTris[rng.Next(wallTris.Count)];
            // Sit the light just off the wall, inside the cave
            Vector3 lightPos = pick.center + pick.inwardNormal * 0.4f;

            var lightGo = new GameObject($"AlgaeLight_{placed}");
            lightGo.transform.SetParent(lightsRoot.transform, worldPositionStays: true);
            lightGo.transform.position = lightPos;
            if (interiorLayer >= 0) lightGo.layer = interiorLayer;

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = clusterLightColor;
            light.intensity = clusterLightIntensity;
            light.range = clusterLightRange;
            light.shadows = LightShadows.None;
            placed++;
        }

        Debug.Log($"[CaveSpawner] placed {placed} algae cluster lights.");
    }


    Material DefaultCaveMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = "DefaultCaveMaterial" };
        m.SetColor("_BaseColor", new Color(0.32f, 0.30f, 0.27f));
        m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    // -------------------------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------------------------

    void OnDrawGizmosSelected()
    {
        if (drawBoundsGizmo)
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireCube(Vector3.zero, settings.halfExtents * 2f);
        }

        if (drawGraphGizmos && _last != null)
        {
            foreach (var r in _last.Graph.Rooms)
            {
                Gizmos.color = r.Kind switch
                {
                    RoomKind.BigChamber => new Color(1f, 0.4f, 0.2f, 0.6f),
                    RoomKind.Junction   => new Color(0.4f, 1f, 0.5f, 0.6f),
                    RoomKind.DeadEnd    => new Color(0.6f, 0.6f, 0.6f, 0.5f),
                    _                   => new Color(0.9f, 0.9f, 0.2f, 0.5f),
                };
                Gizmos.DrawWireSphere(transform.TransformPoint(r.Center), r.Radius);
            }

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.7f);
            foreach (var c in _last.Graph.Corridors)
            {
                var a = transform.TransformPoint(_last.Graph.Rooms[c.FromRoomId].Center);
                var b = transform.TransformPoint(_last.Graph.Rooms[c.ToRoomId].Center);
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
