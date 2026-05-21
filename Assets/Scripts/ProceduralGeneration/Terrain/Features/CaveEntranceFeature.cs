using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="CaveEntranceFeature"/>. All values tunable in the spawner
/// inspector via the <c>[SerializeReference]</c> settings object; kept separate from the shared
/// <see cref="TerrainFeatureTuning"/> so the four mandated knobs remain untouched. These knobs let
/// the designer dial the feature from a small boulder-with-a-hole up to a big mine-shaft hillside.
/// </summary>
[System.Serializable]
public class CaveEntranceSettings
{
    [Tooltip("Radius of the main rock mass, in metres. Small = a boulder you could walk around; " +
             "large = a whole hillside. The whole entrance scales around this.")]
    [Range(6f, 40f)] public float rockRadius = 16f;

    [Tooltip("How far the rock mass rises above the terrain, as a fraction of rockRadius. " +
             "Larger = a tall, prominent landmark hill rather than a flat mound.")]
    [Range(0.6f, 2f)] public float rockHeightScale = 1.1f;

    [Tooltip("Width of the tunnel mouth bored into the rock face, in metres (left-to-right span).")]
    [Range(2f, 10f)] public float entranceWidth = 4.5f;

    [Tooltip("Height of the tunnel mouth, in metres (floor-to-lintel). Should comfortably clear " +
             "a walking agent.")]
    [Range(2.5f, 9f)] public float entranceHeight = 4f;

    [Tooltip("How far the tunnel throat bores straight into the rock before dead-ending, in metres.")]
    [Range(5f, 30f)] public float tunnelDepth = 14f;

    [Tooltip("Vertical slope of the tunnel throat. 0 = runs flat into the mountain; negative = " +
             "the throat descends down into the mountain like a mine shaft as it goes inward.")]
    [Range(-1f, 0.4f)] public float descentSlope = -0.2f;

    [Tooltip("How far the rock brow/lintel juts forward over the mouth, as a fraction of the " +
             "entrance height. Larger = a deeper, more sheltering overhang above the portal.")]
    [Range(0f, 1f)] public float browOverhang = 0.45f;

    [Tooltip("Smooth-min blend radius for fusing the rock blobs and carving the mouth. Larger = " +
             "more eroded, rounded rock; smaller = sharper, blockier rock.")]
    [Range(0.5f, 5f)] public float smoothBlendK = 2f;

    [Tooltip("Noise amplitude eroding the rock surface as an SDF displacement, in metres. Gives " +
             "the mass a natural crumbling-sandstone look instead of smooth geometric solids.")]
    [Range(0f, 4f)] public float surfaceNoiseAmplitude = 1.6f;
}

