using UnityEngine;

/// <summary>
/// Extra per-instance knobs for <see cref="MesaFeature"/>. Attach alongside a
/// <c>TerrainFeatureSpawner</c> — the spawner hands the <see cref="FeatureContext"/> to
/// <see cref="MesaFeature.BuildDensity"/>, which reads these values if present in the scene.
/// All four core knobs (noise, overlap, height, jaggedness) live in
/// <see cref="TerrainFeatureTuning"/> as mandated; this class only adds mesa-specific extras.
/// </summary>
[System.Serializable]
public class MesaSettings
{
    /// <summary>
    /// Fraction of the normalised footprint half-span that the steep cliff wall occupies before
    /// the profile becomes flat top. 0.15 = narrow cliff band, very broad summit; 0.45 = tall
    /// sheer walls, narrower summit. Feeds directly into <see cref="TerrainProfiles.Plateau"/>.
    /// </summary>
    [Range(0.05f, 0.6f)]
    public float wallFraction = 0.28f;

    /// <summary>
    /// Amplitude of the organic outline modulation applied to the effective edge distance, as a
    /// fraction of the footprint's shorter half-extent. Positive values break the rectangular
    /// silhouette into a naturally irregular perimeter. 0 = perfectly rectangular footprint.
    /// </summary>
    [Range(0f, 0.35f)]
    public float outlineWarpStrength = 0.18f;

    /// <summary>
    /// Frequency (world-space, relative to 1 m) of the outline warp noise. Higher = finer
    /// indentations; lower = broad lobes. Works alongside <see cref="outlineWarpStrength"/>.
    /// </summary>
    [Range(0.005f, 0.12f)]
    public float outlineWarpFrequency = 0.03f;

    /// <summary>
    /// Amplitude of the additional vertical erosion / striation noise layered onto the cliff
    /// walls only, in metres. Gives the steep face a craggy, banded, wind-scoured look.
    /// Blended in proportion to how much of the wall band we are in — zero on the flat top.
    /// </summary>
    [Range(0f, 12f)]
    public float wallStriationStrength = 5f;

    /// <summary>
    /// Spatial frequency of the vertical erosion striations on the cliff face.
    /// Higher = tight horizontal bands; lower = broad erosion swells.
    /// </summary>
    [Range(0.01f, 0.3f)]
    public float wallStriationFrequency = 0.09f;

    /// <summary>
    /// Amplitude of the subtle height variation on the flat summit, in metres. Keeps the top
    /// walkable but not unnaturally perfect — a slight undulation of ancient rock.
    /// </summary>
    [Range(0f, 4f)]
    public float summitRippleStrength = 0.8f;
}

/// <summary>
/// Heightfield terrain feature that builds a MESA: a tall, flat-topped mountain with steep
/// eroded cliff walls and a broad, gently-noisy walkable summit.
///
/// <para><b>Shape approach</b></para>
/// <list type="bullet">
///   <item>The box footprint's edge distance is normalised to [0, 1] and warped by low-frequency
///     noise (<see cref="MesaSettings.outlineWarpStrength"/>) so the perimeter is organic rather
///     than a perfect rectangle.</item>
///   <item><see cref="TerrainProfiles.Plateau"/> converts that warped distance into the
///     steep-wall→flat-top silhouette: 0 at the edge, rising sharply across the wall fraction,
///     then clamped to 1 (the summit).</item>
///   <item>Within the wall band, vertical erosion striations are applied via a separate
///     <see cref="TerrainNoiseHelper.Fbm"/> pass modulated by wall-band position, giving a
///     layered, wind-scoured cliff face.</item>
///   <item><see cref="TerrainNoiseHelper.SurfaceNoise"/> with the tuning jaggedness adds
///     fine-scale surface crags everywhere; on the summit its amplitude is reduced to preserve
///     walkability.</item>
///   <item><see cref="TerrainNoiseHelper.OverlapWeight"/> blends the entire feature gracefully
///     into the surrounding terrain across the overlap band.</item>
/// </list>
///
/// <para>All outputs are deterministic off <see cref="FeatureContext.Seed"/>.</para>
/// </summary>
public sealed class MesaFeature : TerrainFeature
{
    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.Mesa;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    // Per-instance settings. If no MesaSettings instance is injected, defaults are used.
    private MesaSettings _settings = new MesaSettings();

    /// <summary>
    /// Inject custom settings (e.g. from a spawner companion component or test harness).
    /// If never called the default <see cref="MesaSettings"/> values are used.
    /// </summary>
    public void Configure(MesaSettings settings) => _settings = settings ?? new MesaSettings();

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box          = context.LocalBounds;
        TerrainFeatureTuning tuning = context.Tuning;
        MesaSettings s      = _settings;

