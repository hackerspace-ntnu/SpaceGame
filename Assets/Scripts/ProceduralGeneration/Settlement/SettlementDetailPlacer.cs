using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads a SettlementLayout and appends facade, rooftop, colonnade,
/// obelisk, boulder, and bridge details to the placement list.
///
/// Design principles for alien ancient ruins:
///   – Walls are MONOLITHIC by default — blank stone planes dominate.
///   – Windows are RARE, TALL, and NARROW (slit-windows only).
///   – Arches are BIG — grand arches only at ground floor entrances.
///   – Buttresses are HEAVY — thick piers on lower floors of tall sections.
///   – Friezes mark floor-level transitions like a horizontal datum.
///   – Colonnades stand alone outside the main mass — no walls behind them.
///   – Obelisks are placed outside the footprint as sentinel markers.
///   – Grand arches span gaps between buttress wings.
///   – Top of structure gets a temple-top feature and roof parapets.
///   – Boulders placed at BoulderCore positions and scattered outside.
/// </summary>
public static class SettlementDetailPlacer
{
    // ── Probabilities ─────────────────────────────────────────────────────────

    // Wall surface selection per exposed face
    const float P_SLIT_WINDOW   = 0.04f;  // Rare slit windows
    const float P_GRAND_ARCH    = 0.38f;  // More ancient arch language
    const float P_BUTTRESS      = 0.14f;  // Lower to avoid castle-like ribbing
    const float P_PANEL         = 0.12f;  // Less even masonry repetition
    const float P_TECH          = 0.34f;  // Futuristic greeble panels
    const float P_VENT          = 0.22f;  // Vent/mechanical surface strips
    const float P_FRIEZE_WALL   = 0.00f;  // Friezes emitted separately per floor-level

    // Rooftop
    const float P_PARAPET       = 0.22f;  // Reduce castle battlement reads
    const float P_TEMPLE_TOP    = 0.18f;  // Less temple/castle crown language
    const float P_ROOF_MACHINE  = 0.72f;
    const float P_BALCONY       = 0.06f;
    const float P_TOP_CLUTTER   = 0.52f;

    // Bridges between buttresses
    const int   MAX_BRIDGE_GAP   = 5;
    const int   MIN_BRIDGE_FLOOR = 2;

    // -------------------------------------------------------------------------

    public static void PlaceDetails(
        SettlementLayout layout,
        List<TilePlacement> placements,
        System.Random rng)
    {
        var footprint = new HashSet<Vector2Int>(layout.Heights.Keys);

        PlaceWalls(layout, footprint, placements, rng);
        PlaceFriezes(layout, footprint, placements, rng);
        PlaceRooftop(layout, footprint, placements, rng);
        PlaceTopClutter(layout, footprint, placements, rng);
        PlaceColonnades(layout, placements, rng);
        PlaceBalconies(layout, footprint, placements, rng);
        PlaceObelisks(layout, footprint, placements, rng);
        PlaceBoulders(layout, footprint, placements, rng);
        PlaceBridges(layout, footprint, placements, rng);
        PlaceGrandArches(layout, footprint, placements, rng);
        PlaceRuinedVerticalArches(layout, footprint, placements, rng);
    }

    // -------------------------------------------------------------------------
    // Walls
    // -------------------------------------------------------------------------

    static void PlaceWalls(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];

