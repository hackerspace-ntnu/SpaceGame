using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="CanyonFeature"/>. Embed on a spawner MonoBehaviour or pass
/// via a wrapper; the feature reads these alongside the shared <see cref="TerrainFeatureTuning"/>
/// knobs from the context.
/// </summary>
[System.Serializable]
public class CanyonSettings
{
    [Tooltip("Fraction of the half-width that is a flat walkable floor before the walls begin. " +
             "0 = V-shaped slot, 0.3 = narrow floor with steep walls, 0.6 = wide basin.")]
    [Range(0f, 0.6f)] public float floorFlatFraction = 0.15f;

    [Tooltip("How much the half-width varies sinusoidally along the spline (0 = constant width, " +
             "1 = pinch points narrow to near zero, chambers open wide). Slot-canyon feel.")]
    [Range(0f, 1f)] public float widthMeander = 0.55f;

    [Tooltip("Spatial frequency of the width variation along the spline — higher = more pinch " +
             "points per unit of spline length.")]
    [Range(0.5f, 8f)] public float meanderFrequency = 2.5f;

    [Tooltip("Fraction of the half-width occupied by the erosion-noise wall band. Noise is " +
             "concentrated here and fades to zero at the rim and at the floor edge.")]
    [Range(0.1f, 0.9f)] public float wallBandFraction = 0.7f;

    [Tooltip("Extra noise scale multiplier on the wall striations — larger = coarser vertical " +
             "streaks, smaller = fine-grained crags.")]
    [Range(0.5f, 4f)] public float wallNoiseScale = 1.8f;
}

/// <summary>
/// <para>
/// Heightfield terrain feature that carves a narrow, sinuous slot canyon into the desert terrain
/// along the designer-placed spline. The canyon is deep relative to its width, with steep eroded
/// walls, vertical striations, and a gently sloped walkable floor.
/// </para>
/// <para>
/// The half-width varies along the spline using seeded noise to create alternating pinch points
/// and wider chambers — the slot-canyon feel the project lead requested. Wall-surface noise is
/// concentrated in the wall band and falls to zero at the rim and at the floor, keeping the floor
/// walkable and the rim visually continuous with surrounding terrain. Two overlapping canyons
/// merge smoothly via <see cref="TerrainNoiseHelper.OverlapWeight"/>.
/// </para>
/// <para>
/// Register with: <c>Register(() => new CanyonFeature());</c>
/// </para>
/// </summary>
public sealed class CanyonFeature : TerrainFeature
{
    // ------------------------------------------------------------------
    //  Optional per-feature settings. When no external settings are
    //  injected the defaults produce a good slot-canyon out of the box.
    // ------------------------------------------------------------------

    /// <summary>
    /// Per-feature detail settings. Assign before baking for designer control; the shared
    /// <see cref="TerrainFeatureTuning"/> block in the context governs the four core knobs.
    /// </summary>
    public CanyonSettings Settings { get; set; } = new CanyonSettings();

    // ------------------------------------------------------------------
    //  Contract
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.Canyon;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    /// <summary>
    /// Builds a <see cref="HeightfieldDensity"/> that carves the canyon into the terrain.
    /// Falls back to a straight-line path across the box long axis when the path is invalid.
    /// </summary>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        TerrainFeatureTuning tuning = context.Tuning;
        CanyonSettings cfg = Settings;

