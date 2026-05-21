using UnityEngine;

/// <summary>
/// A raised rock spine that follows the editable spline path — a long, climbable hogback ridge
/// rising from the desert floor. Cross-section is shaped by <see cref="TerrainProfiles.Ridge"/>;
/// crest height varies along the path via FBM noise, producing high points and lower saddle
/// passes that give natural crossing points. When <see cref="TerrainFeatureTuning.keepWalkable"/>
/// is true, flanks and along-path slope are kept under <see cref="TerrainFeatureTuning.maxWalkableSlope"/>
/// so NavMeshAgents can climb the full ridge; when false the ridge can be sharper and more scenic.
/// </summary>
public sealed class RidgeFeature : TerrainFeature
{
    // -------------------------------------------------------------------------
    // Per-feature settings — readable in the inspector via a custom field on the
    // spawner, or left at defaults for a production-ready ridge out of the box.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extra tuning knobs specific to the ridge cross-section and along-path profile.
    /// These sit alongside the shared <see cref="TerrainFeatureTuning"/> and are read from
    /// a <see cref="RidgeSettings"/> instance that the feature creates with sensible defaults.
    /// </summary>
    [System.Serializable]
    public sealed class RidgeSettings
    {
        [Tooltip("Fraction of the along-path height variation relative to the base crest height. " +
                 "0 = constant crest, 1 = full variation (saddles can drop to near ground).")]
        [Range(0f, 1f)]
        public float crestVariation = 0.45f;

        [Tooltip("Spatial frequency (in normalised path parameter) of the saddle / high-point cycle. " +
                 "0.5 = about 2 saddle-peak cycles along the whole ridge, 2 = many short bumps.")]
        [Range(0.1f, 4f)]
        public float saddleFrequency = 0.8f;

        [Tooltip("Half-width of the ridge base in metres. Overrides FeaturePath.halfWidth when > 0. " +
                 "Set to 0 to use the spline's own halfWidth.")]
        [Range(0f, 80f)]
        public float halfWidthOverride = 0f;
    }

    // =========================================================================
    //  TerrainFeature contract
    // =========================================================================

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.Ridge;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    /// <summary>Per-feature settings. Created with sensible defaults; a spawner may expose and
    /// assign a custom instance via a public field on a derived spawner class.</summary>
    public RidgeSettings Settings { get; } = new RidgeSettings();

    // =========================================================================
    //  BuildDensity
    // =========================================================================

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        TerrainFeatureTuning tuning = context.Tuning;

        // ------------------------------------------------------------------
        // Spline — fall back to a straight two-point line if path is invalid.
        // ------------------------------------------------------------------
        FeatureSpline spline = BuildSpline(context);
        float halfWidth = ResolveHalfWidth(spline, context);

