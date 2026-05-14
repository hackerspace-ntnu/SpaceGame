using UnityEngine;

/// <summary>
/// Cheap 3D value-noise built from sums of Unity's 2D <see cref="Mathf.PerlinNoise"/>.
/// Three orthogonal Perlin lookups summed give a passable 3D field with no extra dependencies.
/// Used to ripple the cave walls so they don't look like obvious spheres and capsules.
/// </summary>
public static class NoiseDistortion
{
    /// <summary>Returns roughly [-1, 1] noise value at the given world position.</summary>
    public static float Sample(Vector3 p, float frequency, int seed)
    {
        // Seed offsets so different generated caves don't look identical.
        Vector3 o = new Vector3(
            (seed * 0.7531f) % 1000f,
            (seed * 1.3729f) % 1000f,
            (seed * 2.1313f) % 1000f);

        float x = (p.x + o.x) * frequency;
        float y = (p.y + o.y) * frequency;
        float z = (p.z + o.z) * frequency;

        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);

        // Average → [0, 1], then remap to [-1, 1].
        return ((xy + yz + xz) / 3f) * 2f - 1f;
    }

    /// <summary>2-octave fbm for a bit more visual variety.</summary>
    public static float Fbm(Vector3 p, float frequency, int seed)
    {
        float a = Sample(p, frequency, seed);
        float b = Sample(p, frequency * 2.13f, seed + 17) * 0.5f;
        return (a + b) / 1.5f;
    }
}
