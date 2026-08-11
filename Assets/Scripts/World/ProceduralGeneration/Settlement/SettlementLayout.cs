using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates the height-map for alien ancient ruins.
///
/// Shape philosophy
/// ─────────────────
/// The settlement is a SINGLE MONOLITH MASS — not scattered buildings.
/// It has a strong base that tapers irregularly as it rises, like a
/// stepped pyramid that has eroded and been partially rebuilt over centuries.
///
/// Generation passes:
///   1. Core monolith   — one large rectangular base (widest, lowest N floors)
///   2. Ziggurat taper  — each tier above shrinks the footprint inward on
///                         random sides by 1-2 tiles, creating overhangs/setbacks
///   3. Inner courtyard — optionally carve a hollow center in the mid-levels
///                         to create a large interior room
///   4. Buttress wings  — 2-4 thick arms radiating from the base corners
///   5. Colonnade rows  — 1-2 rows of isolated pillar columns forming an
///                         approach corridor leading to a main face
///   6. Boulder core    — optionally mark a center zone as BoulderCore role
///                         (the builder will place large boulders there)
/// </summary>
public class SettlementLayout
{
    public readonly Dictionary<Vector2Int, int>       Heights = new();
    public readonly Dictionary<Vector2Int, BlockRole> Roles   = new();
    public readonly List<BuildingBlock>               Blocks  = new();

    // Colonnade positions are stored separately (they have no height, just columns)
    public readonly List<Vector2Int> ColonnadePositions = new();

    // Optional boulder-core center tiles
    public readonly HashSet<Vector2Int> BoulderCore = new();

    public int FootprintRadius { get; private set; }
    public int MaxHeight       { get; private set; }
    public int MinHeight       { get; private set; }

    // The axis-aligned bounding rect of the monolith base (used for entrances)
    public RectInt MonolithBase { get; private set; }

    public struct BuildingBlock
    {
        public RectInt   rect;
        public int       height;
        public BlockRole role;
    }

    // -------------------------------------------------------------------------
    // Entry point
    // -------------------------------------------------------------------------

    public static SettlementLayout Build(
        System.Random rng,
        int footprintRadius = 7,
        int maxHeight       = 20,
        int minHeight       = 3)
    {
        return Build(rng, new SettlementGenerationSettings
        {
            footprintRadius = footprintRadius,
            maxHeight = maxHeight,
            minHeight = minHeight,
        });
    }

    public static SettlementLayout Build(
        System.Random rng,
        SettlementGenerationSettings settings)
    {
        var l = new SettlementLayout
        {
            FootprintRadius = settings.footprintRadius,
            MaxHeight       = settings.maxHeight,
            MinHeight       = settings.minHeight,
        };
        l.Generate(rng, settings);
        return l;
    }

    // -------------------------------------------------------------------------
    // Generation
    // -------------------------------------------------------------------------

