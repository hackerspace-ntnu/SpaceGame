using UnityEngine;

/// <summary>
/// Visual / structural style of a <see cref="SpanFeature"/>. Both styles share ALL the SDF
/// construction code in <see cref="SpanFeature"/>; the style only shifts the default settings
/// (mainly arch rise and deck width) toward one of two recognisable silhouettes.
/// </summary>
public enum SpanStyle
{
    /// <summary>A thick, wide, gently-arched deck you can walk continuously end-to-end.</summary>
    WalkableBridge,

    /// <summary>A tall, narrow, dramatically curved free-standing stone arch.</summary>
    DramaticArch,
}

/// <summary>
/// Shared designer-tunable settings for the merged span feature (natural bridge + stone arch).
/// One settings class backs BOTH <see cref="NaturalBridgeFeature"/> and <see cref="StoneArchFeature"/>;
/// the <see cref="style"/> flag is what makes the same code read as a walkable bridge or a tall arch.
/// </summary>
[System.Serializable]
public class SpanFeatureSettings
{
    [Tooltip("Overall silhouette: a wide walkable bridge, or a tall dramatic arch.")]
    public SpanStyle style = SpanStyle.WalkableBridge;

    [Tooltip("Width of the deck PERPENDICULAR to the span direction, in metres. The walkable surface width.")]
    [Range(1f, 24f)] public float spanWidth = 7f;

    [Tooltip("Half-thickness (top-to-bottom) of the deck slab, in metres. Solid rock you walk on top of.")]
    [Range(0.6f, 8f)] public float deckHalfThickness = 2.5f;

    [Tooltip("Extra height the arch rises at mid-span above the straight abutment line. Arches go very high here.")]
    [Range(0f, 60f)] public float archRise = 5f;

    [Tooltip("Radius of the solid rock abutment spheres anchoring each end into the ground.")]
    [Range(1f, 18f)] public float abutmentRadius = 6f;

    [Tooltip("Smooth-blend radius k for SmoothMin. Larger = more organic flowing rock joins.")]
    [Range(0f, 6f)] public float smoothBlendK = 2.5f;

    [Tooltip("Number of capsule segments tracing the deck arc. More = smoother curve.")]
    [Range(6, 32)] public int deckSegments = 14;

    [Tooltip("Strength of surface noise erosion. Auto-clamped so it can never punch a hole through the deck.")]
    [Range(0f, 1f)] public float erosionStrength = 0.45f;

    /// <summary>Returns a fresh settings instance pre-tuned for the given style.</summary>
    public static SpanFeatureSettings ForStyle(SpanStyle style)
    {
        var s = new SpanFeatureSettings { style = style };
        if (style == SpanStyle.WalkableBridge)
        {
            s.spanWidth = 8f;
            s.deckHalfThickness = 2.8f;
            s.archRise = 5f;
            s.abutmentRadius = 6.5f;
            s.smoothBlendK = 2.5f;
            s.deckSegments = 14;
            s.erosionStrength = 0.4f;
        }
        else // DramaticArch
        {
            s.spanWidth = 3.5f;
            s.deckHalfThickness = 1.6f;
            s.archRise = 26f;
            s.abutmentRadius = 5f;
            s.smoothBlendK = 1.6f;
            s.deckSegments = 20;
            s.erosionStrength = 0.5f;
        }
        return s;
    }
}

/// <summary>
/// Merged voxel-SDF span feature backing BOTH the natural bridge and the stone arch — one
/// implementation, two thin subclasses (<see cref="NaturalBridgeFeature"/>,
/// <see cref="StoneArchFeature"/>) differing only in <see cref="TerrainFeature.FeatureType"/> and
/// default <see cref="SpanStyle"/>.
///
/// SDF construction (negative = solid, positive = air): the deck is swept as a chain of WIDE
/// flattened capsules along a parabolic arch between two ground-anchored sphere abutments, unioned
/// with <see cref="SdfPrimitives.SmoothMin"/>. Width perpendicular to the span is a real tunable
/// (<c>spanWidth</c>) — the cross-section is flattened so it reads as a walkable slab, not a tube.
///
/// COHESIVENESS GUARANTEE: consecutive deck segments share endpoints and the sweep radius exceeds
/// the per-segment arc sagitta so neighbours provably overlap into one solid; deck endpoints sit
/// deep inside the abutment spheres; surface erosion is HARD-CLAMPED to a safe fraction of the deck
/// half-thickness so it can never punch a hole through the walkable surface.
/// </summary>
public abstract class SpanFeature : TerrainFeature
{
    /// <summary>Per-instance settings, injected by the spawner. Never null.</summary>
    SpanFeatureSettings _settings;

