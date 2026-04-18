using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates interior traversal geometry for alien ancient ruins.
///
/// Interior philosophy
/// ────────────────────
/// Ruins have LARGE ROOMS — not lots of small cells. The monolith has:
///   – A grand ground-floor hall (entire ground plan, no interior walls)
///   – A handful of elevated chambers at terrace levels
///   – Open atria where the courtyard carve removed upper floors
///   – Stair runs connecting every reachable height-step
///   – Exterior monumental ramps on the main entrance face
///
/// Interior floors are placed only where needed (upper floors inside
/// the solid mass). Stairs are placed at every height-step edge.
/// </summary>
public static class SettlementInterior
{
    const int MAX_STAIR_STEP = 4;

    // -------------------------------------------------------------------------

    public static void Emit(
        SettlementLayout layout,
        List<TilePlacement> placements,
        System.Random rng)
    {
        var footprint = new HashSet<Vector2Int>(layout.Heights.Keys);

        EmitInteriorFloors(layout, footprint, placements);
        EmitStairs(layout, footprint, placements, rng);
    }

    // -------------------------------------------------------------------------
    // Interior floor slabs
    // -------------------------------------------------------------------------

    static void EmitInteriorFloors(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements)
    {
        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];

            for (int floor = 1; floor < h; floor++)
            {
                // Skip: already gets a terrace-edge floor from structural pass
                bool hasTerraceFloor = false;
                foreach (var dir in SettlementGenerator.Dirs)
                {
                    int nbH = footprint.Contains(col + dir) ? layout.Heights[col + dir] : 0;
                    if (nbH <= floor) { hasTerraceFloor = true; break; }
                }
                if (hasTerraceFloor) continue;

                placements.Add(new TilePlacement
                {
                    kind    = TileKind.InteriorFloor,
                    cell    = new Vector3Int(col.x, floor, col.y),
                    face    = WallFace.North,
                    role    = role,
                });
            }
        }
    }

    // -------------------------------------------------------------------------
    // Stair runs
    // -------------------------------------------------------------------------

    static void EmitStairs(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        var occupied = new HashSet<(Vector2Int col, int floor, int dirIdx)>();

        // Collect all adjacent height steps
        var steps = new List<(Vector2Int col, int d, int fromH, int toH)>();
        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            for (int d = 0; d < 4; d++)
            {
                var nb  = col + SettlementGenerator.Dirs[d];
                int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                int delta = nbH - h;
                if (delta >= 1 && delta <= MAX_STAIR_STEP)
                    steps.Add((col, d, h, nbH));
            }
        }

        // Shuffle for unbiased distribution
        for (int i = steps.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (steps[i], steps[j]) = (steps[j], steps[i]);
        }

        foreach (var (col, d, fromH, toH) in steps)
        {
            int bottom = fromH - 1;
            int top    = toH   - 1;

            var edgeKey = (col, bottom, d);
            if (occupied.Contains(edgeKey)) continue;

            bool canPlace = true;
            for (int step = 0; step <= top - bottom; step++)
            {
                if (occupied.Contains((col + SettlementGenerator.Dirs[d] * step, bottom + step, d)))
                { canPlace = false; break; }
            }
            if (!canPlace) continue;

            for (int step = 0; step <= top - bottom; step++)
                occupied.Add((col + SettlementGenerator.Dirs[d] * step, bottom + step, d));

            EmitStairRun(col, d, bottom, top, layout.Roles[col], footprint, layout.Heights, placements);
        }

        // Exterior monumental ramps on the entrance face of the monolith base
        EmitExteriorRamps(layout, footprint, placements, rng, occupied);
    }

    static void EmitStairRun(
        Vector2Int startCol, int dirIdx,
        int fromFloor, int toFloor,
        BlockRole role,
        HashSet<Vector2Int> footprint,
        Dictionary<Vector2Int, int> heights,
        List<TilePlacement> placements)
    {
        int steps = toFloor - fromFloor;
        for (int i = 0; i < steps; i++)
        {
            var tileCol   = startCol + SettlementGenerator.Dirs[dirIdx] * i;
            int tileFloor = fromFloor + i;

            if (i > 0)
            {
                if (!footprint.Contains(tileCol)) break;
                if (heights[tileCol] <= tileFloor) break;
            }

            placements.Add(new TilePlacement
            {
                kind    = TileKind.Stair,
                cell    = new Vector3Int(tileCol.x, tileFloor, tileCol.y),
                face    = SettlementGenerator.DirFaces[dirIdx],
                role    = role,
                variant = 0,
            });
        }
    }

    static void EmitExteriorRamps(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng,
        HashSet<(Vector2Int, int, int)> occupied)
    {
        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];
            if (h < 2 || h > layout.MinHeight + 2) continue;

            for (int d = 0; d < 4; d++)
            {
                var nb  = col + SettlementGenerator.Dirs[d];
                // Only exterior faces (no solid neighbour)
                if (footprint.Contains(nb)) continue;

                var key = (col, 0, d);
                if (occupied.Contains(key)) continue;
                if (rng.NextDouble() > 0.60f) continue;

                occupied.Add(key);

                placements.Add(new TilePlacement
                {
                    kind    = TileKind.ExteriorRamp,
                    cell    = new Vector3Int(col.x, 0, col.y),
                    face    = SettlementGenerator.DirFaces[d],
                    role    = role,
                    variant = 0,
                });
                break;
            }
        }
    }
}