    void Generate(System.Random rng, SettlementGenerationSettings settings)
    {
        int radius = settings.footprintRadius;
        int maxH = settings.maxHeight;
        int minH = settings.minHeight;

        // ── 1. Core monolith base ─────────────────────────────────────────────
        int baseW = Mathf.Clamp(radius * 2 + rng.Next(-2, 1), 7, 11);
        int baseD = Mathf.Clamp(radius * 2 + rng.Next(-2, 1), 7, 11);
        int baseH = Mathf.Clamp(rng.Next(minH + 1, minH + 4), minH + 1, Mathf.Max(minH + 1, maxH - 5));
        var baseRect = new RectInt(-baseW / 2, -baseD / 2, baseW, baseD);
        MonolithBase = baseRect;
        FillRect(baseRect, baseH, BlockRole.Monolith);

        // Add attached lower wings, but keep them subordinate so the form still
        // reads as one rising ruin rather than a broad castle keep.
        int annexCount = rng.Next(1, 3);
        for (int i = 0; i < annexCount; i++)
        {
            int side   = rng.Next(4);
            int depth  = rng.Next(2, 4);
            int width  = rng.Next(2, Mathf.Max(3, (side < 2 ? baseD : baseW) - 2));
            int annexH = Mathf.Max(minH, baseH - rng.Next(1, 3));

            RectInt annex = side switch
            {
                0 => new RectInt(
                    rng.Next(baseRect.xMin + 1, Mathf.Max(baseRect.xMin + 2, baseRect.xMax - width)),
                    baseRect.yMax,
                    width,
                    depth),
                1 => new RectInt(
                    baseRect.xMax,
                    rng.Next(baseRect.yMin + 1, Mathf.Max(baseRect.yMin + 2, baseRect.yMax - width)),
                    depth,
                    width),
                2 => new RectInt(
                    rng.Next(baseRect.xMin + 1, Mathf.Max(baseRect.xMin + 2, baseRect.xMax - width)),
                    baseRect.yMin - depth,
                    width,
                    depth),
                _ => new RectInt(
                    baseRect.xMin - depth,
                    rng.Next(baseRect.yMin + 1, Mathf.Max(baseRect.yMin + 2, baseRect.yMax - width)),
                    depth,
                    width),
            };

            FillRect(annex, annexH, BlockRole.Buttress);
        }

        // ── 2. Ziggurat taper — build upward in tiers ─────────────────────────
        var tierRect  = baseRect;
        int tierFloor = baseH;
        int maxTiers  = 6;
        bool middleBulgeApplied = false;

        for (int tier = 0; tier < maxTiers && tierFloor < maxH - 2; tier++)
        {
            int tierH    = rng.Next(2, 5);   // floors in this tier
            int newFloor = Mathf.Min(tierFloor + tierH, maxH);

            // Keep the silhouette heavy: mostly shrink by 0-1 with occasional
            // deeper cuts to create terraces and width subsections.
            int shrinkL = rng.NextDouble() < 0.55 ? rng.Next(1, 3) : rng.Next(0, 2);
            int shrinkR = rng.NextDouble() < 0.55 ? rng.Next(1, 3) : rng.Next(0, 2);
            int shrinkB = rng.NextDouble() < 0.55 ? rng.Next(1, 3) : rng.Next(0, 2);
            int shrinkT = rng.NextDouble() < 0.55 ? rng.Next(1, 3) : rng.Next(0, 2);

            // Prevent over-shrinking below 3×3
            int newX  = tierRect.xMin + shrinkL;
            int newY  = tierRect.yMin + shrinkB;
            int newW  = tierRect.width  - shrinkL - shrinkR;
            int newH2 = tierRect.height - shrinkB - shrinkT;
            if (newW < 3) { newW = 3; newX = -1; }
            if (newH2 < 3) { newH2 = 3; newY = -1; }

            tierRect = new RectInt(newX, newY, newW, newH2);

            // Occasionally expand outward on one side (overhang over lower tier)
            bool canBulge = settings.allowMiddleBulge &&
                            !middleBulgeApplied &&
                            tier >= 1 &&
                            tier <= 3 &&
                            rng.NextDouble() < settings.middleBulgeChance;

            if (canBulge)
            {
                int bulgeX = rng.Next(1, settings.middleBulgeExtraWidth + 1);
                int bulgeZ = rng.Next(1, settings.middleBulgeExtraWidth + 1);
                tierRect = new RectInt(
                    tierRect.xMin - bulgeX,
                    tierRect.yMin - bulgeZ,
                    tierRect.width + bulgeX * 2,
                    tierRect.height + bulgeZ * 2);
                middleBulgeApplied = true;
            }

            if (rng.NextDouble() < settings.overhangChance)
            {
                int side = rng.Next(4);
                int push = rng.Next(1, settings.overhangMaxPush + 1);
                tierRect = side switch {
                    0 => new RectInt(tierRect.xMin - push, tierRect.yMin, tierRect.width + push, tierRect.height),
                    1 => new RectInt(tierRect.xMin, tierRect.yMin, tierRect.width + push, tierRect.height),
                    2 => new RectInt(tierRect.xMin, tierRect.yMin - push, tierRect.width, tierRect.height + push),
                    _ => new RectInt(tierRect.xMin, tierRect.yMin, tierRect.width, tierRect.height + push),
                };
            }

            // Fill this tier (MAX height wins — higher tiers override lower)
            for (int x = tierRect.xMin; x < tierRect.xMax; x++)
            for (int z = tierRect.yMin; z < tierRect.yMax; z++)
            {
                var col = new Vector2Int(x, z);
                if (!Heights.TryGetValue(col, out int cur) || newFloor > cur)
                {
                    Heights[col] = newFloor;
                    Roles[col]   = BlockRole.Monolith;
                }
            }
            Blocks.Add(new BuildingBlock { rect = tierRect, height = newFloor, role = BlockRole.Monolith });

            tierFloor = newFloor;
        }

        // Break the crown into many offset mini-masses so the silhouette reads
        // like stacked ruined blocks instead of a clean keep or single spire.
        int crownBandMin = Mathf.Max(baseH + 2, maxH - 5);
        int crownBandMax = maxH;

        var crownSeeds = new List<RectInt>();
        int crownSeedCount = rng.Next(3, 6);
        for (int i = 0; i < crownSeedCount; i++)
        {
            int seedW = rng.Next(2, 4);
            int seedD = rng.Next(2, 4);
            int seedX = rng.Next(baseRect.xMin + 1, Mathf.Max(baseRect.xMin + 2, baseRect.xMax - seedW));
            int seedZ = rng.Next(baseRect.yMin + 1, Mathf.Max(baseRect.yMin + 2, baseRect.yMax - seedD));
            int seedH = rng.Next(crownBandMin, crownBandMax + 1);
            var seedRect = new RectInt(seedX, seedZ, seedW, seedD);
            crownSeeds.Add(seedRect);
            FillRect(seedRect, seedH, BlockRole.Monolith);
        }

        // Grow lots of tiny stacked offsets around those seed masses.
        int blockyClusterCount = rng.Next(18, 30);
        for (int i = 0; i < blockyClusterCount; i++)
        {
            var seed = crownSeeds[rng.Next(crownSeeds.Count)];
            int w = rng.NextDouble() < 0.8 ? 1 : 2;
            int d = rng.NextDouble() < 0.8 ? 1 : 2;

            int x = rng.Next(seed.xMin - 1, seed.xMax + 1);
            int z = rng.Next(seed.yMin - 1, seed.yMax + 1);
            int h = rng.Next(crownBandMin, crownBandMax + 1);

            FillRect(new RectInt(x, z, w, d), h, BlockRole.Monolith);
        }

        // Add a few taller needles inside the broken crown so the tower still
        // feels like it rises upward through the messy top mass.
        int needleCount = rng.Next(3, 6);
        for (int i = 0; i < needleCount; i++)
        {
            int x = rng.Next(baseRect.xMin + 1, baseRect.xMax - 1);
            int z = rng.Next(baseRect.yMin + 1, baseRect.yMax - 1);
            int h = Mathf.Max(crownBandMin + 1, maxH - rng.Next(0, 2));
            FillRect(new RectInt(x, z, 1, 1), h, BlockRole.Monolith);
        }

        // ── 3. Carve an interior courtyard at mid-height (optional) ───────────
        // This punches a rectangular hollow through mid-levels, creating
        // a large atrium-like interior room open at the top.
        if (rng.NextDouble() < settings.ruinedVoidChance && baseW >= 7 && baseD >= 7)
        {
            int cx  = rng.Next(2, Mathf.Max(3, baseW - 3));
            int cz  = rng.Next(2, Mathf.Max(3, baseD - 3));
            int cw  = rng.Next(2, Mathf.Max(3, baseW - cx - 1));
            int cd  = rng.Next(2, Mathf.Max(3, baseD - cz - 1));
            int courtyardBaseX = baseRect.xMin + cx;
            int courtyardBaseZ = baseRect.yMin + cz;

            // Only hollow out floors above ground (keep ground floor solid)
            int hollowFrom = 1;
            int hollowTo   = Mathf.Min(maxH - 2, maxH / 2 + 1);

            for (int x = courtyardBaseX; x < courtyardBaseX + cw; x++)
            for (int z = courtyardBaseZ; z < courtyardBaseZ + cd; z++)
            {
                var col = new Vector2Int(x, z);
                if (Heights.TryGetValue(col, out int ch) && ch > hollowTo)
                {
                    Heights[col] = hollowFrom;
                }
            }
        }

        // ── 4. Buttress wings ─────────────────────────────────────────────────
        int buttressCount = rng.Next(1, 3);
        int baseCX = (baseRect.xMin + baseRect.xMax) / 2;
        int baseCY = (baseRect.yMin + baseRect.yMax) / 2;
        var edgePositions = new List<(Vector2Int center, Vector2Int dir)>
        {
            (new Vector2Int(baseRect.xMin,     baseCY),              Vector2Int.left),
            (new Vector2Int(baseRect.xMax - 1, baseCY),              Vector2Int.right),
            (new Vector2Int(baseCX,            baseRect.yMin),       Vector2Int.down),
            (new Vector2Int(baseCX,            baseRect.yMax - 1),   Vector2Int.up),
            (new Vector2Int(baseRect.xMin,     baseRect.yMin),       new Vector2Int(-1,-1)),
            (new Vector2Int(baseRect.xMax - 1, baseRect.yMin),       new Vector2Int( 1,-1)),
            (new Vector2Int(baseRect.xMin,     baseRect.yMax - 1),   new Vector2Int(-1, 1)),
            (new Vector2Int(baseRect.xMax - 1, baseRect.yMax - 1),   new Vector2Int( 1, 1)),
        };

        Shuffle(rng, edgePositions);
        for (int i = 0; i < buttressCount && i < edgePositions.Count; i++)
        {
            var (edgeCenter, outDir) = edgePositions[i];
            int bw    = rng.Next(2, 4);
            int bd    = rng.Next(2, 4);
            int bh    = Mathf.Max(minH + 1, baseH - rng.Next(1, 3));
            int reach = rng.Next(1, 3);

            var bCenter = edgeCenter + outDir * reach;
            var brect   = new RectInt(bCenter.x - bw / 2, bCenter.y - bd / 2, bw, bd);
            FillRect(brect, bh, BlockRole.Buttress);
        }

        // ── 5. Colonnade approach ─────────────────────────────────────────────
        // Pick 1 face of the monolith as the "entrance" face.
        // Place 1-2 rows of isolated columns approaching it.
        int  entranceFace = rng.Next(4); // 0=N,1=E,2=S,3=W
        bool doColonnade  = rng.NextDouble() < 0.35f;
        if (doColonnade)
        {
            int  colLength  = rng.Next(4, 8);
            int  colWidth   = rng.Next(3, 6);

            // Start the colonnade just outside the monolith base
            Vector2Int colStart;
            if (entranceFace == 0 || entranceFace == 2)
            {
                int xOffset = -colWidth / 2;
                int zStart  = entranceFace == 0
                    ? baseRect.yMax
                    : baseRect.yMin - colLength;
                colStart = new Vector2Int(baseCX + xOffset, zStart);
            }
            else
            {
                int zOffset = -colWidth / 2;
                int xStart  = entranceFace == 1
                    ? baseRect.xMax
                    : baseRect.xMin - colLength;
                colStart = new Vector2Int(xStart, baseCY + zOffset);
            }

            // Place columns every 2 tiles in the perpendicular direction
            for (int step = 0; step < colLength; step += 2)
            for (int side = 0; side < colWidth; side += 2)
            {
                Vector2Int pos = entranceFace == 0 || entranceFace == 2
                    ? colStart + new Vector2Int(side, step)
                    : colStart + new Vector2Int(step, side);

                if (!Heights.ContainsKey(pos))
                {
                    ColonnadePositions.Add(pos);
                }
            }
        }

        // ── 6. Optional boulder core ──────────────────────────────────────────
        if (rng.NextDouble() < 0.3f)
        {
            // A 2-3 tile wide zone at the absolute center, ground floor only
            int br = rng.Next(1, 3);
            for (int x = -br; x <= br; x++)
            for (int z = -br; z <= br; z++)
            {
                var col = new Vector2Int(x, z);
                if (Heights.ContainsKey(col))
                    BoulderCore.Add(col);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    void FillRect(RectInt rect, int h, BlockRole role)
    {
        Blocks.Add(new BuildingBlock { rect = rect, height = h, role = role });
        for (int x = rect.xMin; x < rect.xMax; x++)
        for (int z = rect.yMin; z < rect.yMax; z++)
        {
            var col = new Vector2Int(x, z);
            if (!Heights.TryGetValue(col, out int cur) || h > cur)
            {
                Heights[col] = h;
                Roles[col]   = role;
            }
        }
    }

    static void Shuffle<T>(System.Random rng, List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
