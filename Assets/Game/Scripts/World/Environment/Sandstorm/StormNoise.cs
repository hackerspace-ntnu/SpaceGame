// Wander and gusts for a storm, reproducible on every machine in the session.
//
// Mathf.PerlinNoise would have done the job visually, but it carries no cross-platform promise:
// two clients could legitimately disagree about where a storm is, and a storm's position is what
// decides who is taking damage. This is a handful of integer operations with an exactly defined
// result, which is both a correctness fix and the reason the determinism test can be written.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    public static class StormNoise
    {
        /// <summary>Smooth noise in 0..1, wandering roughly once per unit of <paramref name="t"/>.</summary>
        public static float Value(uint seed, float t)
        {
            float floored = Mathf.Floor(t);
            uint cell = unchecked((uint)(int)floored);
            float fraction = t - floored;

            float a = Hash01(cell, seed);
            float b = Hash01(cell + 1u, seed);

            // Smoothstep rather than a straight lerp: a linear ramp between lattice points gives
            // the wind a visible kink every period, which reads as a glitch rather than weather.
            float eased = fraction * fraction * (3f - 2f * fraction);
            return Mathf.Lerp(a, b, eased);
        }

        /// <summary>The same noise mapped to -1..1, for anything that wanders either side of zero.</summary>
        public static float Signed(uint seed, float t) => Value(seed, t) * 2f - 1f;

        // A standard integer avalanche hash. The constants are the point: they mix every input bit
        // into every output bit, so seeds one apart give completely unrelated storms.
        private static float Hash01(uint cell, uint seed)
        {
            unchecked
            {
                uint x = cell * 747796405u + seed * 2891336453u + 1376312589u;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;

                // Top 24 bits only: float has 24 bits of mantissa, so the low byte would be noise
                // that survives on some platforms and rounds away on others.
                return (x >> 8) / 16777216f;
            }
        }
    }
}
