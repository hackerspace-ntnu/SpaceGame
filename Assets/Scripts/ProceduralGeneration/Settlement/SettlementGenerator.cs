using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates alien ancient ruin generation:
///   1. SettlementLayout   — height-map + block roles
///   2. EmitStructural     — floors, roofs, exterior-only corner pillars
///   3. SettlementInterior — interior floor slabs + stair runs
///   4. SettlementDetailPlacer — walls, grand arches, colonnades, obelisks
/// </summary>
public static class SettlementGenerator
{
    public static List<TilePlacement> GenerateFull(
        int seed,
        int footprintRadius = 7,
        int maxHeight       = 14,
        int minHeight       = 3)
    {
        var rng    = new System.Random(seed);
        var layout = SettlementLayout.Build(rng, footprintRadius, maxHeight, minHeight);

        var tiles = EmitStructural(layout);
        SettlementInterior.Emit(layout, tiles, rng);
        SettlementDetailPlacer.PlaceDetails(layout, tiles, rng);

        return tiles;
    }

    // -------------------------------------------------------------------------
    // Cardinal directions (shared with other passes)
    // -------------------------------------------------------------------------

    public static readonly Vector2Int[] Dirs = {
        Vector2Int.up,    // North  (Z+)  index 0
        Vector2Int.right, // East   (X+)  index 1
        Vector2Int.down,  // South  (Z-)  index 2
        Vector2Int.left,  // West   (X-)  index 3
    };

    public static readonly WallFace[] DirFaces = {
        WallFace.North, WallFace.East, WallFace.South, WallFace.West
    };

    // Corner dedup offsets: which (dx,dz) in the 2× pillar grid belongs to each corner
    static readonly (int dx, int dz, WallFace corner)[] Corners = {
        (0, 0, WallFace.North),
        (1, 0, WallFace.East),
        (0, 1, WallFace.South),
        (1, 1, WallFace.West),
    };

    // -------------------------------------------------------------------------
    // Structural pass
    // -------------------------------------------------------------------------

    static List<TilePlacement> EmitStructural(SettlementLayout layout)
    {
        var placements    = new List<TilePlacement>(layout.Heights.Count * 6);
        var footprint     = new HashSet<Vector2Int>(layout.Heights.Keys);
        var placedPillars = new HashSet<Vector3Int>();

        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];

            for (int floor = 0; floor < h; floor++)
            {
                var cell = new Vector3Int(col.x, floor, col.y);

                // ── Ground floor slab ──────────────────────────────────────────
                if (floor == 0)
                    placements.Add(T(TileKind.Floor, cell, WallFace.North, role));

                // ── Terrace edge floor ─────────────────────────────────────────
                // Any cell where at least one neighbour is lower gets a slab.
                // Interior cells at upper floors are handled by SettlementInterior.
                if (floor > 0)
                {
                    bool terraceEdge = false;
                    foreach (var dir in Dirs)
                    {
                        int nbH = footprint.Contains(col + dir) ? layout.Heights[col + dir] : 0;
                        if (nbH <= floor) { terraceEdge = true; break; }
                    }
                    if (terraceEdge)
                        placements.Add(T(TileKind.Floor, cell, WallFace.North, role));
                }

                // ── Roof slab ──────────────────────────────────────────────────
                if (floor == h - 1)
                    placements.Add(T(TileKind.Roof,
                        new Vector3Int(col.x, floor + 1, col.y), WallFace.North, role));

                // ── Exterior-only corner pillars ───────────────────────────────
                EmitExteriorPillars(col, floor, footprint, layout.Heights,
                    placedPillars, placements, role);
            }
        }

        return placements;
    }

    /// <summary>
    /// Only emits a pillar at a grid corner if that corner is on the outer
    /// boundary — i.e. at least one of the 3 tiles sharing that corner is
    /// absent or shorter than the current floor.
    /// </summary>
    static void EmitExteriorPillars(
        Vector2Int col, int floor,
        HashSet<Vector2Int> footprint,
        Dictionary<Vector2Int, int> heights,
        HashSet<Vector3Int> placed,
        List<TilePlacement> placements,
        BlockRole role)
    {
        foreach (var (dx, dz, corner) in Corners)
        {
            var key = new Vector3Int(col.x * 2 + dx, floor, col.y * 2 + dz);
            if (!placed.Add(key)) continue;

            int dx2 = dx == 0 ? -1 : 1;
            int dz2 = dz == 0 ? -1 : 1;
            var n1  = new Vector2Int(col.x + dx2, col.y);
            var n2  = new Vector2Int(col.x,       col.y + dz2);
            var n3  = new Vector2Int(col.x + dx2, col.y + dz2);

            bool exterior =
                !footprint.Contains(n1) || heights[n1] <= floor ||
                !footprint.Contains(n2) || heights[n2] <= floor ||
                !footprint.Contains(n3) || heights[n3] <= floor;

            if (exterior)
            {
                placements.Add(new TilePlacement
                {
                    kind = TileKind.Pillar,
                    cell = new Vector3Int(col.x, floor, col.y),
                    face = corner,
                    role = role,
                });
            }
        }
    }

    static TilePlacement T(TileKind k, Vector3Int c, WallFace f, BlockRole r) =>
        new TilePlacement { kind = k, cell = c, face = f, role = r };
}
