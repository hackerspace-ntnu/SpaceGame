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
    const float P_BUTTRESS      = 0.14f;  // Lower to avoid castle-like ribbing
    const float P_PANEL         = 0.12f;  // Less even masonry repetition
    const float P_FRIEZE_WALL   = 0.00f;  // Friezes emitted separately per floor-level

    // Rooftop
    const float P_PARAPET       = 0.22f;  // Reduce castle battlement reads
    const float P_TEMPLE_TOP    = 0.18f;  // Less temple/castle crown language
    const float P_BALCONY       = 0.06f;

    // Bridges between buttresses
    const int   MAX_BRIDGE_GAP   = 5;
    const int   MIN_BRIDGE_FLOOR = 2;

    // -------------------------------------------------------------------------

    public static void PlaceDetails(
        SettlementLayout layout,
        List<TilePlacement> placements,
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        var footprint = new HashSet<Vector2Int>(layout.Heights.Keys);

        PlaceWalls(layout, footprint, placements, rng, settings);
        PlaceFriezes(layout, footprint, placements, rng);
        PlaceRooftop(layout, footprint, placements, rng, settings);
        PlaceColonnades(layout, placements, rng, settings);
        PlaceBalconies(layout, footprint, placements, rng);
        PlaceObelisks(layout, footprint, placements, rng);
        PlaceBoulders(layout, footprint, placements, rng);
        PlaceBridges(layout, footprint, placements, rng, settings);
        PlaceGrandArches(layout, footprint, placements, rng, settings);
        PlaceRuinedVerticalArches(layout, footprint, placements, rng, settings);
        PlaceCircularFeatures(layout, footprint, placements, rng, settings);
        PlaceTriangularFeatures(layout, footprint, placements, rng, settings);
        PlacePillarClusters(layout, footprint, placements, rng, settings);
    }

    // -------------------------------------------------------------------------
    // Walls
    // -------------------------------------------------------------------------

    static void PlaceWalls(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng,
        SettlementGenerationSettings settings)
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
                    var kind = ChooseWall(floor, h, role, rng, settings);

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

    static TileKind ChooseWall(int floor, int totalH, BlockRole role, System.Random rng, SettlementGenerationSettings settings)
    {
        float r = (float)rng.NextDouble();
        float normalizedFloor = totalH > 1 ? floor / (float)(totalH - 1) : 1f;
        float lowerHalfFade = Mathf.InverseLerp(0.1f, 0.55f, normalizedFloor);
        float archChance = Mathf.Clamp01(settings.archDensity * 0.55f * lowerHalfFade);
        float reliefChance = 0.05f * lowerHalfFade;
        float techChance = Mathf.Clamp01(settings.techDetailDensity * 0.45f * lowerHalfFade);
        float ventChance = Mathf.Clamp01(settings.ventDetailDensity * 0.35f * lowerHalfFade);
        float panelChance = Mathf.Lerp(0.03f, P_PANEL, lowerHalfFade);

        // Grand arch openings: only ground floor, only base/buttress wide sections
        if (floor <= 1 && (role == BlockRole.Monolith || role == BlockRole.Buttress))
        {
            if (r < archChance) return TileKind.WallGrandArch;
            r -= archChance;
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
        if (r < panelChance) return TileKind.WallPanel;
        r -= panelChance;

        // Large raised slab to break up blank wall planes with heavier sci-fi massing.
        if (floor >= 1 && floor < totalH - 1 && totalH >= 5)
        {
            if (r < reliefChance) return TileKind.WallRelief;
            r -= reliefChance;
        }

        // Futuristic detailing makes the large facade planes feel engineered.
        if (floor >= 1 && floor < totalH - 1)
        {
            if (r < techChance) return TileKind.WallTech;
            r -= techChance;
        }

        if (floor >= 1 && floor < totalH - 1 && r < ventChance)
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
        System.Random rng,
        SettlementGenerationSettings settings)
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
                if (exposed && rng.NextDouble() < settings.parapetDensity)
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

            if (rng.NextDouble() < settings.roofClutterDensity)
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

    // -------------------------------------------------------------------------
    // Colonnades
    // -------------------------------------------------------------------------

    static void PlaceColonnades(
        SettlementLayout layout,
        List<TilePlacement> placements,
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        if (settings.largePillarDensity <= 0.01f)
            return;

        int colHeight = rng.Next(3, 6); // columns are 3-5 floors tall

        foreach (var pos in layout.ColonnadePositions)
        {
            if (rng.NextDouble() > settings.largePillarDensity) continue;
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
                float normalizedFloor = h > 1 ? floor / (float)(h - 1) : 1f;
                float balconyChance = Mathf.Lerp(0.01f, P_BALCONY, Mathf.InverseLerp(0.4f, 0.8f, normalizedFloor));
                for (int d = 0; d < 4; d++)
                {
                    var nb   = col + SettlementGenerator.Dirs[d];
                    int nbH  = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                    bool exposed = nbH <= floor;
                    if (!exposed) continue;
                    if (rng.NextDouble() >= balconyChance) continue;

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
        System.Random rng,
        SettlementGenerationSettings settings)
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

                    if (rng.NextDouble() < Mathf.Lerp(0.25f, 0.9f, settings.archDensity))
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
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        int upperFeatureLimit = GetUpperFeatureFloorLimit(layout, settings);
        var archPlaced = new HashSet<Vector2Int>();

        foreach (var col in footprint)
        {
            var role = layout.Roles[col];
            if (role != BlockRole.Buttress && role != BlockRole.Monolith) continue;
            if (layout.Heights[col] > upperFeatureLimit + 2) continue;

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

                    if (rng.NextDouble() < Mathf.Lerp(0.25f, 0.95f, settings.archDensity))
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
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        int upperFeatureLimit = GetUpperFeatureFloorLimit(layout, settings);
        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            if (h < 5) continue;
            if (h > upperFeatureLimit + 2) continue;

            for (int d = 0; d < 4; d++)
            {
                var nb = col + SettlementGenerator.Dirs[d];
                int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                if (nbH > 1) continue;
                if (rng.NextDouble() > Mathf.Lerp(0.08f, 0.35f, settings.archDensity)) continue;

                int archFloor = Mathf.Min(h - 3, rng.Next(1, Mathf.Max(2, Mathf.Min(h - 2, upperFeatureLimit))));
                if (archFloor > upperFeatureLimit) continue;
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

    static void PlaceCircularFeatures(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        if (!settings.enableCircularFeatures) return;

        int upperFeatureLimit = GetUpperFeatureFloorLimit(layout, settings);
        var claimedCells = new HashSet<Vector3Int>();
        var reservedAnchors = new List<(Vector2Int col, int floor)>();

        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            if (h < layout.MinHeight + 4) continue;
            if (rng.NextDouble() > settings.circularFeatureDensity * 0.22f) continue;

            if (!TryGetFeatureFloor(col, h, footprint, layout, rng, upperFeatureLimit, out int featureFloor))
                continue;

            if (!HasWideTerraceSupport(col, featureFloor, footprint, layout))
                continue;

            if (IsNearReservedCircularFeature(col, featureFloor, reservedAnchors))
                continue;

            var featureCells = new List<Vector2Int> { col };
            foreach (var dir in SettlementGenerator.Dirs)
            {
                var ringCell = col + dir;
                if (!footprint.Contains(ringCell)) continue;
                if (layout.Heights[ringCell] <= featureFloor) continue;
                featureCells.Add(ringCell);
            }

            if (featureCells.Count < 3)
                continue;

            foreach (var featureCell in featureCells)
            {
                var cell = new Vector3Int(featureCell.x, featureFloor, featureCell.y);
                if (!claimedCells.Add(cell))
                    continue;

                placements.Add(new TilePlacement
                {
                    kind = TileKind.CircularFeature,
                    cell = cell,
                    face = WallFace.North,
                    role = layout.Roles[featureCell],
                    variant = rng.Next(0, 2),
                });
            }

            reservedAnchors.Add((col, featureFloor));

        }
    }

    static void PlaceTriangularFeatures(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        if (!settings.enableTriangularFeatures) return;

        int upperFeatureLimit = GetUpperFeatureFloorLimit(layout, settings);
        var claimedCells = new HashSet<Vector3Int>();

        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            if (h < layout.MinHeight + 4) continue;
            if (rng.NextDouble() > settings.triangularFeatureDensity * 0.65f) continue;

            if (!TryGetFeatureFloor(col, h, footprint, layout, rng, upperFeatureLimit, out int featureFloor))
                continue;
            if (!TryGetExposedFace(col, featureFloor, footprint, layout, rng, out WallFace face))
                continue;

            var featureCells = new List<(Vector2Int cell, WallFace face)> { (col, face) };
            Vector2Int leftDir;
            Vector2Int rightDir;
            GetLateralDirs(face, out leftDir, out rightDir);

            var leftCell = col + leftDir;
            if (footprint.Contains(leftCell) && layout.Heights[leftCell] > featureFloor)
                featureCells.Add((leftCell, face));

            var rightCell = col + rightDir;
            if (footprint.Contains(rightCell) && layout.Heights[rightCell] > featureFloor)
                featureCells.Add((rightCell, face));

            foreach (var feature in featureCells)
            {
                var cell = new Vector3Int(feature.cell.x, featureFloor, feature.cell.y);
                if (!claimedCells.Add(cell))
                    continue;

                placements.Add(new TilePlacement
                {
                    kind = TileKind.TriangularFeature,
                    cell = cell,
                    face = feature.face,
                    role = layout.Roles[feature.cell],
                    variant = rng.Next(0, 2),
                });
            }

        }
    }

    static void PlacePillarClusters(
        SettlementLayout layout,
        HashSet<Vector2Int> footprint,
        List<TilePlacement> placements,
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        int upperFeatureLimit = GetUpperFeatureFloorLimit(layout, settings);
        foreach (var col in footprint)
        {
            int h = layout.Heights[col];
            if (h < layout.MinHeight + 1) continue;

            bool exposed = false;
            for (int d = 0; d < 4; d++)
            {
                var nb = col + SettlementGenerator.Dirs[d];
                int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
                if (nbH < h)
                {
                    exposed = true;
                    break;
                }
            }
            if (!exposed) continue;

            int terraceFloor = GetHighestExposedNeighbourFloor(col, footprint, layout);
            int pillarBaseFloor = rng.NextDouble() < 0.55f
                ? 0
                : Mathf.Clamp(terraceFloor, 1, Mathf.Min(upperFeatureLimit, h - 3));

            if (pillarBaseFloor > upperFeatureLimit)
                continue;

            float normalizedBase = layout.MaxHeight > 1 ? pillarBaseFloor / (float)(layout.MaxHeight - 1) : 1f;
            float lowerHalfFade = Mathf.InverseLerp(0.2f, 0.65f, normalizedBase);

            if (rng.NextDouble() < settings.largePillarDensity * 0.12f * lowerHalfFade)
            {
                int largeHeight = rng.Next(2, 5);
                int pillarTop = Mathf.Min(pillarBaseFloor + largeHeight, upperFeatureLimit + 1);
                for (int floor = pillarBaseFloor; floor < pillarTop; floor++)
                {
                    placements.Add(new TilePlacement
                    {
                        kind = TileKind.ColonnadeColumn,
                        cell = new Vector3Int(col.x, floor, col.y),
                        face = WallFace.North,
                        role = layout.Roles[col],
                        variant = rng.Next(0, 2),
                    });
                }
            }

            if (rng.NextDouble() < settings.thinPillarDensity * 0.2f * lowerHalfFade)
            {
                int thinHeight = rng.Next(2, 6);
                WallFace corner = SettlementGenerator.DirFaces[rng.Next(4)];
                int pillarTop = Mathf.Min(pillarBaseFloor + thinHeight, upperFeatureLimit + 1);
                for (int floor = pillarBaseFloor; floor < pillarTop; floor++)
                {
                    placements.Add(new TilePlacement
                    {
                        kind = TileKind.Pillar,
                        cell = new Vector3Int(col.x, floor, col.y),
                        face = corner,
                        role = layout.Roles[col],
                        variant = rng.Next(0, 2),
                    });
                }
            }
        }
    }

    static int GetUpperFeatureFloorLimit(SettlementLayout layout, SettlementGenerationSettings settings)
    {
        return Mathf.Clamp(
            Mathf.FloorToInt(layout.MaxHeight * settings.upperFeatureCutoff),
            layout.MinHeight + 2,
            layout.MaxHeight - 4);
    }

    static bool TryGetFeatureFloor(
        Vector2Int col,
        int height,
        HashSet<Vector2Int> footprint,
        SettlementLayout layout,
        System.Random rng,
        int upperFeatureLimit,
        out int featureFloor)
    {
        var candidates = new List<int>();
        foreach (var dir in SettlementGenerator.Dirs)
        {
            var nb = col + dir;
            int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
            if (nbH < 1 || nbH >= height - 1 || nbH > upperFeatureLimit)
                continue;
            candidates.Add(nbH);
        }

        if (candidates.Count > 0)
        {
            featureFloor = candidates[rng.Next(candidates.Count)];
            return true;
        }

        int minFloor = Mathf.Max(1, layout.MinHeight);
        int maxFloor = Mathf.Min(height - 2, upperFeatureLimit);
        if (maxFloor < minFloor)
        {
            featureFloor = 0;
            return false;
        }

        featureFloor = rng.Next(minFloor, maxFloor + 1);
        return true;
    }

    static bool HasWideTerraceSupport(
        Vector2Int col,
        int floor,
        HashSet<Vector2Int> footprint,
        SettlementLayout layout)
    {
        int supportedNeighbours = 0;
        foreach (var dir in SettlementGenerator.Dirs)
        {
            var nb = col + dir;
            if (footprint.Contains(nb) && layout.Heights[nb] > floor)
                supportedNeighbours++;
        }

        return supportedNeighbours >= 2;
    }

    static bool TryGetExposedFace(
        Vector2Int col,
        int floor,
        HashSet<Vector2Int> footprint,
        SettlementLayout layout,
        System.Random rng,
        out WallFace face)
    {
        var faces = new List<WallFace>();
        for (int d = 0; d < 4; d++)
        {
            var nb = col + SettlementGenerator.Dirs[d];
            int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
            if (nbH <= floor)
                faces.Add(SettlementGenerator.DirFaces[d]);
        }

        if (faces.Count == 0)
        {
            face = WallFace.North;
            return false;
        }

        face = faces[rng.Next(faces.Count)];
        return true;
    }

    static void GetLateralDirs(WallFace face, out Vector2Int leftDir, out Vector2Int rightDir)
    {
        switch (face)
        {
            case WallFace.North:
            case WallFace.South:
                leftDir = Vector2Int.left;
                rightDir = Vector2Int.right;
                break;
            default:
                leftDir = Vector2Int.down;
                rightDir = Vector2Int.up;
                break;
        }
    }

    static int GetHighestExposedNeighbourFloor(
        Vector2Int col,
        HashSet<Vector2Int> footprint,
        SettlementLayout layout)
    {
        int best = 0;
        foreach (var dir in SettlementGenerator.Dirs)
        {
            var nb = col + dir;
            int nbH = footprint.Contains(nb) ? layout.Heights[nb] : 0;
            if (nbH < layout.Heights[col])
                best = Mathf.Max(best, nbH);
        }

        return best;
    }

    static bool IsNearReservedCircularFeature(
        Vector2Int col,
        int floor,
        List<(Vector2Int col, int floor)> reservedAnchors)
    {
        foreach (var reserved in reservedAnchors)
        {
            if (Mathf.Abs(reserved.floor - floor) > 3)
                continue;

            int dx = Mathf.Abs(reserved.col.x - col.x);
            int dz = Mathf.Abs(reserved.col.y - col.y);
            if (dx <= 2 && dz <= 2)
                return true;
        }

        return false;
    }
}
