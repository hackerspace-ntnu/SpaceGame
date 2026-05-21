using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="StoneArchFeature"/>. All values are local to the feature
/// and override the generic <see cref="TerrainFeatureTuning"/> only for arch-specific geometry.
/// </summary>
[System.Serializable]
public class StoneArchSettings
{
    /// <summary>Thickness of the rock fin / leg cross-section at the base, in metres.</summary>
    [Range(0.5f, 6f)] public float legRadius = 1.8f;

    /// <summary>Thickness of the span at its crown, as a fraction of <see cref="legRadius"/>.
    /// Values below 1 create a thinner crown — more arch, less slab.</summary>
    [Range(0.3f, 1.2f)] public float crownRadiusFraction = 0.65f;

    /// <summary>Radius of the air-window punched through the fin to make the opening, in metres.
    /// Must be large enough that players can walk through at ground level.</summary>
    [Range(1f, 8f)] public float windowRadius = 3.2f;

    /// <summary>Smooth-blend radius used on SdfPrimitives.SmoothMin for blending arch parts and
    /// carving the window. Larger values = more organic, blended transitions.</summary>
    [Range(0.1f, 4f)] public float blendK = 1.4f;

    /// <summary>Strength of high-frequency surface erosion applied on top of TerrainNoiseHelper
    /// noise — pits and pocking that makes the rock read as wind-carved sandstone.</summary>
    [Range(0f, 1f)] public float erosionStrength = 0.55f;
}

/// <summary>
/// Voxel SDF terrain feature: a free-standing wind-eroded stone arch. Two tapering rock pillars
/// rise from the ground and join in a curved span overhead, leaving a clear air window at ground
/// level that players and NavMeshAgents can walk through. Heavy noise erosion gives the surface the
/// pitted, organic look of desert sandstone. Orientation follows <see cref="FeatureContext.Path"/>
/// if valid; otherwise the arch spans the local-bounds long axis.
///
/// Registration: <c>Register(() => new StoneArchFeature());</c>
/// </summary>
public sealed class StoneArchFeature : TerrainFeature
{
    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.StoneArch;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    // Per-instance settings; a spawner may expose these via a serialized field if desired.
    readonly StoneArchSettings _s;

    /// <summary>Create with default arch settings.</summary>
    public StoneArchFeature() : this(new StoneArchSettings()) { }

    /// <summary>Create with caller-supplied arch settings.</summary>
    public StoneArchFeature(StoneArchSettings settings)
    {
        _s = settings ?? new StoneArchSettings();
    }

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box       = context.LocalBounds;
        TerrainFeatureTuning tuning = context.Tuning;
        int seed         = context.Seed;

        // ----------------------------------------------------------------
        // Arch geometry — all in feature-local space
        // ----------------------------------------------------------------

