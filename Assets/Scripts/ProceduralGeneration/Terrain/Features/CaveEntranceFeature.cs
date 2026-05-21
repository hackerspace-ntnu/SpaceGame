using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="CaveEntranceFeature"/>. All values tunable in the spawner
/// inspector via the <c>[SerializeReference]</c> settings object; kept separate from the shared
/// <see cref="TerrainFeatureTuning"/> so the four mandated knobs remain untouched.
/// </summary>
[System.Serializable]
public class CaveEntranceSettings
{
    [Tooltip("Radius of the tunnel mouth carved into the rock face, in metres.")]
    [Range(1.5f, 8f)] public float mouthRadius = 3.5f;

    [Tooltip("How far the tunnel throat penetrates inward before dead-ending, in metres.")]
    [Range(4f, 24f)] public float tunnelDepth = 10f;

    [Tooltip("How much rock mass surrounds the opening. 0 = a minimal rocky lip framing the " +
             "mouth; 1 = a large hillside. Scales rock-blob size and count.")]
    [Range(0f, 1f)] public float surroundingRockAmount = 0.5f;

    [Tooltip("Vertical slope of the tunnel throat. 0 = runs flat into the mountain; negative = " +
             "the throat descends down into the mountain as it goes inward. Drives the throat " +
             "end Y drop and the walkable floor slope.")]
    [Range(-1f, 1f)] public float descentSlope = -0.25f;

    [Tooltip("How far the rock brow juts forward over the mouth, as a fraction of mouthRadius. " +
             "Larger = a deeper overhang shading the opening.")]
    [Range(0.1f, 1f)] public float browOverhang = 0.5f;

    [Tooltip("Smooth-min blend radius for carving the mouth and blending blobs. Larger = more " +
             "eroded, rounded edges.")]
    [Range(0.5f, 6f)] public float smoothBlendK = 2.5f;

    [Tooltip("Noise amplitude eroding the rock surface as an SDF displacement, in metres.")]
    [Range(0f, 4f)] public float surfaceNoiseAmplitude = 1.8f;
}

/// <summary>
/// Surface cave entrance: a sandstone rocky outcrop rising from the terrain with a tunnel mouth
/// carved into its face and an overhanging brow of rock above the opening. The brow juts forward
/// over the mouth — a genuine 3D overhang — hence the Voxel density. The throat can be tuned to
/// descend down into the mountain or run flat, and the surrounding rock mass scales from a small
/// outcrop to a whole hillside.
///
/// Construction layers (negative = solid rock, positive = air):
///   1. ROCK MASS  — several spheres blended with <see cref="SdfPrimitives.SmoothMin"/> so the
///      silhouette reads as a natural outcrop, not one perfect dome. Its base is anchored to
///      <see cref="FeatureContext.LocalGroundHeight"/> so it sits on and rises above the terrain.
///   2. THROAT     — a capsule smooth-subtracted from the mass, opening on the face at ground
///      level and dead-ending inward; <c>descentSlope</c> tilts the inner end downward.
///   3. BROW       — only the lower part of the face is carved, so the rock above the mouth stays
///      solid and overhangs the opening.
///   4. FLOOR      — a LOCAL slope-tracking flatten masked to the throat region only, giving
///      NavMeshAgents a walkable floor that follows the descent without flattening the whole volume.
///   5. EROSION    — <see cref="NoiseDistortion"/> displaces the iso-surface so it reads as
///      crumbling sandstone rather than smooth geometric solids.
///
/// Path orientation: when a valid <see cref="FeaturePath"/> is present the tunnel axis follows its
/// first-segment direction (mouth at path start, throat inward); otherwise it faces the box long
/// axis. <c>descentSlope</c> tilts the throat downward regardless of the horizontal direction.
///
/// Registration (one line in <c>TerrainFeatureRegistry.RegisterBuiltIns</c>):
/// <code>Register(() => new CaveEntranceFeature());</code>
/// </summary>
public sealed class CaveEntranceFeature : TerrainFeature
{
    /// <summary>Per-feature settings; injected via <see cref="ApplySettings"/> before build.</summary>
    public CaveEntranceSettings Settings { get; set; } = new CaveEntranceSettings();

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.CaveEntrance;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => new CaveEntranceSettings();

