// Deterministic randomness for rosters: the same group draws the same people and the same guns
// every time it spawns. A caravan that folds and re-spawns while you watch must come back as the
// caravan you saw. Pure, so it is tested without a scene.
using System.Collections.Generic;

namespace SpaceGame.Agents
{
    public static class RosterDraw
    {
        /// <summary>A well-mixed 32-bit hash of (seed, index). Not cryptographic; stable across runs and platforms.</summary>
        public static uint Hash(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ ((uint)index + 0x7F4A7C15u) * 0x85EBCA77u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>A roll in [0, 1).</summary>
        public static double Roll01(int seed, int index) => Hash(seed, index) / 4294967296.0;

        /// <summary>An index in [0, count), or -1 when there is nothing to pick from.</summary>
        public static int IndexFor(int seed, int index, int count) =>
            count <= 0 ? -1 : (int)(Hash(seed, index) % (uint)count);

        /// <summary>
        /// The slot <paramref name="roll01"/> lands in, weighting each slot by its value. Zero and
        /// negative weights are never picked; -1 when no weight is positive.
        /// </summary>
        public static int PickWeighted(IReadOnlyList<float> weights, double roll01)
        {
            double total = 0d;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0f) total += weights[i];

            if (total <= 0d) return -1;

            double target = roll01 * total;
            double accumulated = 0d;

            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;

                accumulated += weights[i];
                if (target < accumulated) return i;
            }

            // roll01 is below 1, so only floating-point rounding reaches here: the last positive slot.
            for (int i = weights.Count - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;

            return -1;
        }

        /// <summary>
        /// FNV-1a over a string. Used instead of <c>string.GetHashCode</c>, which .NET is free to
        /// randomise per process — and a seed that changes between sessions is no seed at all.
        /// </summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        h ^= c;
                        h *= 16777619u;
                    }
                }
                return (int)h;
            }
        }
    }
}
