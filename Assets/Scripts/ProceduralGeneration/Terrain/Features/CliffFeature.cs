using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="CliffFeature"/>. Exposed separately so a designer can
/// attach these to the spawner and tune independently of the shared <see cref="TerrainFeatureTuning"/>
/// knobs. All values drive deterministic math — nothing mutable or frame-dependent.
/// </summary>
[System.Serializable]
public class CliffFeatureSettings
{
    /// <summary>
    /// Fraction of the footprint (or spline half-width) that the steep face occupies. 0.1 = a thin
    /// knife-edge; 0.4 = a broad craggy wall. The remainder is flat walkable top or bottom.
    /// </summary>
    [Range(0.05f, 0.6f)]
    [Tooltip("Fraction of the cross-section width the steep face occupies. Wider = more ramp, narrower = more vertical.")]
    public float faceWidthFraction = 0.18f;

    /// <summary>
    /// Amplitude of the low-frequency perturbation that makes the cliff edge irregular.
    /// 0 = perfectly straight edge line; 1 = very wavy, organic edge.
    /// </summary>
    [Range(0f, 1f)]
    [Tooltip("How wavy/organic the cliff-edge line is. 0 = ruler-straight, 1 = heavily eroded.")]
    public float edgeIrregularity = 0.55f;
}

/// <summary>
/// Escarpment (cliff) terrain feature: a height step — flat low ground on one side, raised plateau
/// on the other, with a steep, dramatically eroded rock face between. The cliff edge follows the
/// spline <see cref="FeatureContext.Path"/> when a valid path is provided (≥2 points), making it a
/// freely-winding escarpment. When no path is present it falls back to a straight step across the
/// box <see cref="FeatureContext.LocalBounds"/>, making it an area-wide terrain shelf.
///
/// Shape contract:
///   - LOW side: sits on natural ground (weight-blended at the footprint edge).
///   - FACE band: steeply rising rock, heavily noised with vertical striations and craggy surface.
///   - HIGH side (plateau): raised by <see cref="TerrainFeatureTuning.height"/> above natural ground,
///     gently noised so it reads as lived-in ancient rock.
///   - The step position is perturbed by low-frequency noise → organic, non-ruler edge.
///   - Jaggedness concentrates on the steep face band → dramatic, eroded look.
/// </summary>
public sealed class CliffFeature : TerrainFeature
{
    // -------------------------------------------------------------------------
    // Per-feature settings (may be null when invoked from registry default ctor)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional per-spawner override settings. When null the feature falls back to code defaults.
    /// Designers attach a <see cref="CliffFeatureSettings"/> component to the spawner GameObject
    /// and the spawner forwards it; otherwise default values are used.
    /// </summary>
    public CliffFeatureSettings Settings { get; set; }

    // -------------------------------------------------------------------------
    // TerrainFeature contract
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.Cliff;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => new CliffFeatureSettings();

    /// <inheritdoc/>
    public override void ApplySettings(object settings)
    {
        Settings = settings as CliffFeatureSettings;
    }

    /// <summary>
    /// Builds the escarpment heightfield density. The height lambda evaluates the lateral distance
    /// from the cliff edge (spline- or box-derived), feeds it through
    /// <see cref="TerrainProfiles.CliffStep"/>, adds face-concentrated erosion noise, and blends
    /// the result into the natural ground at the footprint boundary via
    /// <see cref="TerrainNoiseHelper.OverlapWeight"/>.
    /// </summary>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box          = context.LocalBounds;
        TerrainFeatureTuning tuning = context.Tuning;
        int seed            = context.Seed;

        // Resolve per-feature settings, falling back to defaults.
        float faceWidth     = Settings != null ? Settings.faceWidthFraction : 0.18f;
        float edgeIrreg     = Settings != null ? Settings.edgeIrregularity  : 0.55f;

