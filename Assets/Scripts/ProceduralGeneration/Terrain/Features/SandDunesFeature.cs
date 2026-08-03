using UnityEngine;

/// <summary>
/// Optional per-feature settings that the designer can tune on top of the four shared knobs.
/// Kept small: only the parameters that are genuinely unique to a dune field.
/// </summary>
[System.Serializable]
public class SandDunesSettings
{
    [Tooltip("Compass angle (degrees, clockwise from +Z) that the wind blows FROM. " +
             "Dunes face into this direction: gentle windward ramp on the upwind side, " +
             "steep slip face on the downwind side.")]
    [Range(0f, 360f)] public float windAngleDeg = 45f;

    [Tooltip("How many dune rows to tile across the footprint along the wind axis. " +
             "More rows = tighter spacing, a busier dune sea.")]
    [Range(2, 20)] public int duneRows = 6;

    [Tooltip("How many dune columns to tile across the footprint perpendicular to the wind. " +
             "More columns = more parallel dune bodies side-by-side.")]
    [Range(2, 20)] public int duneCols = 5;

    [Tooltip("How strongly each dune's crest position is randomised within its cell " +
             "[0 = perfectly periodic grid, 1 = fully random within the cell].")]
    [Range(0f, 1f)] public float crestJitter = 0.55f;

    [Tooltip("Asymmetry bias for the dune crest: positive shifts the crest toward the " +
             "leeward side, steepening the slip face. Matches TerrainProfiles.Dune crestBias.")]
    [Range(-0.5f, 0.5f)] public float crestBias = 0.25f;
}

/// <summary>
/// <b>SandDunesFeature</b> — area terrain feature that fills the box footprint with a natural
/// field of wind-blown barchan dunes sitting on top of the underlying terrain.
///
/// <para>
/// Dunes are tiled in a regular grid of cells whose axes are aligned with the wind direction.
/// Each cell gets a single dune evaluated with <see cref="TerrainProfiles.Dune"/> — gentle
/// windward ramp, steep leeward slip face. Per-cell deterministic jitter (size, spacing, crest
/// offset) via <see cref="TerrainNoiseHelper.Hash01"/> breaks up the repetition so the field
/// reads as a natural dune sea rather than a wallpaper tile.
/// </para>
///
/// <para>
/// Fine sand-ripple detail is added on top via <see cref="TerrainNoiseHelper.SurfaceNoise"/>,
/// scaled down so it does not obscure the macro dune shapes. A broad low-frequency
/// <see cref="TerrainNoiseHelper.Fbm"/> swell nudges inter-dune ground level slightly, giving
/// the field a gentle basin-and-rise feel. Heights are capped to gentle slopes to keep dunes
/// walkable for NavMesh agents.
/// </para>
///
/// <para>
/// The footprint edge uses <see cref="TerrainNoiseHelper.OverlapWeight"/> so dunes fade smoothly
/// into the surrounding terrain with no hard seam. All stochastic elements are seeded exclusively
/// off <see cref="FeatureContext.Seed"/> — output is fully deterministic.
/// </para>
/// </summary>
public sealed class SandDunesFeature : TerrainFeature
{
    /// <summary>Extra per-feature knobs on top of the four shared tuning sliders.</summary>
    public SandDunesSettings Settings = new SandDunesSettings();

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.SandDunes;

    /// <summary>No overhangs — the cheap heightfield density is the correct choice.</summary>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box           = context.LocalBounds;
        TerrainFeatureTuning tuning = context.Tuning;
        int seed             = context.Seed;
        SandDunesSettings s  = Settings;

        // -----------------------------------------------------------------------
        // Pre-compute wind-axis transform (rotate XZ so the wind axis maps to U).
        // Wind blows FROM windAngleDeg, so dunes face into that direction.
        // -----------------------------------------------------------------------
        float windRad = s.windAngleDeg * Mathf.Deg2Rad;
        float windCos = Mathf.Cos(windRad);
        float windSin = Mathf.Sin(windRad);

