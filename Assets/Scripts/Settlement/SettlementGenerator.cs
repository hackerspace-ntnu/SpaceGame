using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Generates a chaotic desert settlement:
/// - 1-3 large base buildings as the foundation
/// - Towers shooting up from and around the bases
/// - Terraces: exposed rooftop areas at mid-heights
/// - Standalone pillars scattered around
/// - Small satellite structures connected or near the main mass
/// </summary>
public static class SettlementGenerator
{
    public enum WallFace { North, East, South, West }
    public enum TileKind { Floor, Roof, Wall, Pillar }

    public struct TilePlacement
    {
        public TileKind kind;
        public Vector3Int cell;
        public WallFace face;
    }

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    public static List<TilePlacement> Generate(int seed, int footprintRadius = 6, int maxHeight = 8, int minHeight = 2)
    {
        var rng = new System.Random(seed);
        var placements = new List<TilePlacement>();

        // Height map: every (x,z) column has a solid height
        var solidColumns = new Dictionary<Vector2Int, int>();

        // Step 1: 1-3 large base buildings
        int baseCount = rng.Next(1, 4);
        var baseCenters = new List<Vector2Int>();
        for (int i = 0; i < baseCount; i++)
        {
            var center = RandomPoint(rng, footprintRadius - 3);
            baseCenters.Add(center);
            int w = rng.Next(3, 6);
            int d = rng.Next(3, 6);
            int h = rng.Next(minHeight, minHeight + 2);
            CarveRect(solidColumns, center, w, d, h, rng, jitter: 0);
        }

        // Step 2: towers — tall narrow extrusions on and around bases
        int towerCount = rng.Next(4, 9);
        for (int i = 0; i < towerCount; i++)
        {
            // Prefer placing towers on or near a base center
            var anchor = baseCenters[rng.Next(baseCenters.Count)];
            var offset = new Vector2Int(rng.Next(-4, 5), rng.Next(-4, 5));
            var center = anchor + offset;
            int w = rng.Next(1, 4);
            int d = rng.Next(1, 4);
            int h = rng.Next(minHeight + 2, maxHeight + 1);
            CarveRect(solidColumns, center, w, d, h, rng, jitter: 1);
        }

        // Step 3: terraces — wide flat structures at mid height, creates ledges
        int terraceCount = rng.Next(2, 5);
        for (int i = 0; i < terraceCount; i++)
        {
            var anchor = baseCenters[rng.Next(baseCenters.Count)];
            var offset = new Vector2Int(rng.Next(-5, 6), rng.Next(-5, 6));
            var center = anchor + offset;
            int w = rng.Next(2, 5);
            int d = rng.Next(2, 5);
            int h = rng.Next(1, minHeight + 1);
            CarveRect(solidColumns, center, w, d, h, rng, jitter: 0);
        }

        // Step 4: small satellite buildings scattered further out
        int satelliteCount = rng.Next(3, 7);
        for (int i = 0; i < satelliteCount; i++)
        {
            var center = RandomPoint(rng, footprintRadius);
            int w = rng.Next(1, 3);
            int d = rng.Next(1, 3);
            int h = rng.Next(minHeight, minHeight + 3);
            CarveRect(solidColumns, center, w, d, h, rng, jitter: 0);
        }

        // Step 5: emit all geometry from the column height map
        var footprint = new HashSet<Vector2Int>(solidColumns.Keys);
        EmitTiles(footprint, solidColumns, placements);

        // Step 6: standalone decorative pillars scattered around the settlement
        int decorPillarCount = rng.Next(4, 10);
        var fpList = footprint.ToList();
        var placedDecorPillars = new HashSet<Vector2Int>();
        for (int i = 0; i < decorPillarCount; i++)
        {
            // Place near but outside the main footprint
            var near = fpList[rng.Next(fpList.Count)];
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            var dir = dirs[rng.Next(4)];
            var pos = near + dir * rng.Next(1, 3);
            if (footprint.Contains(pos) || placedDecorPillars.Contains(pos)) continue;
            placedDecorPillars.Add(pos);

            int pillarHeight = rng.Next(1, maxHeight);
            for (int y = 0; y < pillarHeight; y++)
            {
                placements.Add(new TilePlacement
                {
                    kind = TileKind.Pillar,
                    cell = new Vector3Int(pos.x, y, pos.y),
                    face = WallFace.North // corner index 0 — standalone pillar uses single corner
                });
            }
        }

        return placements;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static Vector2Int RandomPoint(System.Random rng, int radius)
        => new Vector2Int(rng.Next(-radius, radius + 1), rng.Next(-radius, radius + 1));

    /// Fills a rectangle of columns into the height map, taking the MAX height
    /// so overlapping regions keep the tallest value (towers win over bases).
    static void CarveRect(
        Dictionary<Vector2Int, int> columns,
        Vector2Int center, int w, int d, int h,
        System.Random rng, int jitter)
    {
        for (int x = -w / 2; x <= w / 2; x++)
        for (int z = -d / 2; z <= d / 2; z++)
        {
            var cell = center + new Vector2Int(x, z);
            int height = h + rng.Next(-jitter, jitter + 1);
            height = Mathf.Max(1, height);
            if (!columns.TryGetValue(cell, out int existing) || height > existing)
                columns[cell] = height;
        }
    }

    // -------------------------------------------------------------------------
    // Tile emission
    // -------------------------------------------------------------------------

    static readonly Vector2Int[] Dirs = {
        Vector2Int.up,    // North (Z+)
        Vector2Int.right, // East  (X+)
        Vector2Int.down,  // South (Z-)
        Vector2Int.left,  // West  (X-)
    };

    static readonly WallFace[] Faces = {
        WallFace.North, WallFace.East, WallFace.South, WallFace.West
    };

    static readonly (int dx, int dz, WallFace cornerIndex)[] Corners = {
        (0, 0, WallFace.North),
        (1, 0, WallFace.East),
        (0, 1, WallFace.South),
        (1, 1, WallFace.West),
    };

    static void EmitTiles(
        HashSet<Vector2Int> footprint,
        Dictionary<Vector2Int, int> heights,
        List<TilePlacement> placements)
    {
        var placedPillars = new HashSet<Vector3Int>();

        foreach (var col in footprint)
        {
            int h = heights[col];

            for (int floor = 0; floor < h; floor++)
            {
                var cell = new Vector3Int(col.x, floor, col.y);

                // Floor on ground level only
                if (floor == 0)
                    placements.Add(new TilePlacement { kind = TileKind.Floor, cell = cell });

                // Terrace floor: place a floor wherever this column is taller than its neighbour
                // This creates visible walkable ledges at every height step
                if (floor > 0)
                {
                    bool isTerraceEdge = false;
                    foreach (var dir in Dirs)
                    {
                        var n = col + dir;
                        int nHeight = footprint.Contains(n) ? heights[n] : 0;
                        if (nHeight <= floor) { isTerraceEdge = true; break; }
                    }
                    if (isTerraceEdge)
                        placements.Add(new TilePlacement { kind = TileKind.Floor, cell = cell });
                }

                // Roof on top
                if (floor == h - 1)
                    placements.Add(new TilePlacement { kind = TileKind.Roof, cell = new Vector3Int(col.x, floor + 1, col.y) });

                // Walls on exposed faces
                for (int d = 0; d < 4; d++)
                {
                    var neighbour2D = col + Dirs[d];
                    bool neighbourExists = footprint.Contains(neighbour2D);
                    int neighbourHeight = neighbourExists ? heights[neighbour2D] : 0;

                    if (!neighbourExists || neighbourHeight <= floor)
                    {
                        placements.Add(new TilePlacement
                        {
                            kind = TileKind.Wall,
                            cell = cell,
                            face = Faces[d]
                        });
                    }
                }

                // Pillars at corners
                EmitPillars(cell, placedPillars, placements);
            }
        }
    }

    static void EmitPillars(
        Vector3Int cell,
        HashSet<Vector3Int> placed,
        List<TilePlacement> placements)
    {
        foreach (var (dx, dz, cornerIndex) in Corners)
        {
            var key = new Vector3Int(cell.x * 2 + dx, cell.y, cell.z * 2 + dz);
            if (placed.Add(key))
            {
                placements.Add(new TilePlacement
                {
                    kind = TileKind.Pillar,
                    cell = cell,
                    face = cornerIndex
                });
            }
        }
    }
}
