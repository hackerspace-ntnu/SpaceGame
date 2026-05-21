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

    /// <summary>Headroom kept clear under the crown, in metres — the walk-through opening height.
    /// The arch span is lifted so its underside clears this above the higher leg base.</summary>
    [Range(1f, 8f)] public float openingHeight = 3.2f;

    /// <summary>Smooth-blend radius used on <see cref="SdfPrimitives.SmoothMin"/> for blending the
    /// legs and span into one rock mass. Larger values = more organic, blended transitions.</summary>
    [Range(0.1f, 4f)] public float blendK = 1.4f;

    /// <summary>Strength of high-frequency surface erosion applied on top of TerrainNoiseHelper
    /// noise — pits and pocking that makes the rock read as wind-carved sandstone.</summary>
    [Range(0f, 1f)] public float erosionStrength = 0.55f;
}

/// <summary>
/// Voxel SDF terrain feature: a free-standing wind-eroded stone arch that visibly SPANS between two
/// points. Two tapering rock legs touch down on the terrain under the two path endpoints — each
/// grounded at its own local ground height — and join in a smooth curved span overhead, leaving a
/// clear walk-through air opening underneath at ground level. Heavy noise erosion gives the surface
/// the pitted, organic look of desert sandstone. Orientation follows <see cref="FeatureContext.Path"/>
/// when valid (legs at path start/end); otherwise the arch spans the local-bounds long axis.
///
/// Registration: <c>Register(() => new StoneArchFeature());</c>
/// </summary>
public sealed class StoneArchFeature : TerrainFeature
{
    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.StoneArch;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    /// <summary>Per-instance settings; injected by the spawner via <see cref="ApplySettings"/>.
    /// Never null — falls back to a fresh default instance.</summary>
    StoneArchSettings _s = new StoneArchSettings();

    /// <summary>Create with default arch settings.</summary>
    public StoneArchFeature() { }

    /// <summary>Create with caller-supplied arch settings.</summary>
    public StoneArchFeature(StoneArchSettings settings)
    {
        _s = settings ?? new StoneArchSettings();
    }

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => new StoneArchSettings();

    /// <inheritdoc/>
    public override void ApplySettings(object settings)
    {
        if (settings is StoneArchSettings s) _s = s;
        else _s ??= new StoneArchSettings();
    }

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box                  = context.LocalBounds;
        TerrainFeatureTuning tuning  = context.Tuning;
        int seed                    = context.Seed;

        // ----------------------------------------------------------------
        // Endpoints — the two things the arch spans BETWEEN.
        // With a valid path the legs anchor at the path start/end; without
        // one they anchor at the two ends of the box long axis.
        // ----------------------------------------------------------------
        var spline = new FeatureSpline(context.Path);

        Vector3 footL, footR;
        if (spline.IsValid)
        {
            Vector3 a = spline.Evaluate(0f);
            Vector3 b = spline.Evaluate(1f);
            footL = new Vector3(a.x, 0f, a.z);
            footR = new Vector3(b.x, 0f, b.z);
        }
        else
        {
            bool alongX = box.size.x >= box.size.z;
            Vector3 dir = alongX ? Vector3.right : Vector3.forward;
            float half  = (alongX ? box.extents.x : box.extents.z) * 0.72f;
            footL = box.center - dir * half;
            footR = box.center + dir * half;
            footL.y = footR.y = 0f;
        }

        // Ground each leg on its OWN local terrain height so an arch dragged
        // between two mesas touches down correctly on each side.
        footL.y = context.LocalGroundHeight(footL.x, footL.z);
        footR.y = context.LocalGroundHeight(footR.x, footR.z);

        // Span direction (XZ) and the thin-fin depth axis perpendicular to it.
        Vector3 spanVec = footR - footL; spanVec.y = 0f;
        Vector3 spanDir = spanVec.sqrMagnitude > 0.01f ? spanVec.normalized : Vector3.right;
        Vector3 depthDir = Vector3.Cross(spanDir, Vector3.up).normalized;

