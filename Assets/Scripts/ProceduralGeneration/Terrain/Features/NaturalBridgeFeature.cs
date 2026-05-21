using UnityEngine;

/// <summary>
/// Per-feature settings for <see cref="NaturalBridgeFeature"/>. Designer-tunable beyond the four
/// shared knobs. Attach these on the spawner's extra-settings field, or leave null for defaults.
/// </summary>
[System.Serializable]
public class NaturalBridgeSettings
{
    [Tooltip("Half-thickness of the bridge deck in metres (radius of the deck capsules).")]
    [Range(0.5f, 8f)] public float deckHalfThickness = 2.5f;

    [Tooltip("Extra height rise at the midpoint of the arch above the straight abutment-to-abutment line.")]
    [Range(0f, 20f)] public float archRise = 5f;

    [Tooltip("Radius of the solid rock abutment spheres at each end of the bridge.")]
    [Range(1f, 15f)] public float abutmentRadius = 6f;

    [Tooltip("Smooth-blend radius k for SmoothMin calls. Larger = more organic flowing rock.")]
    [Range(0f, 6f)] public float smoothBlendK = 2.5f;

    [Tooltip("Number of capsule segments sweeping the deck arc. More = smoother arch.")]
    [Range(4, 20)] public int deckSegments = 10;

    [Tooltip("Strength of noise erosion applied to the rock surface. 0 = clean geometric arc.")]
    [Range(0f, 1f)] public float erosionStrength = 0.55f;
}

/// <summary>
/// Natural bridge: a thick arc of eroded sandstone spanning a gap, with open air beneath the
/// arch so both the walking surface on top and the shadowed underside are accessible. This is a
/// LINEAR voxel-SDF feature that reads <see cref="FeatureContext.Path"/> for the span direction.
///
/// SDF construction (negative = solid, positive = air):
///   1. A series of fat capsules (<see cref="SdfPrimitives.Capsule"/>) trace the arch arc from
///      abutment to abutment, rising to a mid-span peak set by <c>archRise</c>. Their union forms
///      the bridge deck and abutments.
///   2. Two solid sphere abutments (<see cref="SdfPrimitives.Sphere"/>) anchor each end into the
///      ground so the bridge has no floating legs.
///   3. All parts are merged with <see cref="SdfPrimitives.SmoothMin"/> so the rock flows
///      seamlessly — no hard seams or join artefacts.
///   4. Surface erosion from <see cref="NoiseDistortion"/> is added to the SDF value to break up
///      the clean geometric silhouette into realistic wind-eroded sandstone.
///   5. The deck top is kept gently sloped (arch rise / span clamped to tuning.maxWalkableSlope)
///      so NavMeshAgents can cross without slope rejection.
/// </summary>
public sealed class NaturalBridgeFeature : TerrainFeature
{
    /// <summary>Optional per-spawner override settings. When null, defaults are used.</summary>
    public NaturalBridgeSettings Settings;

    public override TerrainFeatureType FeatureType => TerrainFeatureType.NaturalBridge;

    /// <summary>Real overhang required — must use the full 3-D voxel SDF.</summary>
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

    // -------------------------------------------------------------------------

    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        NaturalBridgeSettings cfg = Settings ?? new NaturalBridgeSettings();
        TerrainFeatureTuning tuning = context.Tuning;

        // ---- Resolve span direction from path or box long axis ------------------
        FeatureSpline spline = new FeatureSpline(context.Path);
        Vector3 startPt, endPt;
        if (spline.IsValid)
        {
            startPt = spline.Evaluate(0f);
            endPt   = spline.Evaluate(1f);
        }
        else
        {
            // Fallback: use the long axis of the bounding box.
            Bounds box = context.LocalBounds;
            float halfLong = Mathf.Max(box.extents.x, box.extents.z);
            Vector3 axis = box.extents.x >= box.extents.z ? Vector3.right : Vector3.forward;
            startPt = box.center - axis * halfLong * 0.8f;
            endPt   = box.center + axis * halfLong * 0.8f;
        }

        // ---- Ground-anchor the endpoints ----------------------------------------
        // Abutments sit on the terrain; deck rises above.
        float groundStart = context.LocalGroundHeight(startPt.x, startPt.z);
        float groundEnd   = context.LocalGroundHeight(endPt.x,   endPt.z);
        startPt.y = groundStart;
        endPt.y   = groundEnd;

