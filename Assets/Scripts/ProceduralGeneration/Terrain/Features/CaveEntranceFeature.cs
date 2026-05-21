using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="CaveEntranceFeature"/>. All values tunable in the spawner
/// inspector via a public field; kept separate from the shared <see cref="TerrainFeatureTuning"/>
/// so the four mandated knobs remain untouched.
/// </summary>
[System.Serializable]
public class CaveEntranceSettings
{
    [Tooltip("Radius of the hillside/mound base sphere, in metres. Should be larger than mouthRadius.")]
    [Range(4f, 40f)] public float moundRadius = 12f;

    [Tooltip("Radius of the tunnel mouth capsule cut into the mound, in metres.")]
    [Range(1f, 8f)] public float mouthRadius = 3.5f;

    [Tooltip("How far the tunnel throat penetrates into the mound before dead-ending, in metres.")]
    [Range(3f, 20f)] public float tunnelDepth = 8f;

    [Tooltip("How high above the mound centre the overhanging brow cap sits, as a fraction of moundRadius.")]
    [Range(0.05f, 0.5f)] public float browFraction = 0.25f;

    [Tooltip("Smooth-min blend radius for carving the mouth. Larger = more eroded, rounded edges.")]
    [Range(0.5f, 6f)] public float smoothBlendK = 2.5f;

    [Tooltip("Noise amplitude applied to the rock surface as an SDF displacement, in metres.")]
    [Range(0f, 4f)] public float surfaceNoiseAmplitude = 1.8f;

    [Tooltip("Strength of floor-flatten in the tunnel throat [0, 1]. 1 = fully flat, 0 = natural capsule.")]
    [Range(0f, 1f)] public float floorFlattenStrength = 0.85f;
}

/// <summary>
/// Surface cave entrance: a sandstone hillside/mound with a horizontal tunnel mouth carved into its
/// face and an overhanging brow above the opening. Produces a genuine 3D overhang — hence Voxel
/// density — so the rock mass above the mouth protrudes over the player without the surface folding
/// being representable as a heightfield.
///
/// Construction layers (negative = solid, positive = air):
///   1. Solid rock MOUND — a large sphere rising from the terrain, anchor base at ground level.
///   2. Tunnel THROAT  — a horizontal capsule subtracted (smooth-union of mound and -throat) to
///      carve the cave mouth. The capsule extends a short distance inward so there is a dead-end
///      pocket the player can walk into; the actual interior cave is a separate scene system.
///   3. Overhanging BROW — the mound sphere above the mouth remains solid because the capsule only
///      carves the lower half of the face; the upper arc of mound rock juts forward as a true overhang.
///   4. FLOOR flatten  — <see cref="SdfPrimitives.ApplyFloorFlatten"/> levels the throat floor so
///      NavMeshAgents transition from open terrain into the tunnel without a step.
///   5. Organic EROSION — <see cref="NoiseDistortion"/> displaces the iso-surface so the mound and
///      mouth read as crumbling sandstone rather than smooth geometric solids.
///
/// Path orientation: if a valid <see cref="FeaturePath"/> is present the tunnel axis is taken from
/// its first-to-second control-point direction (mouth at path start, throat going inward). Otherwise
/// the tunnel faces along the box's local +X axis.
///
/// Registration (add one line to <c>TerrainFeatureRegistry.RegisterBuiltIns</c>):
/// <code>Register(() => new CaveEntranceFeature());</code>
/// </summary>
public sealed class CaveEntranceFeature : TerrainFeature
{
    /// <summary>Per-feature settings; set before <see cref="BuildDensity"/> is called.</summary>
    public CaveEntranceSettings Settings { get; set; } = new CaveEntranceSettings();

    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.CaveEntrance;

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        CaveEntranceSettings cfg = Settings;
        TerrainFeatureTuning tuning = context.Tuning;