/// <summary>
/// Surface MINE ENTRANCE: a big chunky rock mass / hillside rising from the terrain with a clear,
/// deliberate tunnel portal bored straight into its face. The portal is framed by rock on every
/// side (a brow/lintel above, jambs on both sides, a walkable floor below) and the tunnel throat
/// bores a short distance inward and dead-ends — the real interior is a separate scene system.
///
/// Construction layers (negative = solid rock, positive = air):
///   1. ROCK MASS  — several spheres blended with <see cref="SdfPrimitives.SmoothMin"/> so the
///      silhouette reads as a lumpy natural rock hill, not one perfect dome. Its base is anchored
///      to <see cref="FeatureContext.LocalGroundHeight"/> so it sits on and rises well above the
///      terrain as a landmark. Size driven by <see cref="CaveEntranceSettings.rockRadius"/>.
///   2. PORTAL     — a roughly rectangular tunnel mouth (an axis-aligned rounded box) smooth-
///      subtracted from the mass at ground level, so an agent walks straight in. Its size is
///      driven by <see cref="CaveEntranceSettings.entranceWidth"/> / <c>entranceHeight</c>.
///   3. THROAT     — the opening continues inward as a capsule that dead-ends; <c>descentSlope</c>
///      tilts the inner end downward like a mine shaft.
///   4. BROW       — only the lower face is carved: the carve is inflated above the lintel line so
///      the rock above the mouth stays solid and overhangs the portal.
///   5. FLOOR      — a LOCAL slope-tracking flatten masked to the throat region ONLY, giving
///      NavMeshAgents a walkable floor that follows the descent without flattening the whole
///      volume (the old infinite-half-space-plane bug is deliberately NOT reintroduced).
///   6. EROSION    — <see cref="NoiseDistortion"/> displaces the iso-surface near the surface only
///      so it reads as crumbling sandstone and cannot punch spurious holes.
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

        // --- Rock-mass dimensions. ----------------------------------------------------------
        float rockR = cfg.rockRadius;
        float rockH = rockR * cfg.rockHeightScale;          // total rise above the terrain

        // --- Tunnel axis (horizontal travel direction into the rock). -----------------------
        Vector3 dir = TunnelDirection(context);

        // --- Portal half-extents. The mouth opens on the rock face at ground level so an agent
        //     walks straight in; its floor sits ON the terrain. ------------------------------
        float halfW = cfg.entranceWidth * 0.5f;
        float halfH = cfg.entranceHeight * 0.5f;
        Vector3 sideAxis = new Vector3(-dir.z, 0f, dir.x);  // horizontal, perpendicular to dir
        Vector3 mouthCentre = new Vector3(centreXZ.x, groundY + halfH, centreXZ.y);

        // The mass centre sits BEHIND and slightly into the mouth so the bulk of rock frames and
        // overhangs the opening rather than burying it.
        Vector3 massCentre = mouthCentre - dir * rockR * 0.45f;
        massCentre.y = groundY + rockH * 0.42f;

        // Throat: bores inward from just behind the portal; descentSlope drops the inner end.
        float endDrop = Mathf.Clamp(cfg.descentSlope, -1f, 0.4f) * cfg.tunnelDepth;
        Vector3 throatStart = mouthCentre + dir * halfW;     // start inside the portal box
        Vector3 throatEnd = mouthCentre - dir * cfg.tunnelDepth + Vector3.up * endDrop;

        // --- Surrounding blobs: deterministic offsets give an organic, lumpy rock hill. -----
        const int blobCount = 4;
        var blobs = new Vector4[blobCount];                  // xyz = centre, w = radius
        blobs[0] = new Vector4(massCentre.x, massCentre.y, massCentre.z, rockR);
        for (int i = 1; i < blobCount; i++)
        {
            float ang = TerrainNoiseHelper.Hash01(seed, i * 7 + 1) * Mathf.PI * 2f;
            float dist = rockR * (0.35f + TerrainNoiseHelper.Hash01(seed, i * 7 + 2) * 0.55f);
            float r = rockR * (0.5f + TerrainNoiseHelper.Hash01(seed, i * 7 + 3) * 0.45f);
            float bx = massCentre.x + Mathf.Cos(ang) * dist;
            float bz = massCentre.z + Mathf.Sin(ang) * dist;
            float by = groundY + r * (0.45f + TerrainNoiseHelper.Hash01(seed, i * 7 + 4) * 0.5f);
            blobs[i] = new Vector4(bx, by, bz, r);
        }

        // --- Capture immutables for the deterministic SDF lambda. ---------------------------
        float blendK = cfg.smoothBlendK;
        float browInflate = halfH * 2f * cfg.browOverhang;   // how hard the brow caps the carve
        float noiseAmp = cfg.surfaceNoiseAmplitude * Mathf.Lerp(0.5f, 1.5f, tuning.jaggedness);
        float noiseFreq = tuning.noiseScale;
        var noiseType = (CaveNoiseType)(int)tuning.noiseType;
        float warpStr = tuning.domainWarpStrength;
        Vector3 mouth = mouthCentre, tStart = throatStart, tEnd = throatEnd;
        Vector3 fwd = dir, side = sideAxis;
        float portalHalfW = halfW, portalHalfH = halfH;
        float throatR = Mathf.Min(halfW, halfH) * 0.95f;     // throat radius from portal size
        float lintelY = mouthCentre.y + halfH;               // top of the portal opening
        Vector4[] rockBlobs = blobs;

        // --- SDF lambda ---------------------------------------------------------------------
        System.Func<Vector3, float> sdf = p =>
        {
            // 1. Rock mass — blended blobs form a chunky lumpy rock hill.
            float dRock = 1e6f;
            for (int i = 0; i < rockBlobs.Length; i++)
            {
                Vector4 b = rockBlobs[i];
                float s = SdfPrimitives.Sphere(p, new Vector3(b.x, b.y, b.z), b.w);
                dRock = SdfPrimitives.SmoothMin(dRock, s, blendK);
            }

            // 2. PORTAL — a rounded box opening (the deliberate rectangular mine-mouth) plus the
            //    THROAT capsule continuing inward. Union of the two gives air the agent walks into.
            float dPortal = RoundedBoxOpening(p, mouth, fwd, side, portalHalfW, portalHalfH);
            float dThroat = SdfPrimitives.Capsule(p, tStart, tEnd, throatR);
            float dOpening = SdfPrimitives.SmoothMin(dPortal, dThroat, blendK * 0.5f);

            // 3. BROW — keep the rock above the lintel solid: inflate the opening SDF above the
            //    lintel line so it stops carving, leaving a solid rock brow jutting over the mouth.
            if (p.y > lintelY)
            {
                dOpening += (p.y - lintelY) + browInflate;
            }

            // 4. Smooth-subtract the opening from the rock mass: union(rock, -opening).
            float dCarved = SdfPrimitives.SmoothMin(dRock, -dOpening, blendK);

            // 5. LOCAL walkable floor — masked to the throat region ONLY so it never flattens the
            //    whole volume (the old bug cut an INFINITE half-space plane). The floor Y follows
            //    the throat axis so it tracks descentSlope and stays navmeshable; the mask falls
            //    off with horizontal distance to the throat axis.
            Vector3 axisPt = ClosestPointOnSegment(p, mouth, tEnd);
            float axisDist = new Vector2(p.x - axisPt.x, p.z - axisPt.z).magnitude;
            float floorMask = 1f - Mathf.Clamp01(axisDist / (portalHalfW + blendK));
            if (floorMask > 0f && dCarved > 0f)
            {
                float floorY = axisPt.y - portalHalfH * 0.92f;   // walkable floor under the axis
                float halfSpace = floorY - p.y;                  // solid below floorY
                dCarved = Mathf.Lerp(dCarved, Mathf.Max(dCarved, halfSpace), floorMask);
            }

            // 6. Surface erosion — displace only near the iso-surface so noise cannot pop pockets.
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
        float reach = Mathf.Max(portalHalfW, portalHalfH) + throatR;
        foreach (Vector3 t in new[] { mouthCentre, throatStart, throatEnd })
        {
            lo = Vector3.Min(lo, t - Vector3.one * reach);
            hi = Vector3.Max(hi, t + Vector3.one * reach);
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
    /// SDF of a rounded, axis-oriented box opening — the deliberate rectangular mine-mouth. The box
    /// is centred at <paramref name="centre"/>, oriented by the horizontal <paramref name="fwd"/>
    /// (depth axis) and <paramref name="side"/> (width axis) with world-up as the height axis. It
    /// is deep enough along <paramref name="fwd"/> to cut cleanly through the rock face; lateral
    /// half-extents are <paramref name="halfW"/> (width) and <paramref name="halfH"/> (height).
    /// Returned distance is slightly rounded so the portal has eroded, mine-like corners.
    /// </summary>
    static float RoundedBoxOpening(Vector3 p, Vector3 centre, Vector3 fwd, Vector3 side,
                                   float halfW, float halfH)
    {
        Vector3 d = p - centre;
        float along = Vector3.Dot(d, fwd);          // depth axis (into the rock)
        float lat = Vector3.Dot(d, side);           // width axis
        float up = d.y;                             // height axis
        const float round = 0.6f;                   // corner-rounding radius
        float halfDepth = halfW * 1.6f;             // generous depth so it pierces the face
        Vector3 q = new Vector3(
            Mathf.Abs(along) - halfDepth + round,
            Mathf.Abs(up) - halfH + round,
            Mathf.Abs(lat) - halfW + round);
        Vector3 qMax = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
        return qMax.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - round;
    }

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