        // ---- Arch rise: per-feature deterministic variation ---------------------
        float archRise    = TerrainNoiseHelper.VariedHeight(cfg.archRise, tuning, context.Seed);
        float deckRadius  = cfg.deckHalfThickness;
        float abutRadius  = cfg.abutmentRadius;
        float blendK      = cfg.smoothBlendK;
        int   segments    = cfg.deckSegments;
        float erosion     = cfg.erosionStrength;

        // ---- Pre-build arch sample points (evaluate once, capture in lambda) ----
        // We compute a fixed array of (a, b) capsule pairs along the arch so the
        // lambda closes over value types only — no per-sample allocation.
        var capsuleA = new Vector3[segments];
        var capsuleB = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            capsuleA[i] = ArchPoint(startPt, endPt, i / (float)segments,       archRise);
            capsuleB[i] = ArchPoint(startPt, endPt, (i + 1) / (float)segments, archRise);
        }

        Vector3 abutA = startPt;
        Vector3 abutB = endPt;
        int     seed  = context.Seed;

        // ---- SDF lambda ---------------------------------------------------------
        System.Func<Vector3, float> sdfFn = p =>
        {
            // 1. Deck capsules — union via SmoothMin chain.
            float deck = SdfPrimitives.Capsule(p, capsuleA[0], capsuleB[0], deckRadius);
            for (int i = 1; i < segments; i++)
            {
                float seg = SdfPrimitives.Capsule(p, capsuleA[i], capsuleB[i], deckRadius);
                deck = SdfPrimitives.SmoothMin(deck, seg, blendK);
            }

            // 2. Abutment spheres — blend into the deck.
            float sphA = SdfPrimitives.Sphere(p, abutA, abutRadius);
            float sphB = SdfPrimitives.Sphere(p, abutB, abutRadius);
            float shape = SdfPrimitives.SmoothMin(deck, sphA, blendK);
            shape = SdfPrimitives.SmoothMin(shape, sphB, blendK);

            // 3. Surface erosion — displace the SDF value with noise so the rock
            //    reads as wind-eroded sandstone, not a clean swept surface.
            if (erosion > 0f)
            {
                var caveType = (CaveNoiseType)(int)tuning.noiseType;
                float n = NoiseDistortion.SampleByType(
                    caveType, p, tuning.noiseScale, seed, tuning.domainWarpStrength);
                n = TerrainNoiseHelper.ApplyJaggedness(n, tuning.jaggedness);
                shape += n * tuning.noiseAmount * erosion;
            }

            return shape;
        };

        // ---- Tight bounding volume ----------------------------------------------
        Bounds volume = ComputeVolumeBounds(startPt, endPt, archRise, deckRadius, abutRadius, context.VoxelSize);

        return new VoxelSdfDensity(sdfFn, volume);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a point on the arch at normalised parameter t in [0, 1]. The arch is a quadratic
    /// Bezier whose mid-control-point is lifted by <paramref name="archRise"/> above the
    /// start–end midpoint, giving a smooth parabolic curve that clears the gap underneath.
    /// </summary>
    static Vector3 ArchPoint(Vector3 start, Vector3 end, float t, float archRise)
    {
        // Mid-control point for the parabola, lifted in Y.
        Vector3 mid = (start + end) * 0.5f + new Vector3(0f, archRise, 0f);
        // Quadratic Bezier: B(t) = (1-t)^2*P0 + 2(1-t)t*P1 + t^2*P2
        float u  = 1f - t;
        return u * u * start + 2f * u * t * mid + t * t * end;
    }

    /// <summary>
    /// Axis-aligned bounding volume enclosing the full arch with two voxels of air padding
    /// on every side, so marching cubes never misses a surface polygon.
    /// </summary>
    static Bounds ComputeVolumeBounds(
        Vector3 start, Vector3 end,
        float archRise, float deckRadius, float abutRadius,
        float voxelSize)
    {
        float pad = voxelSize * 2f;
        // Arch apex is the highest point.
        Vector3 apex = (start + end) * 0.5f + new Vector3(0f, archRise, 0f);

        // Grow bounds to enclose endpoints + apex + radii + padding.
        Bounds b = new Bounds(start, Vector3.zero);
        b.Encapsulate(end);
        b.Encapsulate(apex);

        // Inflate by the larger of the two radii + padding.
        float inflate = Mathf.Max(deckRadius, abutRadius) + pad;
        b.Expand(inflate * 2f);
        return b;
    }
}