        // Deterministic depth with per-feature variation.
        float canyonDepth = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);

        // Build the spline; fall back to a straight line spanning the box if the path is too short.
        FeatureSpline spline = BuildSpline(context);

        // Footprint: spline bounding box + half-width padding, clamped by the designer box.
        Bounds pathBounds = context.Path.IsValid
            ? context.Path.ComputeLocalBounds(tuning.overlap)
            : context.LocalBounds;
        Bounds footprint = pathBounds;
        footprint.Expand(tuning.overlap * 2f);

        // Half-width source: prefer the path's halfWidth; fall back to box short-axis half-extent.
        float baseHalfWidth = context.Path.IsValid && spline.HalfWidth > 0.1f
            ? spline.HalfWidth
            : Mathf.Min(context.LocalBounds.extents.x, context.LocalBounds.extents.z);

        // Pre-sample a rough min/max Y so HeightfieldDensity can bound its band tightly.
        float centreGround = context.LocalGroundHeight(footprint.center.x, footprint.center.z);
        float minY = centreGround - canyonDepth - tuning.noiseAmount - 4f;
        float maxY = centreGround + tuning.noiseAmount + 4f;
        float bandPadding = context.VoxelSize * 2f;

        // Capture all closure variables (no mutable captures after this point).
        int seed = context.Seed;
        float floorFlat = cfg.floorFlatFraction;
        float widthMeander = cfg.widthMeander;
        float meanderFreq = cfg.meanderFrequency;
        float wallBand = cfg.wallBandFraction;
        float wallNoiseScale = cfg.wallNoiseScale;
        bool keepWalkable = tuning.keepWalkable;
        float maxSlope = tuning.maxWalkableSlope;

        // ---------------------------------------------------------------
        // Height function — the heart of the feature.
        // ---------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY = context.LocalGroundHeight(x, z);

            // 1. Find the closest point on the spline and the signed lateral distance.
            Vector3 samplePt = new Vector3(x, 0f, z);
            float tParam = spline.ClosestParam(samplePt, out float lateralDist, out Vector3 closest);

            // 2. Vary the half-width sinusoidally along the spline (pinch points + chambers).
            //    We use Fbm rather than a pure sine so the variation feels geological, not mechanical.
            float meanderNoise = TerrainNoiseHelper.Fbm(
                new Vector3(tParam * meanderFreq, 0f, (float)seed * 0.001f),
                1f, seed + 3779, 2);                          // [−1, 1]
            float halfWidth = baseHalfWidth * (1f + widthMeander * meanderNoise);
            halfWidth = Mathf.Max(halfWidth, baseHalfWidth * 0.15f); // never fully pinch shut

            // 3. Signed distance INTO the carved zone from the rim (negative = outside canyon).
            float distInsideRim = halfWidth - Mathf.Abs(lateralDist);

            // 4. Overlap / edge-blend weight: fades the canyon into surrounding terrain.
            float overlapWeight = TerrainNoiseHelper.OverlapWeight(distInsideRim, tuning);
            if (overlapWeight <= 0f) return groundY;   // outside footprint entirely

            // 5. Normalised lateral fraction [0, 1] clamped to half-width.
            float lateralFrac = Mathf.Clamp01(Mathf.Abs(lateralDist) / halfWidth);

            // 6. Canyon cross-section depth profile: 1 at floor centre, 0 at rim.
            float profileDepth = TerrainProfiles.CanyonCrossSection(lateralFrac, floorFlat);

            // 7. Wall-band mask: ramps up from 0 at the floor edge, peaks at mid-wall,
            //    falls to 0 again at the rim. Concentrates erosion noise where walls are.
            float floorEdgeFrac = floorFlat + 0.01f;
            float wallMask = 0f;
            if (lateralFrac > floorEdgeFrac)
            {
                float wallT = Mathf.InverseLerp(floorEdgeFrac, 1f, lateralFrac);
                // Bell shape: peaks at wallBand fraction up the wall.
                float bell = 1f - Mathf.Abs(wallT - wallBand) / Mathf.Max(wallBand, 1f - wallBand);
                wallMask = Mathf.Clamp01(bell);
            }

            // 8. Wall erosion noise: vertical striations + crags, scaled up on the wall.
            //    We deliberately include Y from the spline's closest point height so the noise
            //    flows vertically (striations) rather than across the slope.
            float noiseY = closest.y;
            Vector3 noisePt = new Vector3(x * wallNoiseScale, noiseY * 0.4f, z * wallNoiseScale);
            float wallNoise = TerrainNoiseHelper.SurfaceNoise(noisePt, tuning, seed + 1103);
            wallNoise *= wallMask; // zero on floor, full on steep wall band

            // 9. Base carved height: ground minus the depth profile, noise added on walls.
            float carvedY = groundY - profileDepth * canyonDepth + wallNoise;

            // 10. For the walkable floor band, enforce the max slope so NavMeshAgents can traverse.
            //     We approximate slope-limiting by comparing to a reference height one voxel away;
            //     on the floor band this collapses to: depth changes gently along the spline.
            if (keepWalkable && lateralFrac <= floorFlat + 0.05f)
            {
                // Sample depth at the previous spline point to get a neighbouring reference.
                float tPrev = Mathf.Max(0f, tParam - 0.02f);
                Vector3 prevPt = spline.Evaluate(tPrev);
                float prevGround = context.LocalGroundHeight(prevPt.x, prevPt.z);
                float prevCarved = prevGround - canyonDepth;   // approximate floor at prev point
                float stepDist = Vector3.Distance(new Vector3(x, 0f, z),
                    new Vector3(prevPt.x, 0f, prevPt.z));
                carvedY = TerrainProfiles.LimitSlope(carvedY, prevCarved, stepDist, maxSlope);
            }

            // 11. Blend the carved result with the surrounding terrain using the overlap weight.
            return Mathf.Lerp(groundY, carvedY, overlapWeight);
        };

        // Coverage mask: the canyon only occupies the carved zone within its (meandering)
        // half-width of the spline. Beyond that the column is undisturbed desert floor — meshing
        // it would build a flat ground apron alongside the canyon, which is not wanted. A column
        // is covered exactly where the overlap weight is non-zero, mirroring the half-width
        // meander of the height lambda.
        System.Func<float, float, bool> coverageFn = (x, z) =>
        {
            float tParam = spline.ClosestParam(new Vector3(x, 0f, z), out float lateralDist, out _);
            float meanderNoise = TerrainNoiseHelper.Fbm(
                new Vector3(tParam * meanderFreq, 0f, (float)seed * 0.001f),
                1f, seed + 3779, 2);
            float halfWidth = baseHalfWidth * (1f + widthMeander * meanderNoise);
            halfWidth = Mathf.Max(halfWidth, baseHalfWidth * 0.15f);
            float distInsideRim = halfWidth - Mathf.Abs(lateralDist);
            return TerrainNoiseHelper.OverlapWeight(distInsideRim, tuning) > 0f;
        };

        return new HeightfieldDensity(heightFn, footprint, minY, maxY, bandPadding, coverageFn);
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a valid <see cref="FeatureSpline"/> from the context path, or builds a straight
    /// two-point fallback spanning 80 % of the box's long axis when the path is invalid.
    /// </summary>
    static FeatureSpline BuildSpline(FeatureContext context)
    {
        if (context.Path.IsValid)
            return new FeatureSpline(context.Path);

        // Fallback: straight line across the longest box axis.
        Bounds box = context.LocalBounds;
        float halfLong = Mathf.Max(box.extents.x, box.extents.z) * 0.8f;
        bool longX = box.extents.x >= box.extents.z;

        FeaturePath fallback = new FeaturePath();
        fallback.halfWidth = Mathf.Min(box.extents.x, box.extents.z) * 0.3f;
        fallback.controlPoints.Add(longX
            ? new Vector3(box.center.x - halfLong, 0f, box.center.z)
            : new Vector3(box.center.x, 0f, box.center.z - halfLong));
        fallback.controlPoints.Add(longX
            ? new Vector3(box.center.x + halfLong, 0f, box.center.z)
            : new Vector3(box.center.x, 0f, box.center.z + halfLong));

        return new FeatureSpline(fallback);
    }
}