    /// <summary>The style this concrete subclass defaults to (bridge vs arch).</summary>
    protected abstract SpanStyle DefaultStyle { get; }

    /// <inheritdoc/>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    /// <inheritdoc/>
    public override object CreateDefaultSettings() => SpanFeatureSettings.ForStyle(DefaultStyle);

    /// <inheritdoc/>
    public override void ApplySettings(object settings)
    {
        if (settings is SpanFeatureSettings s) _settings = s;
        else _settings ??= SpanFeatureSettings.ForStyle(DefaultStyle);
    }

    /// <inheritdoc/>
    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        SpanFeatureSettings cfg = _settings ?? SpanFeatureSettings.ForStyle(DefaultStyle);
        TerrainFeatureTuning tuning = context.Tuning;
        int seed = context.Seed;

        // ---- Resolve span endpoints from path or box long axis ------------------
        var spline = new FeatureSpline(context.Path);
        Vector3 startPt, endPt;
        if (spline.IsValid)
        {
            startPt = spline.Evaluate(0f);
            endPt   = spline.Evaluate(1f);
        }
        else
        {
            Bounds box = context.LocalBounds;
            bool alongX = box.extents.x >= box.extents.z;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            float halfLong = (alongX ? box.extents.x : box.extents.z) * 0.8f;
            startPt = box.center - axis * halfLong;
            endPt   = box.center + axis * halfLong;
        }

        // ---- Ground-anchor each abutment on its OWN local ground height ---------
        startPt.y = context.LocalGroundHeight(startPt.x, startPt.z);
        endPt.y   = context.LocalGroundHeight(endPt.x,   endPt.z);

        // ---- Deterministic arch rise (per-feature variation, seed-only) ---------
        float archRise   = TerrainNoiseHelper.VariedHeight(cfg.archRise, tuning, seed);
        float halfWidth  = Mathf.Max(0.5f, cfg.spanWidth * 0.5f);
        float halfThick  = Mathf.Max(0.3f, cfg.deckHalfThickness);
        float abutRadius = Mathf.Max(halfThick, cfg.abutmentRadius);
        float blendK     = cfg.smoothBlendK;
        int   segments   = Mathf.Max(6, cfg.deckSegments);

        // Span-perpendicular horizontal axis (the WIDTH direction of the deck).
        Vector3 spanVec = endPt - startPt; spanVec.y = 0f;
        Vector3 sideDir = spanVec.sqrMagnitude > 1e-4f
            ? Vector3.Cross(spanVec.normalized, Vector3.up).normalized
            : Vector3.forward;