        // ----------------------------------------------------------------
        // Arch geometry — feature-local space.
        // ----------------------------------------------------------------
        float archHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed);
        float baseGround = Mathf.Max(footL.y, footR.y);

        // Leg tops: where each pillar stops rising and the curved span takes over.
        // Lifted clear of the higher foot by the requested walk-through headroom.
        float legTopY  = baseGround + Mathf.Max(_s.openingHeight, archHeight * 0.45f);
        float crownY   = legTopY + archHeight * 0.55f;
        Vector3 legTopL = new Vector3(footL.x, legTopY, footL.z);
        Vector3 legTopR = new Vector3(footR.x, legTopY, footR.z);

        // Radii — legs taper from base to a thinner crown.
        float baseRadius  = _s.legRadius;
        float crownRadius = _s.legRadius * _s.crownRadiusFraction;

        // Deterministic leg-thickness asymmetry driven by seed.
        float asymL = 1f + (TerrainNoiseHelper.Hash01(seed, 13) - 0.5f) * 0.28f;
        float asymR = 1f + (TerrainNoiseHelper.Hash01(seed, 29) - 0.5f) * 0.28f;

        float k = _s.blendK;

        // Erosion parameters from shared tuning.
        float erosionAmp = tuning.noiseAmount * _s.erosionStrength;
        float noiseScale = tuning.noiseScale;
        float jagg       = tuning.jaggedness;
        var   noiseType  = (CaveNoiseType)(int)tuning.noiseType;
        float warpStr    = tuning.domainWarpStrength;

        // Crown arc sampled as a chain of capsules — a true curved arch, not a
        // straight slab. Each segment lifts toward crownY following a parabola.
        const int arcSegments = 8;

        // ----------------------------------------------------------------
        // SDF lambda — negative = solid rock, positive = air.
        // ----------------------------------------------------------------
        System.Func<Vector3, float> sdfFn = p =>
        {
            // Two tapering legs: capsules from ground foot up to the leg top.
            float legLSdf = SdfPrimitives.Capsule(p, footL, legTopL, baseRadius * asymL);
            float legRSdf = SdfPrimitives.Capsule(p, footR, legTopR, baseRadius * asymR);
            float solid   = SdfPrimitives.SmoothMin(legLSdf, legRSdf, k);

            // Curved crown: union of short capsules tracing a parabola from
            // legTopL up over crownY and back down to legTopR.
            Vector3 prev = legTopL;
            for (int i = 1; i <= arcSegments; i++)
            {
                float t  = i / (float)arcSegments;
                // Parabola peaking at t=0.5 — lift above the straight leg-top line.
                float lift = 4f * (crownY - legTopY) * t * (1f - t);
                Vector3 cur = Vector3.Lerp(legTopL, legTopR, t) + Vector3.up * lift;
                float seg = SdfPrimitives.Capsule(p, prev, cur, crownRadius);
                solid = SdfPrimitives.SmoothMin(solid, seg, k);
                prev = cur;
            }

            // --- Surface erosion ---
            // Low-frequency noise for large organic deformation of the surface.
            float nLow  = NoiseDistortion.SampleByType(noiseType, p, noiseScale * 0.5f, seed, warpStr);
            nLow        = TerrainNoiseHelper.ApplyJaggedness(nLow, jagg);
            // High-frequency layer adds pitting and cracks.
            float nHigh = NoiseDistortion.SampleByType(noiseType, p, noiseScale * 2.5f, seed + 53, warpStr * 0.4f);
            float erosion = (nLow * 0.7f + nHigh * 0.3f) * erosionAmp;

            return solid + erosion;
        };

        // ----------------------------------------------------------------
        // Volume bounds — enclose both grounded legs, the crown and padding.
        // Legs may sit at different ground heights, so derive Y from both.
        // ----------------------------------------------------------------
        float pad = context.VoxelSize * 2f + _s.legRadius + erosionAmp;

        float minX = Mathf.Min(footL.x, footR.x) - baseRadius - pad;
        float maxX = Mathf.Max(footL.x, footR.x) + baseRadius + pad;
        float minZ = Mathf.Min(footL.z, footR.z) - baseRadius - pad;
        float maxZ = Mathf.Max(footL.z, footR.z) + baseRadius + pad;
        float minY = Mathf.Min(footL.y, footR.y) - pad;
        float maxY = crownY + crownRadius + pad;

        Bounds volumeBounds = new Bounds(
            new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f),
            new Vector3(maxX - minX, maxY - minY, maxZ - minZ));

        return new VoxelSdfDensity(sdfFn, volumeBounds);
    }
}
