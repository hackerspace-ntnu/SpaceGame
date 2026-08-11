using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Instantiates prefabs from a List&lt;TilePlacement&gt; produced by SettlementGenerator.
///
/// Prefab conventions (all at 3× scale, tileSize = 1):
///
///   Floor / Roof / InteriorFloor / Frieze
///     – centred on XZ, placed at cell-base Y.
///
///   Wall variants (WallMonolith, WallSlitWindow, WallGrandArch,
///                  WallButtress, WallPanel)
///     – prefab faces +X at rest, pivot at (0, 0.5, 0).
///     – Y-rotated to face direction, then shifted 0.5 outward.
///
///   Pillar
///     – thin corner column, placed at one of 4 corners of the tile.
///
///   ColonnadeColumn
///     – thick drum column, centred on the tile XZ.
///
///   RoofParapet, Frieze (face variant)
///     – same rotation convention as walls, placed at cell Y.
///
///   GrandArch, Bridge, AqueductArch
///     – centred on tile XZ, Y-rotated to face.
///
///   Obelisk, RoofTemple
///     – centred on tile XZ, no rotation.
///
///   Stair, ExteriorRamp
///     – placed at cell-base Y, Y-rotated to face ascent direction.
///
///   BoulderSmall, BoulderLarge
///     – centred on tile XZ, random Y rotation for natural look.
/// </summary>
public class SettlementBuilder : MonoBehaviour
{
    [SerializeField] private SettlementPrefabConfig config;

    private struct VisualGroupKey
    {
        public Material overrideMaterial;
        public Vector2Int chunk;
        public ShadowCastingMode shadowCastingMode;
    }

    private sealed class SpawnRecord
    {
        public GameObject root;
        public TileKind kind;
    }

    private readonly List<SpawnRecord> spawnedObjects = new();
    private readonly List<GameObject> optimizationObjects = new();
    private static Mesh cachedCubeMesh;

    private static readonly Dictionary<WallFace, Quaternion> WallRotations = new()
    {
        { WallFace.East,  Quaternion.Euler(0,   0, 0) },
        { WallFace.South, Quaternion.Euler(0,  90, 0) },
        { WallFace.West,  Quaternion.Euler(0, 180, 0) },
        { WallFace.North, Quaternion.Euler(0, 270, 0) },
    };

    private static readonly Vector2[] PillarCorners = {
        new(-0.5f, -0.5f), // North = index 0
        new( 0.5f, -0.5f), // East  = index 1
        new(-0.5f,  0.5f), // South = index 2
        new( 0.5f,  0.5f), // West  = index 3
    };

    public void SetConfig(SettlementPrefabConfig cfg) => config = cfg;

    public void Build(List<TilePlacement> placements, Vector3 worldOrigin)
    {
        Clear();
        if (config == null)
        {
            Debug.LogError("[SettlementBuilder] No config assigned.");
            return;
        }

        float s = config.tileSize;
        foreach (var p in placements)
        {
            var center = worldOrigin + new Vector3(p.cell.x * s, p.cell.y * s, p.cell.z * s);
            PlaceTile(p, center, s);
        }

        if (config.optimizeAfterBuild)
            OptimizeSpawnedGeometry(placements, worldOrigin, s);
    }

    public void Clear()
    {
        foreach (var record in spawnedObjects)
            if (record.root != null) DestroyImmediate(record.root);
        spawnedObjects.Clear();

        foreach (var go in optimizationObjects)
            if (go != null) DestroyImmediate(go);
        optimizationObjects.Clear();
    }

    // -------------------------------------------------------------------------