        // --- Mound centre: sit on the terrain, centred in the footprint box. ---------------
        float groundY = context.LocalGroundHeight(context.LocalBounds.center.x, context.LocalBounds.center.z);
        float moundH = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);

        // Mound sphere sits so its equator is ~at ground level; the cap rises moundH above it.
        // We push the sphere centre down by (moundRadius - moundH) so the dome peak equals moundH.
        float sphereRadius = Mathf.Max(cfg.moundRadius, moundH);
        float sphereCentreY = groundY - (sphereRadius - moundH);
        Vector3 moundCentre = new Vector3(context.LocalBounds.center.x, sphereCentreY, context.LocalBounds.center.z);

        // --- Tunnel axis from path or box long axis. ----------------------------------------
        Vector3 tunnelDir = TunnelDirection(context);

        // Mouth sits on the FRONT face of the mound sphere (projected out by sphereRadius along the
        // tunnel direction), at a height placing it flush with the walkable ground.
        float mouthY = groundY + cfg.mouthRadius * 0.5f;     // lift centre above ground so floor is walkable
        Vector3 mouthCentre = new Vector3(
            moundCentre.x + tunnelDir.x * sphereRadius * 0.85f,
            mouthY,
            moundCentre.z + tunnelDir.z * sphereRadius * 0.85f);

        // Throat end: punched inward toward mound centre by tunnelDepth.
        Vector3 throatEnd = mouthCentre - tunnelDir * cfg.tunnelDepth;

        // Floor flatten anchor: midpoint of throat capsule, same Y as mouth centre.
        Vector3 flattenAnchor = (mouthCentre + throatEnd) * 0.5f;
        flattenAnchor.y = mouthCentre.y;

        // Pre-capture all values the SDF lambda needs (no closure over 'this' or context).
        float moundR = sphereRadius;
        float mouthR = cfg.mouthRadius;
        float blendK = cfg.smoothBlendK;
        float flattenDepth = mouthR;
        float flattenStrength = cfg.floorFlattenStrength;
        float noiseAmp = cfg.surfaceNoiseAmplitude * Mathf.Lerp(0.5f, 1.5f, tuning.jaggedness);
        float noiseFreq = tuning.noiseScale;
        int seed = context.Seed;
        var noiseType = (CaveNoiseType)(int)tuning.noiseType;
        float warpStr = tuning.domainWarpStrength;

        // --- SDF lambda -----------------------------------------------------------------
        System.Func<Vector3, float> sdf = p =>
        {
            // 1. Solid rock mound (negative inside).
            float dMound = SdfPrimitives.Sphere(p, moundCentre, moundR);

            // 2. Tunnel capsule (we want to subtract: carve air).
            float dTunnel = SdfPrimitives.Capsule(p, mouthCentre, throatEnd, mouthR);

            // Smooth-subtract the tunnel from the mound:
            //   union(mound, -tunnel) implemented as SmoothMin(dMound, -dTunnel, k)
            //   so the mouth edges are eroded, not razor-clean.
            float dCarved = SdfPrimitives.SmoothMin(dMound, -dTunnel, blendK);

            // 3. Flatten the throat floor so NavMeshAgents can walk in.
            dCarved = SdfPrimitives.ApplyFloorFlatten(
                dCarved, p, flattenAnchor,
                flattenDepth,
                Vector2.zero,           // gentle horizontal slope, no tilt
                0f,
                flattenStrength);

            // 4. Surface noise — displace only near the iso-surface to avoid popping interior pockets.
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

        // --- Volume bounds (tight + 2-voxel air pad on every side). ----------------------
        float pad = context.VoxelSize * 2f + noiseAmp;
        // The volume must cover: mound sphere + 2 voxel padding.
        float halfW = sphereRadius + pad;
        float minY = sphereCentreY - sphereRadius - pad;
        float maxY = sphereCentreY + sphereRadius + pad;
        Bounds volumeBounds = new Bounds(
            new Vector3(moundCentre.x, (minY + maxY) * 0.5f, moundCentre.z),
            new Vector3(halfW * 2f, maxY - minY, halfW * 2f));

        return new VoxelSdfDensity(sdf, volumeBounds);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the normalised horizontal tunnel direction. Reads the path first-segment direction
    /// when the path is valid (≥2 points), otherwise falls back to the box's long axis.
    /// </summary>
    static Vector3 TunnelDirection(FeatureContext context)
    {
        // Try path direction.
        var spline = new FeatureSpline(context.Path);
        if (spline.IsValid)
        {
            Vector3 t = spline.Tangent(0f);
            t.y = 0f;
            if (t.sqrMagnitude > 1e-4f) return t.normalized;
        }

        // Fall back to the box's long XZ axis.
        Bounds b = context.LocalBounds;
        return b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
    }
}
