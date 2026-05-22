using UnityEngine;

/// <summary>
/// Per-feature tuning knobs for <see cref="CanyonPathFeature"/>. Drop on the spawner next to the
/// shared <see cref="TerrainFeatureTuning"/> to get fine-grained control over the corridor shape.
/// All fields are pure data — no RNG state, fully deterministic.
/// </summary>
[System.Serializable]
public class CanyonPathSettings
{
    [Tooltip("Half-width of the flat walkable floor strip, in metres. Agents must fit inside this.")]
    [Range(1f, 20f)] public float floorHalfWidth = 4f;

    [Tooltip("Extra metres the rock wall extends laterally beyond the floor strip before it fades out.")]
    [Range(2f, 30f)] public float wallBandWidth = 8f;

    [Tooltip("Wall sharpness passed to TerrainProfiles.Ridge (0 = smooth shoulder, 1 = knife edge).")]
    [Range(0f, 1f)] public float wallSharpness = 0.65f;

    [Tooltip("Fraction of wall height that varies per pinch-point along the spline, giving alternating narrows and wider openings.")]
    [Range(0f, 0.6f)] public float pinchVariation = 0.35f;

    [Tooltip("Spatial frequency along the spline of pinch / widen oscillation. Higher = tighter rhythm.")]
    [Range(0.01f, 0.5f)] public float pinchFrequency = 0.08f;

    [Tooltip("Floor surface noise scale override. Smaller = broader, gentler undulations on the path.")]
    [Range(0.005f, 0.2f)] public float floorNoiseScale = 0.02f;

    [Tooltip("Maximum floor noise amplitude in metres. Keeps the floor walkable even with noise on.")]
    [Range(0f, 3f)] public float floorNoiseAmount = 0.6f;
}

/// <summary>
/// <para>
/// <b>CanyonPath</b> — a narrow walkable corridor winding between tall craggy rock walls along a
/// designer-placed spline. The core gameplay promise is a <em>guaranteed-traversable floor strip</em>:
/// the centre band is explicitly slope-limited via <see cref="TerrainProfiles.LimitSlope"/> so
/// <c>NavMeshAgent</c>s always have a clean route. Flanking rock walls are generated with
/// <see cref="TerrainProfiles.Ridge"/> + <see cref="TerrainNoiseHelper.SurfaceNoise"/> to produce
/// eroded, craggy canyon walls that vary in height and closeness along the spline (pinch points and
/// wider openings). Wall noise and jaggedness are read from the shared
/// <see cref="TerrainFeatureTuning"/>; floor / wall geometry is tweaked via
/// <see cref="CanyonPathSettings"/>.
/// </para>
/// <para>
/// If <see cref="FeatureContext.Path"/> is invalid (fewer than 2 control points) the feature falls
/// back to a straight path spanning the <see cref="FeatureContext.LocalBounds"/> box diagonal.
/// </para>
/// </summary>
public sealed class CanyonPathFeature : TerrainFeature
{
    /// <summary>Optional per-spawner overrides; null = use built-in defaults.</summary>
    public CanyonPathSettings Settings;

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.CanyonPath;

    /// <inheritdoc/>
    /// <remarks>No overhangs — the heightfield density is correct and far cheaper than voxel.</remarks>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    // ─────────────────────────────────────────────────────────────────────────
    // BuildDensity
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        TerrainFeatureTuning tuning = context.Tuning;
        CanyonPathSettings s = Settings ?? new CanyonPathSettings();

        // ── Spline ───────────────────────────────────────────────────────────
        FeatureSpline spline = BuildSpline(context);

