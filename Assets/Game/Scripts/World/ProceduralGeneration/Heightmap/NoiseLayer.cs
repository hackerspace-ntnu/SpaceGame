using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// One named field of fractal Perlin noise, sampled in world metres and normalised to 0..1.
    /// <see cref="NoiseHeightmapGenerator"/> stacks several of these (continentalness, erosion,
    /// peaks and valleys, hills, detail, warp), each with its own octaves and seed salt, the same way
    /// the procedural-playground <c>Noise</c> object does.
    /// </summary>
    [Serializable]
    public class NoiseLayer
    {
        [Tooltip("Metres covered by one cycle of the first octave. Bigger = broader shapes.")]
        [Min(1f)] public float wavelength = 1000f;

        [Tooltip("Octaves summed, each at lacunarity x the previous frequency.")]
        [Range(1, 8)] public int octaves = 4;

        [Tooltip("Amplitude multiplier per octave. Lower = smoother, higher = rougher.")]
        [Range(0f, 1f)] public float persistence = 0.5f;

        [Tooltip("Frequency multiplier per octave.")]
        [Min(1f)] public float lacunarity = 2f;

        [Tooltip("Stretches the octave sum, which bunches around 0.5, back toward the full 0..1 range.")]
        [Min(0f)] public float contrast = 1.5f;

        [Tooltip("Power applied to the final 0..1 value. >1 widens the lows, <1 widens the highs.")]
        [Min(0.01f)] public float exponent = 1f;

        [Tooltip("Mixed into the generator's seed so every layer gets its own independent field.")]
        public int seedSalt;

        // Mathf.PerlinNoise loses precision far from the origin; offsets stay inside this many cells.
        const float SeedOffsetRange = 1000f;

        /// <summary>Noise at world XZ in metres, 0..1.</summary>
        public float Sample(float x, float z, int seed)
        {
            float frequency = 1f / wavelength;
            float amplitude = 1f;
            float sum = 0f;
            float amplitudeSum = 0f;

            for (int o = 0; o < octaves; o++)
            {
                uint hash = Hash((uint)seed, (uint)seedSalt, (uint)o);
                float offsetX = (hash & 0xFFFF) / 65535f * SeedOffsetRange;
                float offsetZ = (hash >> 16) / 65535f * SeedOffsetRange;

                sum += amplitude * Mathf.PerlinNoise(x * frequency + offsetX, z * frequency + offsetZ);
                amplitudeSum += amplitude;
                frequency *= lacunarity;
                amplitude *= persistence;
            }

            float normalised = Mathf.Clamp01((sum / amplitudeSum - 0.5f) * contrast + 0.5f);
            return Mathf.Pow(normalised, exponent);
        }

        static uint Hash(uint a, uint b, uint c)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u ^ b * 0x85EBCA77u ^ c * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