    /// <inheritdoc/>
    public override void ApplySettings(object settings)
    {
        Settings = settings as CaveEntranceSettings ?? new CaveEntranceSettings();
    }

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        CaveEntranceSettings cfg = Settings ?? new CaveEntranceSettings();
        TerrainFeatureTuning tuning = context.Tuning;
        int seed = context.Seed;

        // --- Footprint anchor: the rock mass sits ON the terrain. ---------------------------
        Vector2 centreXZ = new Vector2(context.LocalBounds.center.x, context.LocalBounds.center.z);
        float groundY = context.LocalGroundHeight(centreXZ.x, centreXZ.y);

        // --- Rock-mass size scales with surroundingRockAmount. ------------------------------
        // Low amount = a minimal lip just bigger than the mouth; high = a broad hillside.
        float massRadius = Mathf.Lerp(cfg.mouthRadius * 1.8f, cfg.mouthRadius * 5.5f,
                                      cfg.surroundingRockAmount);
        float massHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed)
                           + massRadius * 0.6f;

        // --- Tunnel axis (horizontal). ------------------------------------------------------
        Vector3 dir = TunnelDirection(context);

        // Mouth opens on the rock face at ground level so an agent walks straight in. The mass
        // centre sits BEHIND the mouth so the bulk of the rock is over and around the opening.
        Vector3 mouthCentre = new Vector3(centreXZ.x, groundY + cfg.mouthRadius, centreXZ.y);
        Vector3 massCentre = mouthCentre - dir * massRadius * 0.55f;
        massCentre.y = groundY + massHeight * 0.5f;

        // Throat: punched inward; descentSlope drops the inner end down into the mountain.
        float endDrop = Mathf.Clamp(cfg.descentSlope, -1f, 1f) * cfg.tunnelDepth;
        Vector3 throatEnd = mouthCentre - dir * cfg.tunnelDepth + Vector3.up * endDrop;

        // --- Surrounding blobs: deterministic offsets give an organic, lumpy outcrop. -------
        int blobCount = 1 + Mathf.RoundToInt(cfg.surroundingRockAmount * 3f);
        var blobs = new Vector4[blobCount];               // xyz = centre, w = radius
        blobs[0] = new Vector4(massCentre.x, massCentre.y, massCentre.z, massRadius);
        for (int i = 1; i < blobCount; i++)
        {
            float ang = TerrainNoiseHelper.Hash01(seed, i * 7 + 1) * Mathf.PI * 2f;
            float dist = massRadius * (0.4f + TerrainNoiseHelper.Hash01(seed, i * 7 + 2) * 0.6f);
            float r = massRadius * (0.45f + TerrainNoiseHelper.Hash01(seed, i * 7 + 3) * 0.4f);
            float bx = massCentre.x + Mathf.Cos(ang) * dist;
            float bz = massCentre.z + Mathf.Sin(ang) * dist;
            float by = groundY + r * (0.55f + TerrainNoiseHelper.Hash01(seed, i * 7 + 4) * 0.4f);
            blobs[i] = new Vector4(bx, by, bz, r);
        }

        // --- Capture immutables for the deterministic SDF lambda. ---------------------------
        float mouthR = cfg.mouthRadius;
        float blendK = cfg.smoothBlendK;
        float browK = cfg.browOverhang;
        float noiseAmp = cfg.surfaceNoiseAmplitude * Mathf.Lerp(0.5f, 1.5f, tuning.jaggedness);
        float noiseFreq = tuning.noiseScale;
        var noiseType = (CaveNoiseType)(int)tuning.noiseType;
        float warpStr = tuning.domainWarpStrength;
        Vector3 mouth = mouthCentre, end = throatEnd;
        float groundLevel = groundY;
        Vector4[] rockBlobs = blobs;

        // --- SDF lambda ---------------------------------------------------------------------
        System.Func<Vector3, float> sdf = p =>
        {
            // 1. Rock mass — blended blobs form a natural lumpy outcrop.
            float dRock = 1e6f;
            for (int i = 0; i < rockBlobs.Length; i++)
            {
                Vector4 b = rockBlobs[i];
                float s = SdfPrimitives.Sphere(p, new Vector3(b.x, b.y, b.z), b.w);
                dRock = SdfPrimitives.SmoothMin(dRock, s, blendK);
            }

            // 2. Throat capsule. Only carve the LOWER face: shrink the carve smoothly above the
            //    mouth so the rock above stays solid → genuine overhanging brow.
            float dThroat = SdfPrimitives.Capsule(p, mouth, end, mouthR);
            float browLine = mouth.y + mouthR * (1f - browK);
            if (p.y > browLine)
            {
                // Inflate (push positive = toward air-only) the throat SDF above the brow line so
                // it stops carving, leaving a solid rock lip jutting over the opening.
                dThroat += (p.y - browLine);
            }

            // 3. Smooth-subtract the throat from the rock mass: union(rock, -throat).
            float dCarved = SdfPrimitives.SmoothMin(dRock, -dThroat, blendK);

            // 4. LOCAL walkable floor — masked to the throat region ONLY so it never flattens the
            //    whole volume (the old bug: ApplyFloorFlatten cut an INFINITE half-space plane).
            //    The floor Y follows the throat axis, so it tracks the descentSlope and stays
            //    navmeshable. The mask falls off with distance to the throat axis.
            Vector3 axisPt = ClosestPointOnSegment(p, mouth, end);
            float axisDist = new Vector2(p.x - axisPt.x, p.z - axisPt.z).magnitude;
            float floorMask = 1f - Mathf.Clamp01(axisDist / (mouthR + blendK));
            if (floorMask > 0f && dCarved > 0f)
            {
                float floorY = axisPt.y - mouthR * 0.85f;        // walkable floor under the axis
                float halfSpace = floorY - p.y;                  // solid below floorY
                dCarved = Mathf.Lerp(dCarved, Mathf.Max(dCarved, halfSpace), floorMask);
            }

            // 5. Surface erosion — displace only near the iso-surface so noise cannot pop pockets.
            if (noiseAmp > 0f)
            {
                float falloff = 1f - Mathf.Clamp01(Mathf.Abs(dCarved) / Mathf.Max(0.01f, noiseAmp * 2f));
                if (falloff > 0f)
                {
                    float n = NoiseDistortion.SampleByType(noiseType, p, noiseFreq, seed, warpStr);
                    dCarved += n * noiseAmp * falloff;
                }
            }

            return dCarved;
        };

        // --- Volume bounds: enclose every blob + the (possibly descended) throat + padding. -
        float pad = context.VoxelSize * 2f + noiseAmp + blendK;
        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (Vector4 b in blobs)
        {
            lo = Vector3.Min(lo, new Vector3(b.x - b.w, b.y - b.w, b.z - b.w));
            hi = Vector3.Max(hi, new Vector3(b.x + b.w, b.y + b.w, b.z + b.w));
        }
        foreach (Vector3 t in new[] { mouthCentre, throatEnd })
        {
            lo = Vector3.Min(lo, t - Vector3.one * mouthR);
            hi = Vector3.Max(hi, t + Vector3.one * mouthR);
        }
        lo -= Vector3.one * pad;
        hi += Vector3.one * pad;
        Bounds volumeBounds = new Bounds((lo + hi) * 0.5f, hi - lo);

        return new VoxelSdfDensity(sdf, volumeBounds);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the normalised horizontal tunnel direction. Uses the path first-segment direction
    /// when the path is valid (≥2 points), otherwise the box's longer XZ axis.
    /// </summary>
    static Vector3 TunnelDirection(FeatureContext context)
    {
        var spline = new FeatureSpline(context.Path);
        if (spline.IsValid)
        {
            Vector3 t = spline.Tangent(0f);
            t.y = 0f;
            if (t.sqrMagnitude > 1e-4f) return t.normalized;
        }
        Bounds b = context.LocalBounds;
        return b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
    }

    /// <summary>Closest point on segment a→b to p (full 3D), used to derive the throat axis Y so
    /// the walkable floor tracks the descent slope.</summary>
    static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = Vector3.Dot(ab, ab);
        float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
        return a + ab * t;
    }
}