        // Total arch height with per-feature variation.
        float archHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed);

        // Determine span direction: path start→end if a valid path exists, else longest box axis.
        Vector3 spanDir;
        Vector3 archCentre = box.center;
        archCentre.y = context.LocalGroundHeight(box.center.x, box.center.z);

        var spline = new FeatureSpline(context.Path);
        if (spline.IsValid)
        {
            Vector3 pathStart = spline.Evaluate(0f);
            Vector3 pathEnd   = spline.Evaluate(1f);
            Vector3 d = pathEnd - pathStart;
            d.y = 0f;
            spanDir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.right;
            // Anchor arch centre to mid-path XZ, ground Y.
            Vector3 mid = (pathStart + pathEnd) * 0.5f;
            archCentre   = new Vector3(mid.x, archCentre.y, mid.z);
        }
        else
        {
            spanDir = box.size.x >= box.size.z ? Vector3.right : Vector3.forward;
        }

        // Perpendicular (depth) direction — the fin is thin along this axis.
        Vector3 depthDir = Vector3.Cross(spanDir, Vector3.up).normalized;

        // Half-span: distance from arch centre to each leg base, clamped to box.
        float halfSpan = Mathf.Min(box.extents.x, box.extents.z) * 0.72f;
        halfSpan       = Mathf.Max(halfSpan, _s.legRadius * 2.2f);

        // Leg base centres at ground level.
        Vector3 legL = archCentre + spanDir * (-halfSpan);
        Vector3 legR = archCentre + spanDir *   halfSpan;

        // Crown of the arch: above centre by archHeight.
        Vector3 crown = archCentre + Vector3.up * archHeight;

        // Radii — legs taper slightly from base to crown via capsule radius interpolation.
        float baseRadius  = _s.legRadius;
        float crownRadius = _s.legRadius * _s.crownRadiusFraction;

        // Deterministic leg-thickness asymmetry driven by seed.
        float asymL = 1f + (TerrainNoiseHelper.Hash01(seed, 13) - 0.5f) * 0.28f;
        float asymR = 1f + (TerrainNoiseHelper.Hash01(seed, 29) - 0.5f) * 0.28f;

        // Smooth-blend k values.
        float k        = _s.blendK;
        float windowR  = _s.windowRadius;

        // Erosion amplitude from tuning (jaggedness scales detail crunchiness).
        float erosionAmp = tuning.noiseAmount * _s.erosionStrength;
        float noiseScale = tuning.noiseScale;
        float jagg       = tuning.jaggedness;
        var   noiseType  = (CaveNoiseType)(int)tuning.noiseType;
        float warpStr    = tuning.domainWarpStrength;

        // ----------------------------------------------------------------
        // SDF lambda
        // ----------------------------------------------------------------

        System.Func<Vector3, float> sdfFn = p =>
        {
            // --- Solid arch body ---
            // Left leg: capsule from ground base up to crown.
            float legLSdf = SdfPrimitives.Capsule(p, legL, crown, baseRadius * asymL);
            // Right leg: capsule from ground base up to crown.
            float legRSdf = SdfPrimitives.Capsule(p, legR, crown, baseRadius * asymR);
            // Span: capsule connecting the two leg tops, thinner at crown radius.
            float spanSdf = SdfPrimitives.Capsule(p, legL + Vector3.up * archHeight * 0.55f,
                                                     legR + Vector3.up * archHeight * 0.55f,
                                                     crownRadius);

            // Blend all three parts into one rock mass.
            float solid = SdfPrimitives.SmoothMin(legLSdf, legRSdf, k);
            solid        = SdfPrimitives.SmoothMin(solid,  spanSdf,  k);

            // --- Air window ---
            // A large capsule punched horizontally through the fin along the depth axis,
            // positioned between the legs at mid-height so the opening reaches ground level.
            float windowCentreY = archCentre.y + archHeight * 0.38f;
            Vector3 winA = archCentre + depthDir * (box.extents.z + 2f);
            Vector3 winB = archCentre - depthDir * (box.extents.z + 2f);
            winA.y = windowCentreY;
            winB.y = windowCentreY;
            float window = SdfPrimitives.Capsule(p, winA, winB, windowR);

            // Smooth-subtract the window from the solid: max(solid, -window).
            // Use SmoothMin trick: max(a,b) = -SmoothMin(-a,-b,k)
            float carved = -SdfPrimitives.SmoothMin(-solid, window, k * 0.6f);

            // --- Surface erosion ---
            // Low-frequency noise shifts the SDF surface for large organic deformation.
            float nLow  = NoiseDistortion.SampleByType(noiseType, p, noiseScale * 0.5f, seed, warpStr);
            nLow        = TerrainNoiseHelper.ApplyJaggedness(nLow, jagg);
            // High-frequency layer adds pitting and cracks.
            float nHigh = NoiseDistortion.SampleByType(noiseType, p, noiseScale * 2.5f, seed + 53, warpStr * 0.4f);
            float erosion = (nLow * 0.7f + nHigh * 0.3f) * erosionAmp;

            return carved + erosion;
        };

        // ----------------------------------------------------------------
        // Volume bounds — tight around the arch with 2-voxel air padding.
        // ----------------------------------------------------------------
        float pad      = context.VoxelSize * 2f + _s.legRadius;
        float minY     = archCentre.y - pad;
        float maxY     = archCentre.y + archHeight + pad;
        float halfW    = halfSpan + pad;
        float halfD    = Mathf.Max(_s.legRadius, _s.windowRadius) + pad;

        Bounds volumeBounds = new Bounds(
            new Vector3(archCentre.x, (minY + maxY) * 0.5f, archCentre.z),
            new Vector3(halfW * 2f, maxY - minY, halfD * 2f));

        return new VoxelSdfDensity(sdfFn, volumeBounds);
    }
}
