using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>
    /// Seeded fractal noise for the sheet: worn ink, paper grain and crease relief. Deterministic,
    /// so a poster baked twice from the same definition comes out the same.
    /// </summary>
    internal readonly struct PosterNoise
    {
        private const int Octaves = 4;
        private readonly Vector2 offset;

        public PosterNoise(int seed)
        {
            var random = new System.Random(seed);
            offset = new Vector2((float)random.NextDouble() * 1000f, (float)random.NextDouble() * 1000f);
        }

        /// <summary>Fractal noise, roughly 0..1, at <paramref name="x"/>, <paramref name="y"/> in units of the base feature size.</summary>
        public float Fractal(float x, float y)
        {
            float sum = 0f, amplitude = 0.5f, norm = 0f, frequency = 1f;
            for (int i = 0; i < Octaves; i++)
            {
                sum += amplitude * Mathf.PerlinNoise(offset.x + x * frequency, offset.y + y * frequency);
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }

            return sum / norm;
        }

        /// <summary>Ridged noise, 0..1: sharp folds where the plain noise crosses its middle, like creased paper.</summary>
        public float Ridged(float x, float y)
        {
            float sum = 0f, amplitude = 0.5f, norm = 0f, frequency = 1f;
            for (int i = 0; i < Octaves; i++)
            {
                float n = Mathf.PerlinNoise(offset.x + x * frequency, offset.y + y * frequency);
                sum += amplitude * Mathf.Abs(n * 2f - 1f);
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }

            return sum / norm;
        }

        /// <summary>A stable seed from a name, so each poster gets its own creases and stain.</summary>
        public static int SeedOf(string name)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in name ?? string.Empty) hash = (hash ^ c) * 16777619;
                return hash;
            }
        }
    }
}