        // Per-feature deterministic height (applies heightVariation jitter).
        float stepHeight    = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed);

        // Deterministic low-frequency spatial frequency for the edge wiggle.
        // Salt 3 keeps it independent of the surface-noise seed path.
        float edgeFreq      = 0.015f + TerrainNoiseHelper.Hash01(seed, 3) * 0.01f;

        // Pre-compute box geometry used by the fallback straight-step path.
        Vector2 centreXZ    = new Vector2(box.center.x, box.center.z);
        Vector2 halfXZ      = new Vector2(box.extents.x, box.extents.z);

        // Choose the primary axis for the fallback straight step: step across the LONGER box axis
        // so the cliff spans the feature's width, not depth.
        bool stepAlongX     = box.size.z >= box.size.x;

        // Decide whether to use spline mode.
        var spline          = new FeatureSpline(context.Path);
        bool useSpline      = spline.IsValid;

        // -------------------------------------------------------------------------
        // Height lambda — the only thing this feature implements.
        // -------------------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY   = context.LocalGroundHeight(x, z);

            // --- 1. Footprint overlap weight (fades feature into surrounding terrain) ----------
            float distInside = context.FootprintDistanceInside(x, z);
            float weight    = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
            if (weight <= 0f) return groundY;

            // --- 2. Signed lateral distance from the cliff edge line -------------------------
            // lateralT in [-1, 1]: -1 = deep low side, +1 = deep high side.
            float lateralT;

            if (useSpline)
            {
                // Spline path: signed lateral distance from the centre-line, normalised by the
                // spline half-width so ±1 maps to the path's designed edge.
                float hw = Mathf.Max(0.1f, spline.HalfWidth);
                spline.ClosestParam(new Vector3(x, 0f, z), out float latDist, out _);
                lateralT = Mathf.Clamp(latDist / hw, -1f, 1f);
            }
            else
            {
                // Box fallback: step perpendicular to the longer axis.
                if (stepAlongX)
                {
                    // Step runs along X, so lateral is Z.
                    lateralT = Mathf.Clamp((z - centreXZ.y) / Mathf.Max(0.1f, halfXZ.y), -1f, 1f);
                }
                else
                {
                    lateralT = Mathf.Clamp((x - centreXZ.x) / Mathf.Max(0.1f, halfXZ.x), -1f, 1f);
                }
            }

            // --- 3. Edge irregularity: perturb lateralT with low-frequency noise -------------
            // A slow fbm in XZ shifts the apparent cliff-edge position locally, breaking any
            // straight-line appearance without adding high-frequency jaggedness at this stage.
            float edgeWiggle = TerrainNoiseHelper.Fbm(
                new Vector3(x, 0f, z), edgeFreq, seed + 11, 3);
            // Scale wiggle by edgeIrregularity and the face width so it perturbs the edge
            // position by at most one face-width on either side.
            lateralT += edgeWiggle * edgeIrreg * faceWidth * 2f;
            lateralT  = Mathf.Clamp(lateralT, -1.5f, 1.5f);   // allow slight over/undershoot

            // --- 4. CliffStep profile in [-1, 1] range ------------------------------------
            // edge=0 centres the transition; width is the face-width fraction of the full span.
            float profile   = TerrainProfiles.CliffStep(lateralT, 0f, faceWidth * 2f);

            // --- 5. Compute face-band mask for concentrating erosion on the steep section -----
            // Map lateralT into [0,1] and derive how close we are to the midpoint (0 = edge centre).
            // This is 1 at the face and ~0 on the flats.
            float faceMid   = 0f;                              // edge sits at lateralT = 0
            float faceBandT = Mathf.Clamp01(1f - Mathf.Abs(lateralT - faceMid) / (faceWidth * 2f + 0.01f));
            float faceBoost = faceBandT * faceBandT;           // concentrate at the sheer face

            // --- 6. Surface noise — vertical striations + craggy erosion on the face ----------
            // Use the standard SurfaceNoise (drives tuning.noiseAmount & jaggedness) for the
            // baseline organic surface. Then overlay a higher-frequency striation pass on the face.
            float surfNoise = TerrainNoiseHelper.SurfaceNoise(new Vector3(x, 0f, z), tuning, seed);

            // Striation: tall narrow fbm sampled primarily in X (or Z perpendicular to step) at
            // higher frequency to simulate eroded vertical channels in the rock face.
            Vector3 striationP = stepAlongX || useSpline
                ? new Vector3(x * 2.5f, 0f, z * 0.4f)        // stretch X = vertical channels
                : new Vector3(x * 0.4f, 0f, z * 2.5f);
            float striation    = TerrainNoiseHelper.Fbm(striationP, tuning.noiseScale * 1.8f, seed + 97, 4);
            striation          = TerrainNoiseHelper.ApplyJaggedness(striation, tuning.jaggedness);
            // Striations only show on the steep face, scaled by noiseAmount for consistency.
            float faceNoise    = striation * tuning.noiseAmount * faceBoost;

            float totalNoise   = surfNoise + faceNoise;

            // --- 7. Compose final surface height -----------------------------------------
            float raised       = groundY + stepHeight * profile + totalNoise;

            // Blend by footprint weight (fades to groundY at the boundary).
            return Mathf.Lerp(groundY, raised, weight);
        };

        // -------------------------------------------------------------------------
        // Vertical extent for the marching-cubes band.
        // -------------------------------------------------------------------------
        float centreGroundY = context.LocalGroundHeight(box.center.x, box.center.z);
        float noiseMargin   = tuning.noiseAmount + 4f;
        float minY          = centreGroundY - noiseMargin;
        float maxY          = centreGroundY + stepHeight + noiseMargin;
        float bandPadding   = context.VoxelSize * 2f;

        return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding);
    }
}
