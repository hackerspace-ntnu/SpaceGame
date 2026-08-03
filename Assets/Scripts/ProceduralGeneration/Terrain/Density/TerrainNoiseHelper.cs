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
    // --- Detail-layer noise calibration --------------------------------------------------------
    // NoiseDistortion.Fbm / DomainWarpedFbm only span ~[-0.55, 0.55] with E[|n|] ≈ 0.131
    // (measured over 20k samples). The detail layer needs a true [-1,1] working range and a
    // zero-mean ridge transform, so these constants normalise the raw noise:
    //   NoiseGain      — multiplier that stretches the raw fbm toward [-1, 1].
    //   AbsNoiseMean   — E[|layer|] AFTER the gain (0.131 × NoiseGain) — the recentring offset
    //                    that keeps the ridge transform zero-mean.
    //   RidgeRescale   — restores roughly unit amplitude after the (|layer| - mean) recentre.
    const float NoiseGain    = 1.85f;
    const float AbsNoiseMean = 0.131f * NoiseGain;   // ≈ 0.242
    const float RidgeRescale = 2.4f;

    /// <summary>
    /// Total surface displacement (metres) at a world/local XZ point. This is THE single central
    /// place every feature gets its organic surface variation; it is the sum of two independent
    /// terms so they can be tuned separately:
    ///
    ///   • MACRO noise  — the old single-field shape noise, scaled by <c>noiseAmount</c>.
    ///   • DETAIL layer — the high-frequency bumpiness layer (<see cref="DetailLayer"/>), scaled by
    ///     the ABSOLUTE <c>detailStrength</c> metre value.
    ///
    /// A feature can therefore be smooth-macro + jagged-detail, or any mix. Returns metres.
    /// </summary>
    public static float SurfaceNoise(Vector3 p, TerrainFeatureTuning tuning, int seed)
    {
        if (tuning == null) return 0f;

        float macro = 0f;
        if (tuning.noiseAmount > 0f)
        {
            var caveType = (CaveNoiseType)(int)tuning.noiseType;
            float m = NoiseDistortion.SampleByType(caveType, p, tuning.noiseScale, seed, tuning.domainWarpStrength);
            m = ApplyJaggedness(m, tuning.jaggedness);
            macro = m * tuning.noiseAmount;
        }

        // The detail layer is keyed off a different seed so it does not correlate with the macro.
        return macro + DetailLayer(p, tuning, seed ^ 0x5C7A11);
    }

    /// <summary>
    /// THE independent surface-detail layer, in METRES. Adds high-frequency bumpiness on top of
    /// whatever macro shape a feature already has. Driven entirely by <see cref="TerrainFeatureTuning"/>'s
    /// "Surface detail" block:
    ///
    ///   • <c>detailStrength</c>   — absolute metre amplitude. 0 ⇒ returns 0 (perfectly smooth).
    ///   • <c>detailScale</c>      — base frequency of the layer (own scale, not the macro one).
    ///   • <c>detailOctaves</c> / <c>detailRoughness</c> / <c>detailLacunarity</c> — fractal shape.
    ///   • <c>detailRidged</c>     — crinkle toward sharp eroded ridges/gullies.
    ///   • <c>detailWarp</c>       — domain-warp so crags swirl and don't read as a grid.
    ///
    /// CRUCIAL: the fractal is normalised to a UNIT field and THEN scaled by <c>detailStrength</c>,
    /// so the strength dial is the true amplitude — adding octaves enriches the shape without
    /// silently changing the displacement. Every feature (heightfield AND voxel) calls this, so the
    /// knobs visibly bite everywhere.
    /// </summary>
    public static float DetailLayer(Vector3 p, TerrainFeatureTuning tuning, int seed)
    {
        if (tuning == null || tuning.detailStrength <= 0f) return 0f;
        return DetailUnit(p, tuning, seed) * tuning.detailStrength;
    }

    /// <summary>
    /// The shaped detail field normalised to roughly [-1, 1] — the unit form of <see cref="DetailLayer"/>
    /// before the <c>detailStrength</c> metre scaling. Voxel features that need to clamp the
    /// displacement against their own geometry (so erosion can never punch through thin rock) call
    /// this and apply their own capped amplitude.
    /// </summary>
    public static float DetailUnit(Vector3 p, TerrainFeatureTuning tuning, int seed)
    {
        if (tuning == null) return 0f;

        int   octaves    = Mathf.Clamp(tuning.detailOctaves, 1, 6);
        float roughness  = Mathf.Clamp01(tuning.detailRoughness);
        float lacunarity = Mathf.Max(1.6f, tuning.detailLacunarity);
        float ridged     = Mathf.Clamp01(tuning.detailRidged);
        float warp       = Mathf.Max(0f, tuning.detailWarp);

        float sum  = 0f;
        float amp  = 1f;
        float norm = 0f;
        float freq = Mathf.Max(0.001f, tuning.detailScale);

        for (int o = 0; o < octaves; o++)
        {
            // Each octave is a domain-warped fbm so the crags swirl; warp scales down per octave so
            // finer detail is warped less (otherwise it just turns to mush). NoiseDistortion.Fbm
            // only spans ~[-0.55, 0.55], so we boost it toward a true [-1, 1] working range.
            float raw = warp > 0f
                ? NoiseDistortion.DomainWarpedFbm(p, freq, seed + o * 131, warp / (o + 1f))
                : NoiseDistortion.Fbm(p, freq, seed + o * 131);
            float layer = Mathf.Clamp(raw * NoiseGain, -1f, 1f);

            // Crinkle this octave toward an eroded ridge field BEFORE summing, so the ridging
            // compounds across scales: broad ridges with finer gullies cut into their flanks.
            // The crease field |layer| is recentred by subtracting its measured mean so the
            // ridged octave stays ZERO MEAN — it crinkles the surface BOTH ways (carving gullies
            // AND raising crests) rather than shifting the whole feature bodily upward.
            if (ridged > 0f)
            {
                float a = Mathf.Abs(layer);                       // V-shaped creases at zero crossings
                float r = -(a - AbsNoiseMean) * RidgeRescale;     // zero-mean, creases read as ridges
                layer = Mathf.Lerp(layer, Mathf.Clamp(r, -1f, 1f), ridged);
            }

            sum  += layer * amp;
            norm += amp;
            amp  *= roughness;
            freq *= lacunarity;
        }

        float n = norm > 0f ? sum / norm : 0f;

        // Final jaggedness sharpening on the combined detail field.
        n = ApplyJaggedness(n, tuning.jaggedness);
        return Mathf.Clamp(n, -1f, 1f);
    }

    /// <summary>
    /// Back-compat shim: the raw shaped surface field in ~[-1, 1]. Now an alias of
    /// <see cref="DetailUnit"/> so older callers keep working and pick up the new detail controls.
    /// </summary>
    public static float DetailedNoise(Vector3 p, TerrainFeatureTuning tuning, int seed)
        => DetailUnit(p, tuning, seed);

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