        // ── Geometry constants ───────────────────────────────────────────────
        float wallHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);
        float floorHW    = s.floorHalfWidth;                // flat walkable half-width
        float wallBand   = s.wallBandWidth;                  // wall lateral extent beyond floor
        float totalHW    = floorHW + wallBand;               // full cross-section half-width

        // ── Footprint bounds ─────────────────────────────────────────────────
        // Expand the path's local bounds by the full cross-section plus overlap band.
        Bounds footprint = context.Path.IsValid
            ? context.Path.ComputeLocalBounds(totalHW + tuning.overlap + 2f)
            : ExpandedBoxFootprint(context, totalHW + tuning.overlap + 2f);

        // ── Pre-bake a slope-limited floor elevation table along the spline ──
        // We sample the spline at SplineSteps evenly-spaced t values, record the ground height at
        // each, then apply LimitSlope forward and backward so the baked table is guaranteed
        // walkable. The height lambda looks up the two nearest baked values and lerps.
        const int SplineSteps = 128;
        float[] bakedT      = new float[SplineSteps];
        float[] bakedFloorY = new float[SplineSteps];

        for (int i = 0; i < SplineSteps; i++)
        {
            bakedT[i] = i / (float)(SplineSteps - 1);
            Vector3 cp = spline.Evaluate(bakedT[i]);
            bakedFloorY[i] = context.LocalGroundHeight(cp.x, cp.z);
        }

        // Forward slope-limit pass.
        if (tuning.keepWalkable)
        {
            for (int i = 1; i < SplineSteps; i++)
            {
                Vector3 prev = spline.Evaluate(bakedT[i - 1]);
                Vector3 curr = spline.Evaluate(bakedT[i]);
                float step = new Vector2(curr.x - prev.x, curr.z - prev.z).magnitude;
                step = Mathf.Max(step, context.VoxelSize);
                bakedFloorY[i] = TerrainProfiles.LimitSlope(
                    bakedFloorY[i], bakedFloorY[i - 1], step, tuning.maxWalkableSlope);
            }
            // Backward pass for symmetry.
            for (int i = SplineSteps - 2; i >= 0; i--)
            {
                Vector3 curr = spline.Evaluate(bakedT[i]);
                Vector3 next = spline.Evaluate(bakedT[i + 1]);
                float step = new Vector2(next.x - curr.x, next.z - curr.z).magnitude;
                step = Mathf.Max(step, context.VoxelSize);
                bakedFloorY[i] = TerrainProfiles.LimitSlope(
                    bakedFloorY[i], bakedFloorY[i + 1], step, tuning.maxWalkableSlope);
            }
        }

        // ── Height lambda ────────────────────────────────────────────────────
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            Vector3 p = new Vector3(x, 0f, z);

            // Find the nearest spline parameter and lateral (signed) distance from the centre-line.
            float t = spline.ClosestParam(p, out float lateralDist, out Vector3 closest);
            float absLat = Mathf.Abs(lateralDist);

            // ── Outside the full cross-section + overlap band: return raw ground ──
            float distInsideTotal = totalHW + tuning.overlap - absLat;
            if (distInsideTotal <= 0f)
                return context.LocalGroundHeight(x, z);

            // ── Look up the slope-limited floor elevation at this spline parameter ──
            float floorY = SampleBakedFloor(t, bakedT, bakedFloorY);

            // ── Along-spline pinch variation (deterministic, driven by seed + position) ──
            float pinchNoise = TerrainNoiseHelper.Fbm(
                new Vector3(t * 50f, 0f, (float)context.Seed * 0.001f),
                s.pinchFrequency, context.Seed + 3, 2);  // [-1,1]
            float pinchFactor = 1f - s.pinchVariation * (pinchNoise * 0.5f + 0.5f);
            float effectiveWallH = wallHeight * Mathf.Clamp(pinchFactor, 1f - s.pinchVariation, 1f);

            // ── Floor strip: flat + gentle noise, slope-limited ──
            if (absLat <= floorHW)
            {
                float floorOverlap = TerrainNoiseHelper.OverlapWeight(floorHW - absLat, tuning);
                // Very gentle floor noise (scaled down to keep walkable).
                float floorNoise = NoiseDistortion.Sample(
                    new Vector3(x * s.floorNoiseScale, 0f, z * s.floorNoiseScale), 1f, context.Seed + 11)
                    * s.floorNoiseAmount;
                // Blend from raw ground to the slope-corrected floor Y, weighted by lateral overlap.
                float rawGround = context.LocalGroundHeight(x, z);
                float blendedFloor = Mathf.Lerp(rawGround, floorY + floorNoise, floorOverlap);
                return blendedFloor;
            }

            // ── Wall band: rises from floor level using a Ridge profile ──
            // lateralEdge: 0 at the inner floor edge, 1 at the outer wall edge.
            float lateralEdge = Mathf.Clamp01((absLat - floorHW) / wallBand);

            // Ridge peaks near the floor edge and tapers outward (inverted ridge = wall rising from corridor).
            float ridgeT  = 1f - lateralEdge;                      // 1 at inner edge, 0 at outer
            float wallProfile = TerrainProfiles.Ridge(ridgeT * 2f - 1f, s.wallSharpness); // [0,1]

            // Wall surface noise: craggy, jagged rock face.
            float wallNoise = TerrainNoiseHelper.SurfaceNoise(
                new Vector3(x, 0f, z), tuning, context.Seed + 17);

            // Overall edge blend fades the feature to zero at the footprint boundary.
            float edgeWeight = TerrainNoiseHelper.OverlapWeight(distInsideTotal, tuning);

            float wallSurfaceY = floorY + (wallProfile * effectiveWallH + wallNoise) * edgeWeight;

            // Ensure the wall never dips below the local ground (don't carve).
            float groundY = context.LocalGroundHeight(x, z);
            return Mathf.Max(groundY, wallSurfaceY);
        };

        // ── Meshing band extents ─────────────────────────────────────────────
        float centreGroundY = context.LocalGroundHeight(footprint.center.x, footprint.center.z);
        float minY       = centreGroundY - 4f;
        float maxY       = centreGroundY + wallHeight + tuning.noiseAmount + 4f;
        float bandPad    = context.VoxelSize * 2f;

        // Coverage mask: the corridor only occupies the columns within its full cross-section
        // (floor strip + wall band) plus the overlap band. Beyond that the column is plain
        // desert floor — meshing it would build a flat apron alongside the path, which is not
        // wanted. A column is covered exactly where it falls inside that lateral extent.
        System.Func<float, float, bool> coverageFn = (x, z) =>
        {
            spline.ClosestParam(new Vector3(x, 0f, z), out float lateralDist, out _);
            return (totalHW + tuning.overlap - Mathf.Abs(lateralDist)) > 0f;
        };

        return new HeightfieldDensity(heightFn, footprint, minY, maxY, bandPad, coverageFn);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a valid spline from <paramref name="context"/>. Falls back to a two-point straight
    /// line along the box diagonal when the path has fewer than two control points.
    /// </summary>
    static FeatureSpline BuildSpline(FeatureContext context)
    {
        if (context.Path.IsValid)
            return new FeatureSpline(context.Path);

        // Fallback: a straight path along the local X axis spanning 80 % of the box.
        FeaturePath fallback = new FeaturePath();
        fallback.controlPoints.Add(new Vector3(-context.LocalBounds.extents.x * 0.8f, 0f, 0f));
        fallback.controlPoints.Add(new Vector3( context.LocalBounds.extents.x * 0.8f, 0f, 0f));
        fallback.halfWidth = context.LocalBounds.extents.z * 0.4f;
        return new FeatureSpline(fallback);
    }

    /// <summary>
    /// Expands the context's local box footprint by <paramref name="padding"/> on the XZ axes for
    /// use as the <see cref="HeightfieldDensity"/> footprint when there is no valid path.
    /// </summary>
    static Bounds ExpandedBoxFootprint(FeatureContext context, float padding)
    {
        Bounds b = context.LocalBounds;
        return new Bounds(b.center, new Vector3(b.size.x + padding * 2f, b.size.y, b.size.z + padding * 2f));
    }

    /// <summary>
    /// Linearly interpolates the baked floor-height table at normalised spline parameter
    /// <paramref name="t"/>, clamping to the array ends.
    /// </summary>
    static float SampleBakedFloor(float t, float[] bakedT, float[] bakedFloorY)
    {
        int n = bakedT.Length;
        if (n == 0) return 0f;
        if (t <= bakedT[0])  return bakedFloorY[0];
        if (t >= bakedT[n - 1]) return bakedFloorY[n - 1];

        // Binary-search for the bracketing segment.
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (bakedT[mid] <= t) lo = mid; else hi = mid;
        }
        float frac = (t - bakedT[lo]) / Mathf.Max(bakedT[hi] - bakedT[lo], 1e-6f);
        return Mathf.Lerp(bakedFloorY[lo], bakedFloorY[hi], frac);
    }
}
