using UnityEngine;

/// <summary>
/// Noise functions used by <see cref="CaveSdfField"/> to ripple cave walls.
///
/// Three styles, selected per profile via <see cref="CaveNoiseType"/>:
///   • Perlin       — 3-axis perlin sum, smooth bubbly walls. Earth-like.
///   • DomainWarped — perlin sample point is itself displaced by a second perlin, producing
///                    swirling, alien, less-repetitive walls.
///   • Cellular     — distance-to-nearest-jittered-grid-point, giving chunky broken-rock cracks.
///
/// All functions return roughly [-1, 1] and are seeded so two generations with the same seed
/// give bit-identical results.
/// </summary>
public static class NoiseDistortion
{
    // -------------------------------------------------------------------------
    // Public dispatch — what CaveSdfField calls
    // -------------------------------------------------------------------------

    public static float SampleByType(CaveNoiseType type, Vector3 p, float frequency, int seed, float domainWarpStrength)
    {
        switch (type)
        {
            case CaveNoiseType.DomainWarped: return DomainWarpedFbm(p, frequency, seed, domainWarpStrength);
            case CaveNoiseType.Cellular:     return Cellular(p, frequency, seed);
            default:                         return Fbm(p, frequency, seed);
        }
    }

    // -------------------------------------------------------------------------
    // Perlin
    // -------------------------------------------------------------------------

    /// <summary>Returns roughly [-1, 1] noise value at the given world position.</summary>
    public static float Sample(Vector3 p, float frequency, int seed)
    {
        Vector3 o = SeedOffset(seed);
        float x = (p.x + o.x) * frequency;
        float y = (p.y + o.y) * frequency;
        float z = (p.z + o.z) * frequency;

        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);

        return ((xy + yz + xz) / 3f) * 2f - 1f;
    }

    /// <summary>2-octave fbm for a bit more visual variety.</summary>
    public static float Fbm(Vector3 p, float frequency, int seed)
    {
        float a = Sample(p, frequency, seed);
        float b = Sample(p, frequency * 2.13f, seed + 17) * 0.5f;
        return (a + b) / 1.5f;
    }

    // -------------------------------------------------------------------------
    // Domain-warped
    // -------------------------------------------------------------------------

    /// <summary>
    /// Perlin noise sampled at p + warp(p). The warp is itself a low-frequency perlin
    /// vector field, which gives swirling, organic-feeling distortion that's hard to read
    /// as obvious perlin "blobs."
    /// </summary>
    public static float DomainWarpedFbm(Vector3 p, float frequency, int seed, float warpStrength)
    {
        // Warp field — 3 independent perlins per axis, evaluated at a lower frequency so the
        // distortion is at a larger scale than the primary noise (otherwise it just adds high-freq
        // hash and looks similar to plain perlin).
        float wf = frequency * 0.5f;
        Vector3 warp = new Vector3(
            Sample(p, wf, seed + 101),
            Sample(p, wf, seed + 202),
            Sample(p, wf, seed + 303)) * warpStrength;
        return Fbm(p + warp, frequency, seed);
    }

    // -------------------------------------------------------------------------
    // Cellular (Worley-ish)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Voronoi-style cellular noise: returns the distance to the nearest jittered grid point,
    /// remapped to roughly [-1, 1]. Gives walls a chunky, broken-rock look.
    /// </summary>
    public static float Cellular(Vector3 p, float frequency, int seed)
    {
        Vector3 q = p * frequency;
        Vector3 cell = new Vector3(Mathf.Floor(q.x), Mathf.Floor(q.y), Mathf.Floor(q.z));
        Vector3 frac = q - cell;

        float minDist = 999f;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Vector3 neighbour = new Vector3(cell.x + dx, cell.y + dy, cell.z + dz);
            // Per-cell deterministic jitter offset in [0, 1)^3.
            Vector3 jitter = Hash3(neighbour, seed);
            Vector3 point = new Vector3(dx, dy, dz) + jitter;
            float d2 = (point - frac).sqrMagnitude;
            if (d2 < minDist) minDist = d2;
        }
        // sqrt(3) ≈ 1.73 is the worst-case distance; squash to [0,1] then to [-1,1].
        float d = Mathf.Sqrt(minDist) / 1.73f;
        return d * 2f - 1f;
    }

    // -------------------------------------------------------------------------
    // Hashes
    // -------------------------------------------------------------------------

    static Vector3 SeedOffset(int seed)
    {
        return new Vector3(
            (seed * 0.7531f) % 1000f,
            (seed * 1.3729f) % 1000f,
            (seed * 2.1313f) % 1000f);
    }

    static Vector3 Hash3(Vector3 cell, int seed)
    {
        // Cheap deterministic hash → 3 floats in [0, 1). No allocations, no Mathf.PerlinNoise.
        unchecked
        {
            int ix = (int)cell.x;
            int iy = (int)cell.y;
            int iz = (int)cell.z;
            int h = ix * 374761393 + iy * 668265263 + iz * 1274126177 + seed * 951274097;
            h = (h ^ (h >> 13)) * 1274126177;
            int h2 = (h ^ (h >> 17)) * 374761393;
            int h3 = (h2 ^ (h2 >> 11)) * 668265263;
            return new Vector3(
                (h  & 0x7FFFFFFF) / (float)int.MaxValue,
                (h2 & 0x7FFFFFFF) / (float)int.MaxValue,
                (h3 & 0x7FFFFFFF) / (float)int.MaxValue);
        }
    }
}