        // Feature height with per-feature deterministic variation.
        float duneHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed);

        // Footprint geometry used by the overlap-weight falloff.
        Vector2 centreXZ = new Vector2(box.center.x, box.center.z);

        // Cell sizes in wind-aligned (U) and cross-wind (V) axes.
        // The footprint is measured in wind-axis extents (always half-size × 2 = full).
        float footprintU = 2f * box.extents.x * Mathf.Abs(windCos) +
                           2f * box.extents.z * Mathf.Abs(windSin);
        float footprintV = 2f * box.extents.x * Mathf.Abs(windSin) +
                           2f * box.extents.z * Mathf.Abs(windCos);

        int   rows     = Mathf.Max(2, s.duneRows);
        int   cols     = Mathf.Max(2, s.duneCols);
        float cellU    = footprintU / rows;
        float cellV    = footprintV / cols;

        // Ripple noise scale: smaller than macro-noise so it adds fine grain only.
        const float RippleScaleMult = 3.5f;

        // -----------------------------------------------------------------------
        // The height lambda — evaluated per voxel by the mesher.
        // -----------------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY = context.LocalGroundHeight(x, z);

            // Signed distance INTO the designer-drawn polygon footprint (negative = outside),
            // so the dune field's outline follows the polygon, not a rectangle.
            float distInside = context.FootprintDistanceInside(x, z);

            float weight = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
            if (weight <= 0f) return groundY;

            // Project local XZ into wind-aligned UV.
            float lx = x - centreXZ.x;
            float lz = z - centreXZ.y;
            float u  =  lx * windCos + lz * windSin;
            float v  = -lx * windSin + lz * windCos;

            // Shift origin to [0, footprint*] so modulo is clean.
            float uOff = u + footprintU * 0.5f;
            float vOff = v + footprintV * 0.5f;

            // Determine which cell we are in.
            int   ci  = Mathf.Clamp(Mathf.FloorToInt(uOff / cellU), 0, rows - 1);
            int   cj  = Mathf.Clamp(Mathf.FloorToInt(vOff / cellV), 0, cols - 1);
            int   cellSeed = seed ^ (ci * 73856093) ^ (cj * 19349663);

            // Per-cell jitter: randomise actual dune size and crest position.
            float sizeJitter   = 0.7f + 0.6f * TerrainNoiseHelper.Hash01(cellSeed, 1);
            float crestOffsetU = (TerrainNoiseHelper.Hash01(cellSeed, 2) * 2f - 1f)
                                  * s.crestJitter * cellU * 0.4f;
            float phaseV       = (TerrainNoiseHelper.Hash01(cellSeed, 3) * 2f - 1f)
                                  * cellV * 0.3f;

            // Dune t: normalised position within this cell along U axis, [-1, 1].
            float cellUStart = ci * cellU;
            float localU     = uOff - cellUStart;
            float duneCentreU = cellU * 0.5f + crestOffsetU;
            float halfDuneU  = cellU * 0.5f * sizeJitter;
            float tU         = Mathf.Clamp((localU - duneCentreU) / Mathf.Max(halfDuneU, 0.01f), -1f, 1f);

            // Dune cross-wind softening: taper each dune at its lateral edges.
            float cellVStart  = cj * cellV;
            float localV      = vOff - cellVStart;
            float duneCentreV = cellV * 0.5f + phaseV;
            float halfDuneV   = cellV * 0.48f * sizeJitter;
            float tV          = Mathf.Clamp01(1f - Mathf.Abs(localV - duneCentreV) /
                                              Mathf.Max(halfDuneV, 0.01f));
            float vTaper      = Mathf.SmoothStep(0f, 1f, tV);

            // Asymmetric dune profile (windward gentle ramp / leeward steep slip face).
            float duneProfile = TerrainProfiles.Dune(tU, s.crestBias);

            // Combine profile and cross-wind taper.
            float macroHeight = duneProfile * vTaper;

            // Broad swell noise adds slow undulation to inter-dune ground (desert basin feel).
            Vector3 swellP = new Vector3(x * 0.012f, 0f, z * 0.012f);
            float   swell  = TerrainNoiseHelper.Fbm(swellP, 1f, seed ^ 0xFACE, 2) * 0.15f + 0.85f;

            // Fine sand ripple (grain-scale surface texture) — a clean low-amplitude perlin band,
            // intentionally smooth and independent of the shared bumpiness dials.
            float ripplePerlin = NoiseDistortion.Fbm(
                new Vector3(x, 0f, z), tuning.noiseScale * RippleScaleMult, seed ^ 0xD00E);
            float ripple = ripplePerlin * tuning.noiseAmount * 0.18f;

            // Shared surface-detail layer — lets a designer dial dunes from glassy-smooth to
            // bumpy/craggy from the central "Surface detail" knobs, exactly like every feature.
            float detail = TerrainNoiseHelper.DetailLayer(new Vector3(x, 0f, z), tuning, seed ^ 0x5A11D);

            float finalHeight = duneHeight * macroHeight * swell + ripple + detail;

            // Clamp slope for walkability when the designer asks for it.
            if (tuning.keepWalkable)
            {
                float maxH = tuning.maxWalkableSlope * halfDuneU * 2f;
                finalHeight = Mathf.Min(finalHeight, maxH);
            }

            return groundY + finalHeight * weight;
        };

        // -----------------------------------------------------------------------
        // Vertical band: ground level at box centre ± dune height + noise headroom.
        // -----------------------------------------------------------------------
        float centreGround = context.LocalGroundHeight(box.center.x, box.center.z);
        float minY         = centreGround - 4f;
        float maxY         = box.max.y + duneHeight + tuning.noiseAmount + 2f;
        float bandPadding  = context.VoxelSize * 2f;

        // Coverage mask: the dune field only occupies the columns inside its designer-drawn
        // polygon footprint (plus the overlap band). Outside that the column is plain ground —
        // meshing it would build a flat apron around the dune sea, which is not wanted. A column
        // is covered exactly where the overlap weight is non-zero.
        System.Func<float, float, bool> coverageFn = (x, z) =>
        {
            float distInside = context.FootprintDistanceInside(x, z);
            return TerrainNoiseHelper.OverlapWeight(distInside, tuning) > 0f;
        };

        return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding, coverageFn);
    }
}
