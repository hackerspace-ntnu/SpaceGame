using System.Collections.Generic;
using UnityEngine;
using static SettlementGenerator;

/// <summary>
/// Takes generator output and instantiates prefabs into the scene.
/// Attach to any GameObject. Call Build() to construct, Clear() to destroy.
/// </summary>
public class SettlementBuilder : MonoBehaviour
{
    [SerializeField] private SettlementPrefabConfig config;

    private readonly List<GameObject> spawnedObjects = new();

    // Wall at rest: faces +X, pivot at (0, 0.5, 0), placed at tile center.
    // Rotating around Y moves the face to other edges.
    // East  (+X): 0°   — default, wall on right side
    // North (+Z): 90°  — wall on far side  (Unity Y-rot is clockwise from above: +X->+Z at 90°... wait, no)
    // Unity rotates clockwise from above: at Y=90, +X becomes -Z. So:
    // East  (+X): 0°
    // South (-Z): 90°
    // West  (-X): 180°
    // North (+Z): 270°
    private static readonly Dictionary<WallFace, Quaternion> WallRotations = new()
    {
        { WallFace.East,  Quaternion.Euler(0,   0, 0) },
        { WallFace.South, Quaternion.Euler(0,  90, 0) },
        { WallFace.West,  Quaternion.Euler(0, 180, 0) },
        { WallFace.North, Quaternion.Euler(0, 270, 0) },
    };

    // Pillar corners relative to tile center (tile goes -0.5 to +0.5 on X and Z)
    private static readonly Vector2[] PillarCorners = {
        new(-0.5f, -0.5f),
        new( 0.5f, -0.5f),
        new(-0.5f,  0.5f),
        new( 0.5f,  0.5f),
    };

    public void Build(List<TilePlacement> placements, Vector3 worldOrigin)
    {
        Clear();

        float s = config.tileSize;

        foreach (var p in placements)
        {
            GameObject prefab = ResolvePrefab(p.kind);
            if (prefab == null)
            {
                Debug.LogWarning($"[SettlementBuilder] No prefab assigned for {p.kind}");
                continue;
            }

            Vector3 tileCenter = worldOrigin + new Vector3(p.cell.x * s, p.cell.y * s, p.cell.z * s);
            Vector3 pos;
            Quaternion rot;

            if (p.kind == TileKind.Wall)
            {
                rot = WallRotations[p.face];
                pos = tileCenter + rot * new Vector3(0.5f, 0, 0);
            }
            else if (p.kind == TileKind.Pillar)
            {
                // Pillar cell stores the corner index in face field (reused as int)
                int cornerIndex = (int)p.face;
                var corner = PillarCorners[cornerIndex];
                pos = tileCenter + new Vector3(corner.x * s, 0, corner.y * s);
                rot = Quaternion.identity;
            }
            else if (p.kind == TileKind.Roof)
            {
                // Roof slab is centered on Y, so shift up by half slab thickness
                // so its bottom face sits flush on top of the walls.
                pos = tileCenter + new Vector3(0, -0.5f, 0);
                rot = Quaternion.identity;
            }
            else
            {
                // Floor — shift down by 0.5 so it sits at the base of the cell
                pos = tileCenter + new Vector3(0, -0.5f, 0);
                rot = Quaternion.identity;
            }

            var go = Instantiate(prefab, pos, rot, transform);
            spawnedObjects.Add(go);
        }
    }

    public void Clear()
    {
        foreach (var go in spawnedObjects)
            if (go != null) DestroyImmediate(go);
        spawnedObjects.Clear();
    }

    GameObject ResolvePrefab(TileKind kind) => kind switch
    {
        TileKind.Floor   => config.floorPrefab,
        TileKind.Roof    => config.floorPrefab,
        TileKind.Wall    => config.wallPrefab,
        TileKind.Pillar  => config.pillarPrefab,
        _                => null
    };
}