    void PlaceTile(TilePlacement p, Vector3 center, float s)
    {
        switch (p.kind)
        {
            // ── Slabs ──────────────────────────────────────────────────────────
            case TileKind.Floor:
                Spawn(p.kind, config.Pick(config.floorVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), Quaternion.identity);
                break;

            case TileKind.InteriorFloor:
                Spawn(p.kind, config.Pick(config.interiorFloorVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), Quaternion.identity);
                break;

            case TileKind.Roof:
                Spawn(p.kind, config.Pick(config.roofVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), Quaternion.identity);
                break;

            case TileKind.Frieze:
                // Frieze: same as wall (face-offset) but uses frieze prefab
                SpawnFaced(p.kind, config.Pick(config.friezeVariants, p.variant), center, p.face, s);
                break;

            // ── Walls ──────────────────────────────────────────────────────────
            case TileKind.WallMonolith:
                SpawnFaced(p.kind, config.Pick(config.wallMonolithVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallSlitWindow:
                SpawnFaced(p.kind, config.Pick(config.wallSlitVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallGrandArch:
                SpawnFaced(p.kind, config.Pick(config.wallGrandArchVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallButtress:
                SpawnFaced(p.kind, config.Pick(config.wallButtressVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallPanel:
                SpawnFaced(p.kind, config.Pick(config.wallPanelVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallRelief:
                SpawnFaced(p.kind, config.Pick(config.wallReliefVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallTech:
                SpawnFaced(p.kind, config.Pick(config.wallTechVariants, p.variant), center, p.face, s);
                break;
            case TileKind.WallVent:
                SpawnFaced(p.kind, config.Pick(config.wallVentVariants, p.variant), center, p.face, s);
                break;

            // ── Columns ────────────────────────────────────────────────────────
            case TileKind.Pillar:
            {
                var corner = PillarCorners[(int)p.face];
                Spawn(p.kind, config.Pick(config.pillarVariants, p.variant),
                      center + new Vector3(corner.x * s, 0, corner.y * s), Quaternion.identity);
                break;
            }
            case TileKind.ColonnadeColumn:
                Spawn(p.kind, config.Pick(config.colonnadeVariants, p.variant),
                      center, Quaternion.identity);
                break;
            case TileKind.Balcony:
                SpawnFaced(p.kind, config.Pick(config.balconyVariants, p.variant), center, p.face, s);
                break;

            // ── Rooftop ────────────────────────────────────────────────────────
            case TileKind.RoofParapet:
                SpawnFaced(p.kind, config.Pick(config.roofParapetVariants, p.variant), center, p.face, s);
                break;
            case TileKind.RoofTemple:
                Spawn(p.kind, config.Pick(config.roofTempleVariants, p.variant),
                      center, Quaternion.identity);
                break;
            case TileKind.RoofMachinery:
                Spawn(p.kind, config.Pick(config.roofMachineryVariants, p.variant),
                      center, Quaternion.identity);
                break;
            case TileKind.CircularFeature:
                Spawn(p.kind, config.Pick(config.circularFeatureVariants, p.variant),
                      center, Quaternion.identity, new Vector3(5.4f, 6.2f, 5.4f));
                break;
            case TileKind.TriangularFeature:
                Spawn(p.kind, config.Pick(config.triangularFeatureVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), WallRotations[p.face],
                      new Vector3(1.8f, 1.8f, 1.8f));
                break;

            // ── Large structural ───────────────────────────────────────────────
            case TileKind.GrandArch:
                Spawn(p.kind, config.Pick(config.grandArchVariants, p.variant),
                      center, WallRotations[p.face]);
                break;
            case TileKind.Bridge:
                Spawn(p.kind, config.Pick(config.bridgeVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), Quaternion.identity);
                break;
            case TileKind.AqueductArch:
                Spawn(p.kind, config.Pick(config.aqueductArchVariants, p.variant),
                      center, WallRotations[p.face]);
                break;

            // ── Traversal ──────────────────────────────────────────────────────
            case TileKind.Stair:
                Spawn(p.kind, config.Pick(config.stairVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), WallRotations[p.face]);
                break;
            case TileKind.ExteriorRamp:
                Spawn(p.kind, config.Pick(config.exteriorRampVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), WallRotations[p.face]);
                break;

            // ── Environmental ──────────────────────────────────────────────────
            case TileKind.Obelisk:
                Spawn(p.kind, config.Pick(config.obeliskVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0), Quaternion.identity);
                break;
            case TileKind.BoulderSmall:
                Spawn(p.kind, config.Pick(config.boulderSmallVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0),
                      Quaternion.Euler(0, p.variant * 73f, 0),
                      Vector3.one * config.boulderUniformScale);
                break;
            case TileKind.BoulderLarge:
                Spawn(p.kind, config.Pick(config.boulderLargeVariants, p.variant),
                      center + new Vector3(0, -0.5f * s, 0),
                      Quaternion.Euler(0, p.variant * 51f, 0),
                      Vector3.one * config.boulderUniformScale);
                break;
        }
    }

    void SpawnFaced(TileKind kind, GameObject prefab, Vector3 center, WallFace face, float s)
    {
        if (prefab == null) return;
        var rot = WallRotations[face];
        Spawn(kind, prefab, center, rot);
    }

    void Spawn(TileKind kind, GameObject prefab, Vector3 pos, Quaternion rot)
    {
        Spawn(kind, prefab, pos, rot, null);
    }

    void Spawn(TileKind kind, GameObject prefab, Vector3 pos, Quaternion rot, Vector3? localScaleOverride)
    {
        if (prefab == null) return;
        var instance = Instantiate(prefab, pos, rot, transform);
        if (localScaleOverride.HasValue)
            instance.transform.localScale = localScaleOverride.Value;
        ApplyMaterial(instance, kind);
        spawnedObjects.Add(new SpawnRecord { root = instance, kind = kind });
    }

    void ApplyMaterial(GameObject instance, TileKind kind)
    {
        var mat = GetMaterialForKind(kind);
        if (mat == null) return;
        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            renderer.sharedMaterial = mat;
    }

    Material GetMaterialForKind(TileKind kind)
    {
        return kind switch
        {
            TileKind.WallTech          => config.accentMaterial,
            TileKind.WallVent          => config.accentMaterial,
            TileKind.RoofMachinery     => config.accentMaterial,
            TileKind.CircularFeature   => config.accentMaterial,
            TileKind.TriangularFeature => config.accentMaterial,
            TileKind.RoofTemple        => config.accentMaterial,
            TileKind.Obelisk           => config.accentMaterial,
            _ => IsSecondaryStructure(kind) ? config.baseMaterialB : config.baseMaterialA,
        };
    }

    static bool IsSecondaryStructure(TileKind kind)
    {
        return kind == TileKind.Frieze ||
               kind == TileKind.WallButtress ||
               kind == TileKind.WallPanel ||
               kind == TileKind.WallRelief ||
               kind == TileKind.RoofParapet ||
               kind == TileKind.Pillar ||
               kind == TileKind.ColonnadeColumn ||
               kind == TileKind.Balcony;
    }

    void OptimizeSpawnedGeometry(List<TilePlacement> placements, Vector3 worldOrigin, float tileSize)
    {
        var visualGroups = new Dictionary<VisualGroupKey, List<CombineInstance>>();
        int sourceRendererCount = 0;

        foreach (var record in spawnedObjects)
        {
            if (record.root == null) continue;

            if (config.stripDetailColliders || config.bakeCombinedColliders)
                RemoveColliders(record.root);

            sourceRendererCount += CollectVisualCombines(record.root, record.kind, visualGroups);
        }

        int combinedRendererCount = CreateCombinedVisualMeshes(visualGroups);
        int combinedColliderCount = config.bakeCombinedColliders
            ? CreateCombinedColliderMeshes(placements, tileSize)
            : 0;

        foreach (var record in spawnedObjects)
        {
            if (record.root == null) continue;
            DestroyImmediate(record.root);
            record.root = null;
        }

        spawnedObjects.RemoveAll(r => r.root == null);

        foreach (var go in optimizationObjects)
            MarkStatic(go);

        Debug.Log($"[SettlementBuilder] Optimized settlement: {sourceRendererCount} source renderers -> {combinedRendererCount} combined renderers, {combinedColliderCount} baked collider meshes.");
    }

    int CollectVisualCombines(GameObject root, TileKind kind, Dictionary<VisualGroupKey, List<CombineInstance>> groups)
    {
        int count = 0;
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        Vector2Int chunk = GetChunkCoord(root.transform.position, Mathf.Max(1, config.combinedChunkSizeInTiles), config.tileSize);
        ShadowCastingMode shadowMode = ShouldCastShadows(kind) ? ShadowCastingMode.On : ShadowCastingMode.Off;
        Material overrideMat = GetMaterialForKind(kind);

        foreach (var filter in filters)
        {
            if (filter.sharedMesh == null) continue;

            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled) continue;

            var mesh = filter.sharedMesh;
            var materials = renderer.sharedMaterials;
            int subMeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
            for (int i = 0; i < subMeshCount; i++)
            {
                var material = overrideMat != null ? overrideMat : materials[i];
                if (material == null) continue;

                var key = new VisualGroupKey
                {
                    overrideMaterial = material,
                    chunk = chunk,
                    shadowCastingMode = shadowMode,
                };

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<CombineInstance>();
                    groups.Add(key, list);
                }

                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = i,
                    transform = toLocal * filter.transform.localToWorldMatrix,
                });
            }

            count++;
        }

        return count;
    }

    int CreateCombinedVisualMeshes(Dictionary<VisualGroupKey, List<CombineInstance>> groups)
    {
        int combinedRendererCount = 0;
        int batchSize = Mathf.Max(1, config.combinedMeshBatchSize);

        foreach (var kvp in groups)
        {
            var key = kvp.Key;
            var combines = kvp.Value;
            for (int start = 0; start < combines.Count; start += batchSize)
            {
                int count = Mathf.Min(batchSize, combines.Count - start);
                var slice = combines.GetRange(start, count);

                var go = new GameObject($"Combined_{key.overrideMaterial.name}_{key.chunk.x}_{key.chunk.y}_{start / batchSize}");
                go.transform.SetParent(transform, false);

                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                var mesh = new Mesh
                {
                    name = go.name,
                    indexFormat = config.combinedMeshIndexFormat,
                };
                mesh.CombineMeshes(slice.ToArray(), true, true, false);

                filter.sharedMesh = mesh;
                renderer.sharedMaterial = key.overrideMaterial;
                renderer.shadowCastingMode = key.shadowCastingMode;
                renderer.receiveShadows = key.shadowCastingMode != ShadowCastingMode.Off;

                if (key.shadowCastingMode != ShadowCastingMode.Off && config.shadowCullDistance > 0f)
                {
                    var culler = go.AddComponent<SettlementShadowCuller>();
                    culler.shadowCullDistance = config.shadowCullDistance;
                }

                optimizationObjects.Add(go);
                combinedRendererCount++;
            }
        }

        return combinedRendererCount;
    }

    int CreateCombinedColliderMeshes(List<TilePlacement> placements, float tileSize)
    {
        var colliderGroups = new Dictionary<Vector2Int, List<CombineInstance>>();
        var cubeMesh = GetCubeMesh();
        float chunkTiles = Mathf.Max(1, config.combinedChunkSizeInTiles);
        float wallThickness = Mathf.Max(0.15f, tileSize * 0.08f);
        float slabThickness = Mathf.Max(0.15f, tileSize * 0.08f);

        foreach (var placement in placements)
        {
            if (!ShouldBakeCollision(placement.kind))
                continue;

            var chunk = GetChunkCoord(
                new Vector3(placement.cell.x * tileSize, 0f, placement.cell.z * tileSize),
                (int)chunkTiles,
                tileSize);

            if (!colliderGroups.TryGetValue(chunk, out var combines))
            {
                combines = new List<CombineInstance>();
                colliderGroups.Add(chunk, combines);
            }

            AddColliderShape(combines, cubeMesh, placement, tileSize, wallThickness, slabThickness);
        }

        int combinedColliderCount = 0;
        int batchSize = Mathf.Max(1, config.combinedColliderBatchSize);

        foreach (var kvp in colliderGroups)
        {
            var chunk = kvp.Key;
            var combines = kvp.Value;
            for (int start = 0; start < combines.Count; start += batchSize)
            {
                int count = Mathf.Min(batchSize, combines.Count - start);
                var slice = combines.GetRange(start, count);

                var go = new GameObject($"CombinedCollider_{chunk.x}_{chunk.y}_{start / batchSize}");
                go.transform.SetParent(transform, false);

                var filter = go.AddComponent<MeshFilter>();
                var collider = go.AddComponent<MeshCollider>();
                var mesh = new Mesh
                {
                    name = go.name,
                    indexFormat = config.combinedMeshIndexFormat,
                };
                mesh.CombineMeshes(slice.ToArray(), true, true, false);

                filter.sharedMesh = mesh;
                collider.sharedMesh = mesh;
                collider.convex = false;

                optimizationObjects.Add(go);
                combinedColliderCount++;
            }
        }

        return combinedColliderCount;
    }

    static void AddColliderShape(
        List<CombineInstance> combines,
        Mesh cubeMesh,
        TilePlacement placement,
        float tileSize,
        float wallThickness,
        float slabThickness)
    {
        Vector3 basePos = new Vector3(
            placement.cell.x * tileSize,
            placement.cell.y * tileSize,
            placement.cell.z * tileSize);

        switch (placement.kind)
        {
            case TileKind.Floor:
            case TileKind.InteriorFloor:
            case TileKind.Roof:
            case TileKind.Bridge:
                AddBox(combines, cubeMesh,
                    basePos + new Vector3(0f, slabThickness * 0.5f, 0f),
                    Quaternion.identity,
                    new Vector3(tileSize, slabThickness, tileSize));
                break;

            case TileKind.CircularFeature:
                AddBox(combines, cubeMesh,
                    basePos + new Vector3(0f, tileSize * 2.8f, 0f),
                    Quaternion.identity,
                    new Vector3(tileSize * 2.1f, tileSize * 5.6f, tileSize * 2.1f));
                break;

            case TileKind.Stair:
            case TileKind.ExteriorRamp:
            case TileKind.TriangularFeature:
                AddBox(combines, cubeMesh,
                    basePos + new Vector3(0f, tileSize * 0.5f, 0f),
                    Quaternion.identity,
                    new Vector3(tileSize, tileSize, tileSize));
                break;

            case TileKind.WallGrandArch:
                AddArchBoxes(combines, cubeMesh, basePos, placement.face, tileSize, wallThickness);
                break;

            default:
                if (IsWallKind(placement.kind) || placement.kind == TileKind.GrandArch || placement.kind == TileKind.AqueductArch)
                {
                    Vector3 normal = FaceNormal(placement.face);
                    Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
                    Vector3 center = basePos + new Vector3(0f, tileSize * 0.5f, 0f) + normal * (tileSize * 0.5f - wallThickness * 0.5f);
                    AddBox(combines, cubeMesh, center, rotation, new Vector3(tileSize, tileSize, wallThickness));
                }
                break;
        }
    }

    static void AddArchBoxes(List<CombineInstance> combines, Mesh cubeMesh, Vector3 basePos, WallFace face, float tileSize, float wallThickness)
    {
        Vector3 normal = FaceNormal(face);
        Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
        Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
        Vector3 wallCenter = basePos + new Vector3(0f, tileSize * 0.5f, 0f) + normal * (tileSize * 0.5f - wallThickness * 0.5f);

        AddBox(combines, cubeMesh,
            wallCenter - right * (tileSize * 0.32f),
            rotation,
            new Vector3(tileSize * 0.18f, tileSize, wallThickness));

        AddBox(combines, cubeMesh,
            wallCenter + right * (tileSize * 0.32f),
            rotation,
            new Vector3(tileSize * 0.18f, tileSize, wallThickness));

        AddBox(combines, cubeMesh,
            wallCenter + new Vector3(0f, tileSize * 0.33f, 0f),
            rotation,
            new Vector3(tileSize, tileSize * 0.22f, wallThickness));
    }

    static void AddBox(List<CombineInstance> combines, Mesh cubeMesh, Vector3 center, Quaternion rotation, Vector3 scale)
    {
        combines.Add(new CombineInstance
        {
            mesh = cubeMesh,
            transform = Matrix4x4.TRS(center, rotation, scale),
        });
    }

    static void RemoveColliders(GameObject root)
    {
        var colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (var collider in colliders)
            DestroyImmediate(collider);
    }

    static Vector2Int GetChunkCoord(Vector3 worldPos, int chunkSizeInTiles, float tileSize)
    {
        float chunkWorldSize = Mathf.Max(tileSize, chunkSizeInTiles * tileSize);
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / chunkWorldSize),
            Mathf.FloorToInt(worldPos.z / chunkWorldSize));
    }

    static Vector3 FaceNormal(WallFace face)
    {
        return face switch
        {
            WallFace.North => Vector3.forward,
            WallFace.East => Vector3.right,
            WallFace.South => Vector3.back,
            _ => Vector3.left,
        };
    }

    static bool IsWallKind(TileKind kind)
    {
        return kind == TileKind.WallMonolith ||
               kind == TileKind.WallSlitWindow ||
               kind == TileKind.WallGrandArch ||
               kind == TileKind.WallButtress ||
               kind == TileKind.WallPanel ||
               kind == TileKind.WallRelief ||
               kind == TileKind.WallTech ||
               kind == TileKind.WallVent;
    }

    static bool ShouldBakeCollision(TileKind kind)
    {
        return kind switch
        {
            TileKind.Floor => true,
            TileKind.InteriorFloor => true,
            TileKind.Roof => true,
            TileKind.WallMonolith => true,
            TileKind.WallSlitWindow => true,
            TileKind.WallGrandArch => true,
            TileKind.WallButtress => true,
            TileKind.WallPanel => true,
            TileKind.WallRelief => true,
            TileKind.WallTech => true,
            TileKind.WallVent => true,
            TileKind.CircularFeature => true,
            TileKind.TriangularFeature => true,
            TileKind.GrandArch => true,
            TileKind.Bridge => true,
            TileKind.AqueductArch => true,
            TileKind.Stair => true,
            TileKind.ExteriorRamp => true,
            _ => false,
        };
    }

    static bool ShouldCastShadows(TileKind kind)
    {
        return kind switch
        {
            TileKind.Floor => true,
            TileKind.InteriorFloor => true,
            TileKind.Roof => true,
            TileKind.WallMonolith => true,
            TileKind.WallSlitWindow => true,
            TileKind.WallGrandArch => true,
            TileKind.WallButtress => true,
            TileKind.WallRelief => true,
            TileKind.Bridge => true,
            TileKind.CircularFeature => true,
            TileKind.TriangularFeature => true,
            TileKind.GrandArch => true,
            TileKind.AqueductArch => true,
            TileKind.Stair => true,
            TileKind.ExteriorRamp => true,
            _ => false,
        };
    }

    static void MarkStatic(GameObject go)
    {
        go.isStatic = true;
#if UNITY_EDITOR
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.ReflectionProbeStatic);
#endif
    }

    static Mesh GetCubeMesh()
    {
        if (cachedCubeMesh != null) return cachedCubeMesh;

        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cachedCubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(primitive);
        return cachedCubeMesh;
    }
}