            for (int floor = 0; floor < h; floor++)
            {
                var cell = new Vector3Int(col.x, floor, col.y);

                for (int d = 0; d < 4; d++)
                {
                    var nb   = col + SettlementGenerator.Dirs[d];
                    int nbH  = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                    bool exposed = nbH <= floor;
                    if (!exposed) continue;

                    var face = SettlementGenerator.DirFaces[d];
                    var kind = ChooseWall(floor, h, role, rng);

                    placements.Add(new TilePlacement
                    {
                        kind    = kind,
                        cell    = cell,
                        face    = face,
                        role    = role,
                        variant = rng.Next(0, 3),
                    });
                }
            }
        }
    }

    static TileKind ChooseWall(int floor, int totalH, BlockRole role, System.Random rng)
    {
        float r = (float)rng.NextDouble();

        // Grand arch openings: only ground floor, only base/buttress wide sections
        if (floor <= 1 && (role == BlockRole.Monolith || role == BlockRole.Buttress))
        {
            if (r < P_GRAND_ARCH) return TileKind.WallGrandArch;
            r -= P_GRAND_ARCH;
        }

        // Heavy buttress piers: lower half of tall structures
        if (floor < totalH / 3 && totalH >= 5)
        {
            if (r < P_BUTTRESS) return TileKind.WallButtress;
            r -= P_BUTTRESS;
        }

        // Slit windows: mid to upper floors only, very rare
        if (floor > 0 && floor < totalH - 1)
        {
            if (r < P_SLIT_WINDOW) return TileKind.WallSlitWindow;
            r -= P_SLIT_WINDOW;
        }

        // Shallow panel recess
        if (r < P_PANEL) return TileKind.WallPanel;
        r -= P_PANEL;

        // Futuristic detailing makes the large facade planes feel engineered.
        if (floor >= 1 && floor < totalH - 1)
        {
            if (r < P_TECH) return TileKind.WallTech;
            r -= P_TECH;
        }

        if (floor >= 1 && floor < totalH - 1 && r < P_VENT)
            return TileKind.WallVent;

        // Default: blank monolith wall
        return TileKind.WallMonolith;
    }

    // -------------------------------------------------------------------------
    // Friezes — horizontal bands at every terrace/tier transition
    // -------------------------------------------------------------------------

    static void PlaceFriezes(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        // Emit a frieze on every exposed edge where this column is taller
        // than its neighbour — i.e., at every height-step ledge face.
        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];

            for (int d = 0; d < 4; d++)
            {
                var nb  = col + SettlementGenerator.Dirs[d];
                int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;

                // Frieze at the first exposed floor above the neighbour's roof
                int friezeFloor = nbH; // the floor level sitting on top of the lower block
                if (friezeFloor >= h) continue; // neighbour same or taller

                placements.Add(new TilePlacement
                {
                    kind    = TileKind.Frieze,
                    cell    = new Vector3Int(col.x, friezeFloor, col.y),
                    face    = SettlementGenerator.DirFaces[d],
                    role    = role,
                    variant = rng.Next(0, 2),
                });
            }
        }
    }

    // -------------------------------------------------------------------------
    // Rooftop
    // -------------------------------------------------------------------------

    static void PlaceRooftop(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];
            var roof = new Vector3Int(col.x, h, col.y);

            // Parapet on every exposed roof edge
            for (int d = 0; d < 4; d++)
            {
                var nb  = col + SettlementGenerator.Dirs[d];
                bool exposed = !footprint.Contains(nb) || layout.Heights[nb] < h;
                if (exposed && rng.NextDouble() < P_PARAPET)
                {
                    placements.Add(new TilePlacement
                    {
                        kind    = TileKind.RoofParapet,
                        cell    = roof,
                        face    = SettlementGenerator.DirFaces[d],
                        role    = role,
                        variant = rng.Next(0, 2),
                    });
                }
            }

            // Temple-top: only on very tall interior columns
            if (h == layout.MaxHeight && rng.NextDouble() < P_TEMPLE_TOP)
            {
                bool interior = true;
                foreach (var dir in SettlementGenerator.Dirs)
                {
                    var nb = col + dir;
                    if (!footprint.Contains(nb) || layout.Heights[nb] < h - 1)
                    { interior = false; break; }
                }
                if (interior)
                {
                    placements.Add(new TilePlacement
                    {
                        kind    = TileKind.RoofTemple,
                        cell    = roof,
                        face    = WallFace.North,
                        role    = role,
                        variant = rng.Next(0, 2),
                    });
                }
            }

            if (rng.NextDouble() < P_ROOF_MACHINE)
            {
                bool supported = true;
                foreach (var dir in SettlementGenerator.Dirs)
                {
                    var nb = col + dir;
                    if (!footprint.Contains(nb) || layout.Heights[nb] < h - 1)
                    {
                        supported = false;
                        break;
                    }
                }

                if (supported)
                {
                    placements.Add(new TilePlacement
                    {
                        kind    = TileKind.RoofMachinery,
                        cell    = roof,
                        face    = WallFace.North,
                        role    = role,
                        variant = rng.Next(0, 2),
                    });

                    if (rng.NextDouble() < 0.45f)
                    {
                        placements.Add(new TilePlacement
                        {
                            kind    = TileKind.RoofMachinery,
                            cell    = roof + new Vector3Int(0, 1, 0),
                            face    = WallFace.North,
                            role    = role,
                            variant = rng.Next(0, 2),
                        });
                    }
                }
            }
        }
    }

    static void PlaceTopClutter(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        int topBand = Mathf.Max(layout.MinHeight + 2, layout.MaxHeight - 3);

        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            if (h < topBand) continue;
            if (rng.NextDouble() > P_TOP_CLUTTER) continue;

            int stackHeight = 1;
            if (rng.NextDouble() < 0.6f) stackHeight++;
            if (rng.NextDouble() < 0.35f) stackHeight++;

            for (int i = 0; i < stackHeight; i++)
            {
                placements.Add(new TilePlacement
                {
                    kind = TileKind.RoofMachinery,
                    cell = new Vector3Int(col.x, h + i, col.y),
                    face = WallFace.North,
                    role = layout.Roles[col],
                    variant = rng.Next(0, 2),
                });
            }
        }
    }

    // -------------------------------------------------------------------------
    // Colonnades
    // -------------------------------------------------------------------------

    static void PlaceColonnades(
        SettlementLayout layout,
        List<TilePlacement> placements,
        System.Random rng)
    {
        int colHeight = rng.Next(3, 6); // columns are 3-5 floors tall

        foreach (var pos in layout.ColonnadePositions)
        {
            for (int floor = 0; floor < colHeight; floor++)
            {
                placements.Add(new TilePlacement
                {
                    kind    = TileKind.ColonnadeColumn,
                    cell    = new Vector3Int(pos.x, floor, pos.y),
                    face    = WallFace.North,
                    role    = BlockRole.Colonnade,
                    variant = rng.Next(0, 2),
                });
            }
        }
    }

    // -------------------------------------------------------------------------
    // Obelisks
    // -------------------------------------------------------------------------

    static void PlaceObelisks(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        int count = rng.Next(2, 5);
        var fp    = new List<Vector2Int>(footprint);
        var used  = new HashSet<Vector2Int>();

        for (int i = 0; i < count; i++)
        {
            var near = fp[rng.Next(fp.Count)];
            var dir  = SettlementGenerator.Dirs[rng.Next(4)];
            var pos  = near + dir * rng.Next(2, 5);

            if (footprint.Contains(pos) || used.Contains(pos)) continue;
            // Also avoid colonnade positions
            if (layout.ColonnadePositions.Contains(pos)) continue;
            used.Add(pos);

            // Obelisk: one tall tile (builder stacks the mesh to correct height)
            placements.Add(new TilePlacement
            {
                kind    = TileKind.Obelisk,
                cell    = new Vector3Int(pos.x, 0, pos.y),
                face    = WallFace.North,
                role    = BlockRole.Monolith,
                variant = rng.Next(0, 2),
            });
        }
    }

    // -------------------------------------------------------------------------
    // Balconies / ledges
    // -------------------------------------------------------------------------

    static void PlaceBalconies(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        foreach (var col in footprint)
        {
            int h    = layout.Heights[col];
            var role = layout.Roles[col];
            if (h < 4) continue;

            for (int floor = 2; floor < h - 1; floor += 2)
            {
                for (int d = 0; d < 4; d++)
                {
                    var nb   = col + SettlementGenerator.Dirs[d];
                    int nbH  = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                    bool exposed = nbH <= floor;
                    if (!exposed) continue;
                    if (rng.NextDouble() >= P_BALCONY) continue;

                    placements.Add(new TilePlacement
                    {
                        kind    = TileKind.Balcony,
                        cell    = new Vector3Int(col.x, floor, col.y),
                        face    = SettlementGenerator.DirFaces[d],
                        role    = role,
                        variant = rng.Next(0, 2),
                    });
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Boulders
    // -------------------------------------------------------------------------

    static void PlaceBoulders(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        // Intentionally disabled: boulders add clutter and performance cost.
    }

    // -------------------------------------------------------------------------
    // Bridges between buttresses
    // -------------------------------------------------------------------------

    static void PlaceBridges(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        var buttressCols = new List<Vector2Int>();
        foreach (var kv in layout.Roles)
            if (kv.Value == BlockRole.Buttress) buttressCols.Add(kv.Key);

        var placed = new HashSet<(Vector2Int, Vector2Int)>();

        foreach (var a in buttressCols)
        {
            for (int d = 0; d < 4; d++)
            {
                var dir = SettlementGenerator.Dirs[d];

                for (int dist = 2; dist <= MAX_BRIDGE_GAP; dist++)
                {
                    var b = a + dir * dist;

                    bool clear = true;
                    for (int step = 1; step < dist; step++)
                        if (footprint.Contains(a + dir * step)) { clear = false; break; }
                    if (!clear) break;

                    if (!footprint.Contains(b)) continue;
                    if (layout.Roles[b] != BlockRole.Buttress &&
                        layout.Roles[b] != BlockRole.Monolith) continue;

                    int ha = layout.Heights[a], hb = layout.Heights[b];
                    if (Mathf.Abs(ha - hb) > 2 || ha < MIN_BRIDGE_FLOOR) continue;

                    var key = a.x < b.x || (a.x == b.x && a.y < b.y) ? (a,b) : (b,a);
                    if (placed.Contains(key)) continue;
                    placed.Add(key);

                    if (rng.NextDouble() < 0.8f)
                        EmitBridge(a, b, dir, dist, Mathf.Min(ha, hb) - 1, placements, rng);

                    break;
                }
            }
        }
    }

    static void EmitBridge(
        Vector2Int a, Vector2Int b,
        Vector2Int dir, int dist, int bridgeFloor,
        List<TilePlacement> placements,
        System.Random rng)
    {
        for (int step = 1; step < dist; step++)
        {
            var col  = a + dir * step;
            var cell = new Vector3Int(col.x, bridgeFloor, col.y);

            placements.Add(new TilePlacement
            {
                kind = TileKind.Bridge, cell = cell,
                face = WallFace.North, role = BlockRole.Monolith,
            });

            // Side parapets
            WallFace lf = (dir == Vector2Int.right || dir == Vector2Int.left)
                ? WallFace.North : WallFace.East;
            WallFace rf = (dir == Vector2Int.right || dir == Vector2Int.left)
                ? WallFace.South : WallFace.West;

            placements.Add(new TilePlacement { kind = TileKind.RoofParapet, cell = cell, face = lf, role = BlockRole.Monolith });
            placements.Add(new TilePlacement { kind = TileKind.RoofParapet, cell = cell, face = rf, role = BlockRole.Monolith });
        }
    }

    // -------------------------------------------------------------------------
    // Grand arches spanning buttress-to-buttress gaps
    // -------------------------------------------------------------------------

    static void PlaceGrandArches(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        var archPlaced = new HashSet<Vector2Int>();

        foreach (var col in footprint)
        {
            var role = layout.Roles[col];
            if (role != BlockRole.Buttress && role != BlockRole.Monolith) continue;

            foreach (var dir in SettlementGenerator.Dirs)
            {
                // 1-2 tile gap then another structural column
                for (int gap = 1; gap <= 2; gap++)
                {
                    var gapPos   = col + dir * gap;
                    var otherPos = col + dir * (gap + 1);

                    if (footprint.Contains(gapPos))   break; // no gap
                    if (!footprint.Contains(otherPos)) continue;

                    var otherRole = layout.Roles[otherPos];
                    if (otherRole != BlockRole.Buttress && otherRole != BlockRole.Monolith) continue;
                    if (archPlaced.Contains(gapPos)) continue;

                    if (rng.NextDouble() < 0.7f)
                    {
                        archPlaced.Add(gapPos);

                        // Facing: arch opening is perpendicular to the span
                        WallFace archFace = (dir == Vector2Int.right || dir == Vector2Int.left)
                            ? WallFace.North : WallFace.East;

                        placements.Add(new TilePlacement
                        {
                            kind    = TileKind.GrandArch,
                            cell    = new Vector3Int(gapPos.x, 0, gapPos.y),
                            face    = archFace,
                            role    = BlockRole.Monolith,
                            variant = rng.Next(0, 2),
                        });
                    }
                    break;
                }
            }
        }
    }

    static void PlaceRuinedVerticalArches(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng)
    {
        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            if (h < 5) continue;

            for (int d = 0; d < 4; d++)
            {
                var nb = col + SettlementGenerator.Dirs[d];
                int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                if (nbH > 1) continue;
                if (rng.NextDouble() > 0.18f) continue;

                int archFloor = Mathf.Min(h - 2, rng.Next(1, Mathf.Max(2, h - 2)));
                placements.Add(new TilePlacement
                {
                    kind    = TileKind.GrandArch,
                    cell    = new Vector3Int(col.x, archFloor, col.y),
                    face    = (d == 1 || d == 3) ? WallFace.North : WallFace.East,
                    role    = layout.Roles[col],
                    variant = rng.Next(0, 2),
                });
            }
        }
    }
}
