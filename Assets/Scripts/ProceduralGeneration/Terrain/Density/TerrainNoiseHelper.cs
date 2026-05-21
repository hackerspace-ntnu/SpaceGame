using UnityEngine;

/// <summary>
/// Shared noise + falloff helpers every terrain feature should call rather than reinventing.
/// Thin terrain-flavoured wrapper over the cave system's <see cref="NoiseDistortion"/> (which is
/// reused as-is for the actual noise functions) plus the falloff / overlap maths the nine features
/// all need.
///
/// Everything here is deterministic and allocation-free — pass the feature seed straight through.
/// </summary>
public static class TerrainNoiseHelper
{
    /// <summary>
    /// Surface noise displacement (metres) at a world/local XZ point, driven by the shared tuning
    /// block. Reuses <see cref="NoiseDistortion"/> for the underlying field and applies the
    /// jaggedness exponent so the same call gives soft swells or sharp crags depending on tuning.
    /// Returns roughly [-noiseAmount, +noiseAmount].
    /// </summary>
    public static float SurfaceNoise(Vector3 p, TerrainFeatureTuning tuning, int seed)
    {
        if (tuning == null || tuning.noiseAmount <= 0f) return 0f;

        // NoiseDistortion's CaveNoiseType maps 1:1 onto TerrainNoiseType (same three styles).
        var caveType = (CaveNoiseType)(int)tuning.noiseType;
        float n = NoiseDistortion.SampleByType(caveType, p, tuning.noiseScale, seed, tuning.domainWarpStrength);

        // Jaggedness sharpens the field: a ridge transform pulls values toward sharp creases.
        n = ApplyJaggedness(n, tuning.jaggedness);
        return n * tuning.noiseAmount;
    }

    /// <summary>
    /// Sharpen a roughly-[-1,1] noise value into ridged/craggy terrain. At jaggedness 0 the value
    /// is returned unchanged (soft, rolling). At jaggedness 1 it becomes a sharp ridge field
    /// (1 - |n|) remapped — knife ridges and steep faces. Used for cliffs, mesas and ridges.
    /// </summary>
    public static float ApplyJaggedness(float n, float jaggedness)
    {
        if (jaggedness <= 0f) return n;
        float ridged = 1f - Mathf.Abs(n);          // [0,1], peaks where n crossed zero
        ridged = ridged * ridged;                  // sharpen the crease
        ridged = ridged * 2f - 1f;                 // back to [-1,1]
        return Mathf.Lerp(n, ridged, Mathf.Clamp01(jaggedness));
    }

    /// <summary>
    /// Multi-octave fractal noise for broad-then-fine terrain detail. <paramref name="octaves"/>
    /// layers, each double the frequency and half the amplitude of the last. Returns ~[-1, 1].
    /// </summary>
    public static float Fbm(Vector3 p, float frequency, int seed, int octaves)
    {
        float sum = 0f;
        float amp = 1f;
        float norm = 0f;
        float freq = frequency;
        for (int o = 0; o < Mathf.Max(1, octaves); o++)
        {
            sum += NoiseDistortion.Sample(p, freq, seed + o * 31) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return norm > 0f ? sum / norm : 0f;
    }

    /// <summary>
    /// Edge-overlap falloff weight in [0, 1]. Given a signed distance INTO the footprint from its
    /// boundary (negative = outside, positive = inside), returns 0 outside, ramps up across the
    /// <c>overlap</c> band using the tuning's <c>overlapFalloff</c> curve, and 1 deep inside.
    /// This is the single shared function that makes neighbouring features blend smoothly and the
    /// feature fade into the surrounding terrain with no hard seam.
    /// </summary>
    public static float OverlapWeight(float distanceInside, TerrainFeatureTuning tuning)
    {
        if (tuning == null) return distanceInside > 0f ? 1f : 0f;
        float band = Mathf.Max(0.0001f, tuning.overlap);
        float t = Mathf.Clamp01(distanceInside / band);
        return tuning.overlapFalloff != null ? Mathf.Clamp01(tuning.overlapFalloff.Evaluate(t)) : t;
    }

    /// <summary>
    /// Deterministic per-feature random value in [0, 1] from the seed and a salt. Use for
    /// height-variation jitter and similar one-shot per-feature randomness. Same seed+salt always
    /// gives the same value.
    /// </summary>
    public static float Hash01(int seed, int salt)
    {
        unchecked
        {
            int h = seed * 374761393 + salt * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7FFFFFFF) / (float)int.MaxValue;
        }
    }

    /// <summary>
    /// Applies <see cref="TerrainFeatureTuning.heightVariation"/> to a base height: returns
    /// <paramref name="baseHeight"/> scaled by a deterministic per-feature factor in
    /// [1 - variation, 1 + variation]. Every feature should funnel its 'height' tuning through this.
    /// </summary>
    public static float VariedHeight(float baseHeight, TerrainFeatureTuning tuning, int seed)
    {
        if (tuning == null || tuning.heightVariation <= 0f) return baseHeight;
        float r = Hash01(seed, 7919) * 2f - 1f;          // [-1, 1]
        return baseHeight * (1f + r * tuning.heightVariation);
    }
}