        // ---- Pre-build arch sample points (consecutive segments share endpoints) -
        var deckA = new Vector3[segments];
        var deckB = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            deckA[i] = ArchPoint(startPt, endPt, i / (float)segments, archRise);
            deckB[i] = ArchPoint(startPt, endPt, (i + 1) / (float)segments, archRise);
        }

        // Sweep radius must exceed the per-segment arc sagitta so neighbouring
        // straight capsules overlap into one solid — guarantees a connected deck.
        float sagitta = archRise * 4f / (segments * segments) + 0.01f;
        float sweepR  = Mathf.Max(halfThick, sagitta * 1.5f) + 0.05f;

        // ---- Erosion: clamp so it can NEVER exceed the rock it can absorb ------
        // Amplitude = the feature's own macro erosion PLUS the shared surface-detail layer, so the
        // central "Surface detail" dials reach the bridge/arch. Capped to 60% of the deck half-
        // thickness so even a violently jagged setting leaves >=40% solid rock through the deck.
        float rawErosionAmp = tuning.noiseAmount * cfg.erosionStrength + tuning.detailStrength;
        float erosionCap    = halfThick * 0.6f;
        float erosionAmp    = Mathf.Min(rawErosionAmp, erosionCap);

        Vector3 abutA = startPt;
        Vector3 abutB = endPt;
        // Capture the whole tuning so erosion runs through the shared DetailedNoise — the central
        // "Surface detail" dials reach the bridge/arch like every other feature.
        TerrainFeatureTuning noiseTuning = tuning;

        // ---- SDF lambda ---------------------------------------------------------
        System.Func<Vector3, float> sdfFn = p =>
        {
            // 1. Deck — chain of WIDE flattened capsules. Width comes from squashing
            //    the side-axis distance: a point that is far along the width axis
            //    is treated as closer, so the cross-section becomes a wide slab.
            float deck = WideDeckSegment(p, deckA[0], deckB[0], sweepR, halfWidth, halfThick, sideDir);
            for (int i = 1; i < segments; i++)
            {
                float seg = WideDeckSegment(p, deckA[i], deckB[i], sweepR, halfWidth, halfThick, sideDir);
                deck = SdfPrimitives.SmoothMin(deck, seg, blendK);
            }

            // 2. Abutment spheres — deck endpoints already sit inside these.
            float sphA = SdfPrimitives.Sphere(p, abutA, abutRadius);
            float sphB = SdfPrimitives.Sphere(p, abutB, abutRadius);
            float shape = SdfPrimitives.SmoothMin(deck, sphA, blendK);
            shape = SdfPrimitives.SmoothMin(shape, sphB, blendK);

            // 3. Clamped surface erosion — never punches through the deck.
            if (erosionAmp > 0f)
            {
                float n = TerrainNoiseHelper.DetailUnit(p, noiseTuning, seed);
                shape += n * erosionAmp;
            }

            return shape;
        };

        Bounds volume = ComputeVolumeBounds(
            startPt, endPt, archRise, sideDir, halfWidth, sweepR, halfThick, abutRadius,
            erosionAmp, context.VoxelSize);

        return new VoxelSdfDensity(sdfFn, volume);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// One WIDE deck segment. A capsule between <paramref name="a"/> and <paramref name="b"/> is
    /// stretched along <paramref name="sideDir"/> into a flat slab: distance along the width axis
    /// is allowed up to <paramref name="halfWidth"/> for free, so the cross-section is a wide deck
    /// of tunable width rather than a round tube. Height is bounded by <paramref name="halfThick"/>.
    /// </summary>
    static float WideDeckSegment(
        Vector3 p, Vector3 a, Vector3 b,
        float sweepR, float halfWidth, float halfThick, Vector3 sideDir)
    {
        // Closest point on the segment centre-line.
        Vector3 ba = b - a;
        Vector3 pa = p - a;
        float baLen2 = Vector3.Dot(ba, ba);
        float h = baLen2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pa, ba) / baLen2) : 0f;
        Vector3 onLine = a + ba * h;
        Vector3 d = p - onLine;

        // Decompose the offset into the width axis and the remaining (height/length) part.
        float side = Vector3.Dot(d, sideDir);
        Vector3 nonSide = d - sideDir * side;

        // Width: free travel up to halfWidth, then it counts as outside distance.
        float widthDist = Mathf.Abs(side) - halfWidth;
        // Round cross-section part (capsule radius) flattened by the vertical clamp.
        float roundDist = nonSide.magnitude - sweepR;
        // Vertical clamp so a wide slab does not balloon into a fat cylinder.
        float vertDist  = Mathf.Abs(nonSide.y) - halfThick;
        // The flattened cross-section distance: outside if past round OR vertical face.
        float lateral   = Mathf.Max(roundDist, vertDist);

        // Box-style SDF: positive part is the Euclidean distance outside the slab;
        // negative part is the (negative) distance to the nearest face when inside.
        float outX = Mathf.Max(0f, widthDist);
        float outL = Mathf.Max(0f, lateral);
        float outside = Mathf.Sqrt(outX * outX + outL * outL);
        float inside  = Mathf.Min(0f, Mathf.Max(widthDist, lateral));
        return outside + inside;
    }

    /// <summary>
    /// Point on the parabolic arch at normalised t in [0,1]. Quadratic Bezier whose mid control
    /// point is lifted by <paramref name="archRise"/> above the start–end midpoint.
    /// </summary>
    static Vector3 ArchPoint(Vector3 start, Vector3 end, float t, float archRise)
    {
        Vector3 mid = (start + end) * 0.5f + new Vector3(0f, archRise, 0f);
        float u = 1f - t;
        return u * u * start + 2f * u * t * mid + t * t * end;
    }

    /// <summary>Axis-aligned volume enclosing the full span + width + radii + erosion + padding.</summary>
    static Bounds ComputeVolumeBounds(
        Vector3 start, Vector3 end, float archRise, Vector3 sideDir,
        float halfWidth, float sweepR, float halfThick, float abutRadius,
        float erosionAmp, float voxelSize)
    {
        Vector3 apex = (start + end) * 0.5f + new Vector3(0f, archRise, 0f);
        Bounds b = new Bounds(start, Vector3.zero);
        b.Encapsulate(end);
        b.Encapsulate(apex);
        // Extend along the width axis so the wide deck is fully enclosed.
        b.Encapsulate(apex + sideDir * halfWidth);
        b.Encapsulate(apex - sideDir * halfWidth);

        float pad = voxelSize * 2f + erosionAmp;
        float radial = Mathf.Max(Mathf.Max(sweepR, halfThick), abutRadius) + pad;
        b.Expand(radial * 2f);
        return b;
    }
}