        // Deterministic per-feature height with variation applied.
        float mesaHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);

        // Pre-compute footprint geometry in XZ.
        Vector2 centre  = new Vector2(box.center.x, box.center.z);
        float   halfX   = box.extents.x;
        float   halfZ   = box.extents.z;
        // Shortest half-extent drives the normalisation so the profile is consistent
        // regardless of aspect ratio.
        float   halfMin = Mathf.Min(halfX, halfZ);

        // Salt constants so the three noise passes don't correlate.
        int seedOutline    = context.Seed ^ 0x3A7F1C2B;
        int seedStriation  = context.Seed ^ 0x1D8E4F63;
        int seedSummit     = context.Seed ^ 0x5C2A9E17;

        // -----------------------------------------------------------------------
        // Height lambda — the core of the feature.
        // -----------------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY = context.LocalGroundHeight(x, z);

            // --- 1. Rectangular signed distance from nearest footprint edge ----
            float dx = halfX - Mathf.Abs(x - centre.x);
            float dz = halfZ - Mathf.Abs(z - centre.y);
            float distInside = Mathf.Min(dx, dz);   // negative = outside footprint

            // --- 2. Overlap falloff — drives blending into surrounding terrain --
            float weight = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
            if (weight <= 0f) return groundY;

            // --- 3. Organic outline warp — breaks the rectangular silhouette ---
            // Low-frequency FBM noise shifts the effective edge distance inward or outward,
            // giving the mesa perimeter a natural, irregular look.
            float outlineWarp = 0f;
            if (s.outlineWarpStrength > 0f)
            {
                Vector3 wp = new Vector3(x, 0f, z);
                outlineWarp = TerrainNoiseHelper.Fbm(wp, s.outlineWarpFrequency, seedOutline, 3);
                // Scale by shortest half-extent so the warp is proportional to the footprint.
                outlineWarp *= s.outlineWarpStrength * halfMin;
            }
            float warpedDist = distInside + outlineWarp;

            // Normalise the warped distance to [0,1] where 1 = footprint centre.
            float normDist = Mathf.Clamp01(warpedDist / halfMin);

            // --- 4. Plateau profile: steep wall rising to flat top --------------
            // wallFraction controls how much of normDist is the steep cliff wall.
            float plateau = TerrainProfiles.Plateau(normDist, s.wallFraction);

            // --- 5. Wall erosion striations on the cliff face -------------------
            // Only active in the wall band (normDist < wallFraction).
            // wallBand goes 0 (summit edge) → 1 (base of wall).
            float wallBand = Mathf.Clamp01(1f - normDist / Mathf.Max(0.001f, s.wallFraction));
            float striation = 0f;
            if (s.wallStriationStrength > 0f && wallBand > 0f)
            {
                // Sample high-frequency FBM on a vector that mixes XZ position and
                // the wall-band value so the bands are roughly horizontal striations.
                Vector3 sp = new Vector3(x * s.wallStriationFrequency,
                                         wallBand * 2.5f,           // vertical component
                                         z * s.wallStriationFrequency);
                float raw = TerrainNoiseHelper.Fbm(sp, 1f, seedStriation, 4);
                // Apply the tuning jaggedness to sharpen the strata into hard ledges.
                raw = TerrainNoiseHelper.ApplyJaggedness(raw, tuning.jaggedness);
                striation = raw * s.wallStriationStrength * wallBand;
            }

            // --- 6. Summit ripple — gentle noise on the flat top ----------------
            float summitNoise = 0f;
            if (s.summitRippleStrength > 0f)
            {
                // summit fraction: 0 at the summit edge, 1 deep inside the flat top.
                float summitFraction = Mathf.Clamp01((normDist - s.wallFraction)
                                                     / Mathf.Max(0.001f, 1f - s.wallFraction));
                if (summitFraction > 0f)
                {
                    Vector3 rp = new Vector3(x, 0f, z);
                    summitNoise = TerrainNoiseHelper.Fbm(rp, tuning.noiseScale * 0.5f, seedSummit, 2)
                                  * s.summitRippleStrength * summitFraction;
                }
            }

            // --- 7. Global surface noise (crags / jaggedness) -------------------
            // Dampen it on the summit so the top stays walkable.
            float surfNoise = TerrainNoiseHelper.SurfaceNoise(
                new Vector3(x, 0f, z), tuning, context.Seed);
            // On the summit (plateau ≈ 1) reduce amplitude to 20 %; on walls full amplitude.
            float noiseWallBlend = 1f - Mathf.Clamp01((plateau - 0.85f) / 0.15f) * 0.8f;
            surfNoise *= noiseWallBlend;

            // --- 8. Compose final height ----------------------------------------
            float featureHeight = (plateau * mesaHeight) + striation + summitNoise + surfNoise;
            return groundY + featureHeight * weight;
        };

        // Vertical band for the mesher: from well below ground to above the peak.
        float groundAtCentre = context.LocalGroundHeight(box.center.x, box.center.z);
        float minY = groundAtCentre - 4f;
        float maxY = box.max.y + mesaHeight + tuning.noiseAmount + s.wallStriationStrength + 2f;
        float bandPadding = context.VoxelSize * 2f;

        return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding);
    }
}