        // ------------------------------------------------------------------
        // Crest height (with per-feature deterministic variation).
        // ------------------------------------------------------------------
        float crestHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);

        // Maximum slope the walkable surface may have (rise/run).
        // When keepWalkable is false we use a large value to leave slope unlimited.
        float maxSlope = tuning.keepWalkable ? tuning.maxWalkableSlope : 99f;

        // Ridge sharpness: jaggedness drives the cross-section peak shape,
        // but when keepWalkable is on we cap sharpness so the macro flanks
        // remain traversable (crag detail is then added as surface noise only).
        float crossSharpness = tuning.keepWalkable
            ? Mathf.Lerp(0f, 0.35f, tuning.jaggedness)   // soft-to-moderate hump
            : Mathf.Lerp(0f, 0.85f, tuning.jaggedness);  // up to near-knife-edge

        // Footprint bounds for the density band.
        Bounds footprint = ResolveBounds(spline, context, halfWidth);

        // ------------------------------------------------------------------
        // Height lambda — the core of the feature.
        // ------------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY = context.LocalGroundHeight(x, z);

            // ---- Distance to spline centreline --------------------------------
            Vector3 samplePt = new Vector3(x, 0f, z);
            float splineT = spline.ClosestParam(samplePt, out float lateralDist, out Vector3 closest);

            // Signed lateral fraction in [-1, 1]: 0 = crest, ±1 = base edge.
            float lateralFraction = Mathf.Clamp(lateralDist / Mathf.Max(0.01f, halfWidth), -1f, 1f);

            // Outside the half-width entirely → ground level only (let overlap handle the taper).
            float absLateral = Mathf.Abs(lateralFraction);

            // ---- Overlap weight (edge blending) --------------------------------
            // distInside is positive inside the ridge footprint, negative outside.
            float distInside = halfWidth - Mathf.Abs(lateralDist);
            float weight = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
            if (weight <= 0f) return groundY;

            // ---- Along-path crest variation (saddles & peaks) -----------------
            // Use FBM on the spline parameter to get deterministic high/low points.
            Vector3 splineNoisePt = new Vector3(splineT * 10f, 0f, 0f);
            float crestVarianceRaw = TerrainNoiseHelper.Fbm(
                splineNoisePt,
                Settings.saddleFrequency,
                context.Seed + 1337,
                3);
            // crestVarianceRaw is in ~[-1, 1]; map to [0, 1] for additive factor.
            float crestFactor = 1f - Settings.crestVariation * (1f - (crestVarianceRaw * 0.5f + 0.5f));
            float localCrestHeight = crestHeight * Mathf.Max(0f, crestFactor);

            // ---- Slope-limit the along-path height change ----------------------
            // Evaluate crest height at a small step forward so we can clamp the
            // along-path rise, preventing walk-blocking steepness at peak shoulders.
            if (tuning.keepWalkable && localCrestHeight > 0f)
            {
                float stepT = Mathf.Clamp01(splineT + 0.02f);
                Vector3 stepNoisePt = new Vector3(stepT * 10f, 0f, 0f);
                float stepVarianceRaw = TerrainNoiseHelper.Fbm(
                    stepNoisePt,
                    Settings.saddleFrequency,
                    context.Seed + 1337,
                    3);
                float stepFactor = 1f - Settings.crestVariation * (1f - (stepVarianceRaw * 0.5f + 0.5f));
                float stepCrestHeight = crestHeight * Mathf.Max(0f, stepFactor);

                // The physical distance along-path for a 0.02 parameter step;
                // approximate with spline segment length estimate.
                float pathLen = Vector3.Distance(
                    spline.Evaluate(Mathf.Clamp01(splineT)),
                    spline.Evaluate(Mathf.Clamp01(splineT + 0.02f)));
                float stepDist = Mathf.Max(0.1f, pathLen);

                localCrestHeight = TerrainProfiles.LimitSlope(
                    localCrestHeight, stepCrestHeight, stepDist, maxSlope);
            }

            // ---- Ridge cross-section profile ----------------------------------
            float ridgeProfile = TerrainProfiles.Ridge(lateralFraction, crossSharpness);

            // ---- Crag detail noise (high-frequency, NOT slope-breaking) --------
            float surfNoise = TerrainNoiseHelper.SurfaceNoise(samplePt, tuning, context.Seed);
            // Scale crag noise down on the flanks so the feet stay smooth.
            float cragScale = 1f - absLateral * absLateral;
            float scaledNoise = surfNoise * cragScale;

            // ---- Compose height ------------------------------------------------
            float ridgeContribution = localCrestHeight * ridgeProfile + scaledNoise;
            return groundY + ridgeContribution * weight;
        };

        // ------------------------------------------------------------------
        // Vertical band for the marching-cubes mesher.
        // ------------------------------------------------------------------
        float centreGroundY = context.LocalGroundHeight(footprint.center.x, footprint.center.z);
        float bandPadding = context.VoxelSize * 2f;
        float minY = centreGroundY - 4f;
        float maxY = centreGroundY + crestHeight + tuning.noiseAmount + bandPadding;

        return new HeightfieldDensity(heightFn, footprint, minY, maxY, bandPadding);
    }

    // =========================================================================
    //  Private helpers
    // =========================================================================

    /// <summary>Builds a <see cref="FeatureSpline"/> from <c>context.Path</c>. When the path is
    /// invalid (fewer than 2 points) a straight two-point line is synthesised spanning the
    /// <see cref="FeatureContext.LocalBounds"/> box so the feature is still previewable.</summary>
    FeatureSpline BuildSpline(FeatureContext context)
    {
        if (context.Path != null && context.Path.IsValid)
            return new FeatureSpline(context.Path);

        // Synthesise a default straight path along the longest horizontal axis.
        Bounds b = context.LocalBounds;
        float ext = Mathf.Max(b.extents.x, b.extents.z) * 0.8f;
        FeaturePath fallback = new FeaturePath();
        fallback.controlPoints.Add(new Vector3(-ext, 0f, 0f));
        fallback.controlPoints.Add(new Vector3( ext, 0f, 0f));
        fallback.halfWidth = Mathf.Min(b.extents.x, b.extents.z) * 0.5f;
        return new FeatureSpline(fallback);
    }

    /// <summary>Returns the effective half-width: the per-feature override when positive,
    /// otherwise the spline's own <see cref="FeatureSpline.HalfWidth"/>.</summary>
    float ResolveHalfWidth(FeatureSpline spline, FeatureContext context)
    {
        float ow = Settings.halfWidthOverride;
        if (ow > 0f) return ow;
        float sw = spline.HalfWidth;
        // Guard: if the spline has no width (e.g. fallback path), derive from the bounds.
        if (sw > 0.01f) return sw;
        return Mathf.Max(context.LocalBounds.extents.x, context.LocalBounds.extents.z) * 0.3f;
    }

    /// <summary>Returns the XZ footprint bounds the density needs to cover: spline AABB expanded
    /// by the half-width on all sides. Uses <see cref="FeatureContext.LocalBounds"/> as a fallback
    /// when the spline would produce a degenerate box.</summary>
    Bounds ResolveBounds(FeatureSpline spline, FeatureContext context, float halfWidth)
    {
        if (!spline.IsValid) return context.LocalBounds;

        // Walk the spline to compute an XZ AABB.
        const int steps = 32;
        Vector3 min = new Vector3(float.MaxValue,  0f, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue,  0f, float.MinValue);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = spline.Evaluate(i / (float)steps);
            if (p.x < min.x) min.x = p.x;
            if (p.z < min.z) min.z = p.z;
            if (p.x > max.x) max.x = p.x;
            if (p.z > max.z) max.z = p.z;
        }

        min.x -= halfWidth;
        min.z -= halfWidth;
        max.x += halfWidth;
        max.z += halfWidth;

        Vector3 centre = (min + max) * 0.5f;
        Vector3 size   = max - min;
        // Y extent is irrelevant for a heightfield footprint; the density constructor re-derives it.
        size.y = Mathf.Max(context.LocalBounds.size.y, 10f);
        return new Bounds(centre, size);
    }
}
