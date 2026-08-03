using UnityEngine;

/// <summary>
/// Library of analytic cross-section / silhouette profile functions shared by the nine terrain
/// features. A feature picks a profile, scales it by its tuning height, and adds
/// <see cref="TerrainNoiseHelper.SurfaceNoise"/> on top. Centralising these here means a feature
/// agent writes "dune profile along the wind axis" in one line instead of re-deriving the maths.
///
/// All profiles take a normalised coordinate (usually in [-1, 1] or [0, 1]) and return a
/// normalised height/depth in [0, 1] which the feature then multiplies by metres.
/// </summary>
public static class TerrainProfiles
{
    /// <summary>
    /// Asymmetric dune profile. <paramref name="t"/> runs [-1, 1] across the dune along the wind
    /// direction; the crest sits at <paramref name="crestBias"/> (0 = centre, positive = leeward).
    /// The windward side is a gentle ramp, the leeward side a steep slip face — the classic
    /// barchan shape. Returns [0, 1] height.
    /// </summary>
    public static float Dune(float t, float crestBias)
    {
        t = Mathf.Clamp(t, -1f, 1f);
        float crest = Mathf.Clamp(crestBias, -0.9f, 0.9f);
        if (t <= crest)
        {
            // Windward: smooth ramp from base up to the crest.
            float u = Mathf.InverseLerp(-1f, crest, t);
            return Mathf.SmoothStep(0f, 1f, u);
        }
        // Leeward: steep slip face dropping back to base.
        float d = Mathf.InverseLerp(crest, 1f, t);
        return 1f - d * d;
    }

    /// <summary>
    /// Flat-topped plateau profile (mesa / butte silhouette). <paramref name="edgeDistance"/> is
    /// the normalised distance from the footprint edge inward, [0, 1]. The result is 0 at the
    /// edge, rises steeply across <paramref name="wallFraction"/> of the span, then is a flat 1
    /// across the top. Gives steep-sided, flat-topped rock.
    /// </summary>
    public static float Plateau(float edgeDistance, float wallFraction)
    {
        float w = Mathf.Clamp(wallFraction, 0.01f, 1f);
        float u = Mathf.Clamp01(edgeDistance / w);
        return Mathf.SmoothStep(0f, 1f, u);
    }

    /// <summary>
    /// Ridge / canyon-wall profile. <paramref name="t"/> runs [-1, 1] across the cross-section;
    /// 0 is the centre-line. Returns [0, 1] peaking at the centre — use directly for a ridge, or
    /// subtract from 1 for a canyon (deepest at the centre). <paramref name="sharpness"/> 0 is a
    /// round hump, 1 is a sharp peak.
    /// </summary>
    public static float Ridge(float t, float sharpness)
    {
        float a = 1f - Mathf.Abs(Mathf.Clamp(t, -1f, 1f));
        float round = Mathf.SmoothStep(0f, 1f, a);
        float sharp = a;                              // linear tent = sharper crest
        return Mathf.Lerp(round, sharp, Mathf.Clamp01(sharpness));
    }

    /// <summary>
    /// Smooth single-sided escarpment step (cliff profile). <paramref name="t"/> runs [-1, 1]
    /// across the footprint; the result eases from 0 on the low side to 1 on the high side, with
    /// the transition centred at <paramref name="edge"/> and spread over <paramref name="width"/>.
    /// </summary>
    public static float CliffStep(float t, float edge, float width)
    {
        float w = Mathf.Max(0.01f, width);
        float u = Mathf.Clamp01((t - edge) / w + 0.5f);
        return Mathf.SmoothStep(0f, 1f, u);
    }

    /// <summary>
    /// U-shaped canyon cross-section depth. <paramref name="lateralFraction"/> is the normalised
    /// distance from the path centre-line, [0, 1] (0 = floor centre, 1 = canyon rim). Returns
    /// normalised DEPTH in [0, 1]: 1 at the centre (deepest), 0 at the rim. <paramref name="floorFlat"/>
    /// widens the flat walkable floor band before the walls begin.
    /// </summary>
    public static float CanyonCrossSection(float lateralFraction, float floorFlat)
    {
        float f = Mathf.Clamp01(lateralFraction);
        float flat = Mathf.Clamp01(floorFlat);
        if (f <= flat) return 1f;                     // flat canyon floor
        float wall = Mathf.InverseLerp(flat, 1f, f);  // [0,1] up the wall
        return 1f - Mathf.SmoothStep(0f, 1f, wall);
    }

    /// <summary>
    /// Clamps a height function's slope so a walkable surface stays under a NavMeshAgent limit.
    /// Given the height at a point and the height one step away, returns an adjusted height whose
    /// slope to the neighbour does not exceed <paramref name="maxSlope"/> (rise/run). Apply this
    /// iteratively across a feature's walkable band — it is the heightfield analogue of the cave
    /// system's <c>SdfPrimitives.ApplyFloorFlatten</c> slope thinking.
    /// </summary>
    public static float LimitSlope(float height, float neighbourHeight, float stepDistance, float maxSlope)
    {
        float maxRise = maxSlope * Mathf.Max(0.0001f, stepDistance);
        return Mathf.Clamp(height, neighbourHeight - maxRise, neighbourHeight + maxRise);
    }
}
